using Serilog;
using System;

namespace CfMes
{
    using static Program;

    /// <summary>
    /// Class for shift control.
    /// </summary>
    public class ShiftControl : IShiftControl, IDisposable
    {
        public const int DAYS_PER_WEEK_MIN = 1;
        public const int DAYS_PER_WEEK_MAX = 7;
        public const int SHIFT_COUNT_MIN = 1;
        public const int SHIFT_COUNT_MAX = 288;
        public const int FIRST_SHIFT_START_MIN = 0;
        public const int FIRST_SHIFT_START_MAX = 2400;
        public const int SHIFT_LENGTH_MINUTES_MIN = 5;
        public const int SHIFT_LENGTH_MINUTES_MAX = 1440;
        public const double SHIFT_SHOULD_START_LIMIT_PERCENT_MIN = 0;
        public const double SHIFT_SHOULD_START_LIMIT_PERCENT_MAX = 1;

        public ShiftControl(ILogger logger, int daysPerWeek, int shiftCount, int firstShiftStart, int shiftLength, double shiftShouldStartLimitPercent)
        {
            _logger = logger;
            ValidateConfiguration(daysPerWeek, shiftCount, firstShiftStart, shiftLength, shiftShouldStartLimitPercent);
        }

        /// <summary>
        /// Implement IDisposable.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
        }

        /// <summary>
        /// Implement IDisposable.
        /// </summary>
        public void Dispose()
        {
            // do cleanup
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void ValidateConfiguration(int daysPerWeek, int shiftCount, int firstShiftStart, int shiftLengthInMinutes, double shiftShouldStartLimitPercent)
        {
            // sanity check parameters
            if (daysPerWeek < DAYS_PER_WEEK_MIN || daysPerWeek > DAYS_PER_WEEK_MAX)
            {
                throw new Exception($"The days per week must be between {DAYS_PER_WEEK_MIN} and {DAYS_PER_WEEK_MAX}. Please adjust.");
            }
            if (shiftCount < SHIFT_COUNT_MIN || shiftCount > SHIFT_COUNT_MAX)
            {
                throw new Exception($"The shift count must be between {SHIFT_COUNT_MIN} and {SHIFT_COUNT_MAX}. Please adjust.");
            }
            if (firstShiftStart < FIRST_SHIFT_START_MIN || firstShiftStart > FIRST_SHIFT_START_MAX || firstShiftStart / 100 > 24 || firstShiftStart % 100 > 59)
            {
                throw new Exception($"The start of the first shift must be between {FIRST_SHIFT_START_MIN} and {FIRST_SHIFT_START_MAX} in hhmm format. Please adjust.");
            }
            if (SHIFT_LENGTH_MINUTES_MIN < 5 || SHIFT_LENGTH_MINUTES_MAX > 24 * 60)
            {
                throw new Exception($"The shift length in minutes must be between {SHIFT_LENGTH_MINUTES_MIN} and {SHIFT_LENGTH_MINUTES_MAX}. Please adjust.");
            }
            if (shiftShouldStartLimitPercent < SHIFT_SHOULD_START_LIMIT_PERCENT_MIN || shiftShouldStartLimitPercent > SHIFT_SHOULD_START_LIMIT_PERCENT_MAX)
            {
                throw new Exception($"The shift should start limit in percent must be larger than {SHIFT_SHOULD_START_LIMIT_PERCENT_MIN} and lower than {SHIFT_SHOULD_START_LIMIT_PERCENT_MAX}. Please adjust.");
            }

            // check if all shifts are <= 24hrs
            if (shiftCount * shiftLengthInMinutes > 24 * 60)
            {
                throw new Exception("The total length of all shifts is longer than a day. Please adjust shift length and/or shift count to fit in a day.");
            }
            _firstShiftStart = new TimeSpan(firstShiftStart / 100, firstShiftStart % 100, 0);

            // check if only the last shift overflows into the next day
            DateTime todaysShiftStart = DateTime.Today + _firstShiftStart;
            if (shiftCount > 1)
            {
                TimeSpan len = TimeSpan.FromMinutes((shiftCount - 1) * shiftLengthInMinutes);
                DateTime end = todaysShiftStart.Add(len);
                if (end.Day > DateTime.Now.Day)
                {
                    throw new Exception("Only the last shift is allowed to overflow into the next day. Please adjust shift count and/or shift length.");
                }
            }
            _daysPerWeek = daysPerWeek;
            _shiftCount = shiftCount;
            _shiftShouldStartLimitPercent = shiftShouldStartLimitPercent;
            _shiftLength = TimeSpan.FromMinutes(shiftLengthInMinutes);
            _dayShiftLength = new TimeSpan(0, shiftLengthInMinutes * _shiftCount, 0);
            _shiftShouldStart = new TimeSpan(0, (int)(shiftLengthInMinutes * _shiftShouldStartLimitPercent), 0);
            _shiftOverflow = todaysShiftStart.Add(_dayShiftLength).Day > DateTime.Now.Day;
        }

        public int CurrentShift(out DateTime nextShiftStart)
        {
            DateTime now = Now;
            var today = new DateTime(now.Year, now.Month, now.Day);
            var yesterday = today - TimeSpan.FromDays(1);
            var tomorrow = today + TimeSpan.FromDays(1);

            var dayOfWeek = now.DayOfWeek;
            // check if we are in a day with shifts.
            if (_daysPerWeek == 7 || (dayOfWeek > 0 && dayOfWeek <= (DayOfWeek)_daysPerWeek))
            {
                // we are in a day with shifts. check time if we are in shift or have to wait for next shift to start.
                // if we are in a shift we still wait for the next shift if the next shift start is already close.
                var todaysFirstShiftStart = today + _firstShiftStart;
                var todaysLastShiftShouldStart = todaysFirstShiftStart + _dayShiftLength - _shiftLength + _shiftShouldStart;
                var yesterdaysLastShiftStart = yesterday + _firstShiftStart + _dayShiftLength - _shiftLength;
                var yesterdaysLastShiftShouldStart = yesterdaysLastShiftStart + _shiftShouldStart;

                // we are not yet beyond the start of the first shift.
                if (now < todaysFirstShiftStart)
                {
                    if (_shiftOverflow) {
                        if (now <= yesterdaysLastShiftShouldStart)
                        {
                            // we are early in yesterday last shift, so we go ahead an start it.
                            nextShiftStart = now;
                            return _shiftCount;
                        }
                    }
                    // we wait for the start of todays first shift.
                    nextShiftStart = todaysFirstShiftStart;
                    return 0;
                }

                // we are after the last shift of today.
                if (now > todaysLastShiftShouldStart)
                {
                    // wait till the first shift of tomorrow.
                    nextShiftStart = tomorrow + _firstShiftStart;
                    return 0;
                }

                // we are inside a shift. check in which one we are.
                for (var i = 0; i < _shiftCount; i++)
                {
                    var shiftStart = today + _firstShiftStart + (i * _shiftLength);
                    var shiftEnd = shiftStart + _shiftLength;
                    var shiftShouldStart = shiftStart + _shiftShouldStart;
                    // check if we are in the shift to inspect.
                    if (now >= shiftStart && now < shiftEnd)
                    {
                        // check if we should still start the current shift or wait for the next shift.
                        if (now <= shiftShouldStart)
                        {
                            // start the current shift.
                            nextShiftStart = now;
                            return i + 1;
                        }
                        nextShiftStart = shiftEnd;
                        return 0;
                    }
                }
            }

            // we are in a day without shifts. lets wait for the first shift of next monday.
            int daysTillMonday;
            if (dayOfWeek == DayOfWeek.Sunday)
            {
                daysTillMonday = 1;
            }
            else
            {
                daysTillMonday = 8 - (int)dayOfWeek;
            }
            nextShiftStart = today + TimeSpan.FromDays(daysTillMonday) + _firstShiftStart;
            return 0;
        }

        // enables to set a current time for unit testing.
        public void SetShiftControlNow(DateTime now)
        {
            _shiftControlNow = now;
        }

        private DateTime Now
        {
            get
            {
                return _shiftControlNow != default ? _shiftControlNow : DateTime.Now;
            }
        }

        private readonly ILogger _logger;
        // indicates that the shifts ends in the next day
        private static bool _shiftOverflow;
        // overall length of all shifts per day
        private static TimeSpan _dayShiftLength;
        // length of a shift
        private static TimeSpan _shiftLength;
        // number of shifts per day
        private static int _shiftCount;
        // number of days with shifts starting on monday
        private static int _daysPerWeek;
        // time into the day when the first shift starts
        private static TimeSpan _firstShiftStart;
        // if now is already in a shift, this is the limit in percent were we still start it.
        private static double _shiftShouldStartLimitPercent;
        // time when the shift should still start even the current time is already in a shift
        private static TimeSpan _shiftShouldStart;
        // allows to set current time for testing
        private static DateTime _shiftControlNow = default;
    }
}
