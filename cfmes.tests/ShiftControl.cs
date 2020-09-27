using CfMes;
using Org.BouncyCastle.Utilities;
using Serilog;
using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace cfmes.tests
{
    public class ShiftControlTests
    {
        public ShiftControlTests()
        {
            var _loggerConfiguration = new LoggerConfiguration();
            _loggerConfiguration.MinimumLevel.Information();
            _loggerConfiguration.WriteTo.Console();
            _logger = _loggerConfiguration.CreateLogger();
        }

        public bool CreateShiftControl(int daysPerWeek = DAYSPERWEEK_DEFAULT, int shiftCount = SHIFTCOUNT_DEFAULT, int firstShiftStart = FIRSTSHIFTSTART_DEFAULT, int shiftLength = SHIFTLEN_DEFAULT, double shiftShouldStartLimitPercent = SHIFTSHOULDSTARTLIMITPERCENT_DEFAULT)
        {
            try
            {
                new ShiftControl(_logger, daysPerWeek, shiftCount, firstShiftStart, shiftLength, shiftShouldStartLimitPercent);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public IShiftControl CreateAndGetShiftControl(int daysPerWeek = DAYSPERWEEK_DEFAULT, int shiftCount = SHIFTCOUNT_DEFAULT, int firstShiftStart = FIRSTSHIFTSTART_DEFAULT, int shiftLength = SHIFTLEN_DEFAULT, double shiftShouldStartLimitPercent = SHIFTSHOULDSTARTLIMITPERCENT_DEFAULT)
        {
            return new ShiftControl(_logger, daysPerWeek, shiftCount, firstShiftStart, shiftLength, shiftShouldStartLimitPercent);
        }

        [Fact]
        public void ValidateParameters()
        {
            Assert.True(CreateShiftControl());
            Assert.False(CreateShiftControl(DAYSPERWEEK_NEG));
            Assert.False(CreateShiftControl(DAYSPERWEEK_0));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1));
            Assert.True(CreateShiftControl(DAYSPERWEEK_2));
            Assert.True(CreateShiftControl(DAYSPERWEEK_3));
            Assert.True(CreateShiftControl(DAYSPERWEEK_4));
            Assert.True(CreateShiftControl(DAYSPERWEEK_5));
            Assert.True(CreateShiftControl(DAYSPERWEEK_6));
            Assert.True(CreateShiftControl(DAYSPERWEEK_7));
            Assert.False(CreateShiftControl(DAYSPERWEEK_8));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_NEG));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_0));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_3));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_4));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_24));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_25));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_NEG));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_0000));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_0001));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_0559));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_0600));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_0601));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_1200));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_1800));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_1759));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_2345));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_2350));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_2359));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_2500));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_1270));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_NEG));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_0m));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_1m));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_4m));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_5m));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_6m));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_0h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_1h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_4h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_6h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_8h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_24h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_1, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_25h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_DEFAULT, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_DEFAULT, SHIFTSHOULDSTARTLIMITPERCENT_NEG));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_DEFAULT, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_DEFAULT, SHIFTSHOULDSTARTLIMITPERCENT_0));
            Assert.True(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_DEFAULT, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_DEFAULT, SHIFTSHOULDSTARTLIMITPERCENT_0_5));
            Assert.False(CreateShiftControl(DAYSPERWEEK_DEFAULT, SHIFTCOUNT_DEFAULT, FIRSTSHIFTSTART_DEFAULT, SHIFTLEN_DEFAULT, SHIFTSHOULDSTARTLIMITPERCENT_1_1));
        }


        [Fact]
        public void OverallShiftLengthLarger24HoursPerDay()
        {
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_0000, SHIFTLEN_24h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_0000, SHIFTLEN_25h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_1200, SHIFTLEN_24h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_1200, SHIFTLEN_25h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_2359, SHIFTLEN_24h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_2359, SHIFTLEN_25h));
        }

        [Fact]
        public void OverallShiftLengthEqual24HoursPerDay()
        {
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_0000, SHIFTLEN_24h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_1200, SHIFTLEN_24h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_2359, SHIFTLEN_24h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_3, FIRSTSHIFTSTART_0000, SHIFTLEN_8h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_3, FIRSTSHIFTSTART_1200, SHIFTLEN_4h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_3, FIRSTSHIFTSTART_2359, SHIFTLEN_8h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_0000, SHIFTLEN_25h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_1200, SHIFTLEN_25h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_2359, SHIFTLEN_25h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_0000, SHIFTLEN_8h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_1200, SHIFTLEN_4h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_2359, SHIFTLEN_8h));
        }

        [Fact]
        public void OnlyLastShiftOverflowsToNextDay()
        {
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_0001, SHIFTLEN_24h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_1, FIRSTSHIFTSTART_0600, SHIFTLEN_24h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_0000, SHIFTLEN_6h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_0001, SHIFTLEN_6h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_0559, SHIFTLEN_6h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_0600, SHIFTLEN_6h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_0601, SHIFTLEN_6h));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_4, FIRSTSHIFTSTART_1200, SHIFTLEN_6h));
            Assert.True(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_3, FIRSTSHIFTSTART_2345, SHIFTLEN_5m));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_3, FIRSTSHIFTSTART_2350, SHIFTLEN_5m));
            Assert.False(CreateShiftControl(DAYSPERWEEK_1, SHIFTCOUNT_3, FIRSTSHIFTSTART_2359, SHIFTLEN_5m));
        }


        [Fact]
        public void BeforeFirstShiftInLastShiftOfYesterday()
        {
            DateTime nextShiftStart = default;
            var today = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_TUESDAY);
            // todo: cleanup
            var yesterday = today - TimeSpan.FromDays(1);
            var daysPerWeek = DAYSPERWEEK_7;
            var shiftLengthInMinutes = SHIFTLEN_8h;
            var shiftCount = SHIFTCOUNT_3;
            var shiftShouldStartLimitPercent = SHIFTSHOULDSTARTLIMITPERCENT_0_5;
            var firstShiftStartHhMm = FIRSTSHIFTSTART_0600;
            var firstShiftStart = new TimeSpan(firstShiftStartHhMm / 100, firstShiftStartHhMm % 100, 0);
            var dayShiftLength = new TimeSpan(0, shiftLengthInMinutes * shiftCount, 0);
            var shiftShouldStart = new TimeSpan(0, (int)(shiftLengthInMinutes * shiftShouldStartLimitPercent), 0);
            var shiftLength = new TimeSpan(0, shiftLengthInMinutes, 0);
            var todaysFirstShiftStart = today + firstShiftStart;
            IShiftControl sc;
            Assert.NotNull(sc = CreateAndGetShiftControl(daysPerWeek, shiftCount, firstShiftStartHhMm, shiftLengthInMinutes));
            var now = today + TimeSpan.FromHours(0) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == shiftCount);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == shiftCount);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(1);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == todaysFirstShiftStart);
            now = today + TimeSpan.FromHours(5) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == todaysFirstShiftStart);
        }

        [Fact]
        public void BeforeFirstShiftAfterLastShiftOfYesterday()
        {
            var today = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_TUESDAY);
            var daysPerWeek = DAYSPERWEEK_7;
            var shiftLengthInMinutes = SHIFTLEN_6h;
            var shiftCount = SHIFTCOUNT_3;
            var firstShiftStartHhMm = FIRSTSHIFTSTART_0600;
            var firstShiftStart = new TimeSpan(firstShiftStartHhMm / 100, firstShiftStartHhMm % 100, 0);
            var todaysFirstShiftStart = today + firstShiftStart;
            IShiftControl sc;
            Assert.NotNull(sc = CreateAndGetShiftControl(daysPerWeek, shiftCount, firstShiftStartHhMm, shiftLengthInMinutes));
            var now = today + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out DateTime nextShiftStart) == 0);
            Assert.True(nextShiftStart == todaysFirstShiftStart);
            now = today + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == todaysFirstShiftStart);
            now = today + TimeSpan.FromHours(5) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == todaysFirstShiftStart);
        }

        [Fact]
        public void InShift()
        {
            var today = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_TUESDAY);
            var tomorrow = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_WEDNESDAY);
            var daysPerWeek = DAYSPERWEEK_7;
            var shiftLengthInMinutes = SHIFTLEN_8h;
            var shiftCount = SHIFTCOUNT_3;
            var firstShiftStartHhMm = FIRSTSHIFTSTART_0600;
            var firstShiftStart = new TimeSpan(firstShiftStartHhMm / 100, firstShiftStartHhMm % 100, 0);
            var shiftLength = new TimeSpan(0, shiftLengthInMinutes, 0);
            var todaysFirstShiftStart = today + firstShiftStart;
            IShiftControl sc;
            Assert.NotNull(sc = CreateAndGetShiftControl(daysPerWeek, shiftCount, firstShiftStartHhMm, shiftLengthInMinutes));
            var now = today + TimeSpan.FromHours(6) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out DateTime nextShiftStart) == 1);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(10) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 1);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(10) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(1);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == (todaysFirstShiftStart + shiftLength));
            now = today + TimeSpan.FromHours(13) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == (todaysFirstShiftStart + shiftLength));
            now = today + TimeSpan.FromHours(14) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 2);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 3);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(24) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 3);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(25) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 3);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(26) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 3);
            Assert.True(nextShiftStart == now);
            now = today + TimeSpan.FromHours(26) + TimeSpan.FromMinutes(0) + TimeSpan.FromSeconds(1);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == tomorrow + firstShiftStart);
            now = today + TimeSpan.FromHours(29) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == tomorrow + firstShiftStart);
        }


        [Fact]
        public void DayWithoutShift()
        {
            var yesterday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_MONDAY);
            var today = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_TUESDAY);
            var tomorrow = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_WEDNESDAY);
            var daysPerWeek = DAYSPERWEEK_1;
            var shiftLengthInMinutes = SHIFTLEN_8h;
            var shiftCount = SHIFTCOUNT_1;
            var firstShiftStartHhMm = FIRSTSHIFTSTART_0600;
            var firstShiftStart = new TimeSpan(firstShiftStartHhMm / 100, firstShiftStartHhMm % 100, 0);
            var shiftLength = new TimeSpan(0, shiftLengthInMinutes, 0);
            var todaysFirstShiftStart = today + firstShiftStart;
            IShiftControl sc;
            Assert.NotNull(sc = CreateAndGetShiftControl(daysPerWeek, shiftCount, firstShiftStartHhMm, shiftLengthInMinutes));
            var now = today + firstShiftStart + TimeSpan.FromHours(0) + TimeSpan.FromMinutes(SHIFTLEN_8h) + TimeSpan.FromSeconds(0);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out DateTime nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
            now = today + TimeSpan.FromDays(1);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
            now = today + TimeSpan.FromDays(2);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
            now = today + TimeSpan.FromDays(3);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
            now = today + TimeSpan.FromDays(4);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
            now = today + TimeSpan.FromDays(6);
            sc.SetShiftControlNow(now);
            Assert.True(sc.CurrentShift(out nextShiftStart) == 0);
            Assert.True(nextShiftStart == yesterday + TimeSpan.FromDays(7) + firstShiftStart);
        }
        private readonly ILogger _logger;

        private const int DAYSPERWEEK_NEG = -1;
        private const int DAYSPERWEEK_0 = 0;
        private const int DAYSPERWEEK_1 = 1;
        private const int DAYSPERWEEK_2 = 2;
        private const int DAYSPERWEEK_3 = 3;
        private const int DAYSPERWEEK_4 = 4;
        private const int DAYSPERWEEK_5 = 5;
        private const int DAYSPERWEEK_6 = 6;
        private const int DAYSPERWEEK_7 = 7;
        private const int DAYSPERWEEK_8 = 8;
        private const int DAYSPERWEEK_DEFAULT = DAYSPERWEEK_5;

        private const int SHIFTLEN_NEG = -1;
        private const int SHIFTLEN_0m = 0;
        private const int SHIFTLEN_1m = 1;
        private const int SHIFTLEN_4m = 4;
        private const int SHIFTLEN_5m = 5;
        private const int SHIFTLEN_6m = 6;
        private const int SHIFTLEN_0h = 0;
        private const int SHIFTLEN_1h = 1 * 60;
        private const int SHIFTLEN_4h = 4 * 60;
        private const int SHIFTLEN_6h = 6 * 60;
        private const int SHIFTLEN_8h = 8 * 60;
        private const int SHIFTLEN_24h = 24 * 60;
        private const int SHIFTLEN_25h = 25 * 60;
        private const int SHIFTLEN_DEFAULT = SHIFTLEN_1h;

        private const int SHIFTCOUNT_NEG = -1;
        private const int SHIFTCOUNT_0 = 0;
        private const int SHIFTCOUNT_1 = 1;
        private const int SHIFTCOUNT_3 = 3;
        private const int SHIFTCOUNT_4 = 4;
        private const int SHIFTCOUNT_24 = 24;
        private const int SHIFTCOUNT_25 = 25;
        private const int SHIFTCOUNT_DEFAULT = SHIFTCOUNT_1;

        private const int FIRSTSHIFTSTART_NEG = -1;
        private const int FIRSTSHIFTSTART_0000 = 0;
        private const int FIRSTSHIFTSTART_0001 = 1;
        private const int FIRSTSHIFTSTART_0559 = 559;
        private const int FIRSTSHIFTSTART_0600 = 600;
        private const int FIRSTSHIFTSTART_0601 = 601;
        private const int FIRSTSHIFTSTART_1200 = 1200;
        private const int FIRSTSHIFTSTART_1759 = 1759;
        private const int FIRSTSHIFTSTART_1800 = 1800;
        private const int FIRSTSHIFTSTART_2345 = 2345;
        private const int FIRSTSHIFTSTART_2350 = 2350;
        private const int FIRSTSHIFTSTART_2359 = 2359;
        private const int FIRSTSHIFTSTART_2500 = 2500;
        private const int FIRSTSHIFTSTART_1270 = 1270;
        private const int FIRSTSHIFTSTART_DEFAULT = FIRSTSHIFTSTART_0000;

        private const double SHIFTSHOULDSTARTLIMITPERCENT_NEG = -0.5;
        private const double SHIFTSHOULDSTARTLIMITPERCENT_0 = 0;
        private const double SHIFTSHOULDSTARTLIMITPERCENT_0_5 = 0.5;
        private const double SHIFTSHOULDSTARTLIMITPERCENT_1_1 = 1.1;
        private const double SHIFTSHOULDSTARTLIMITPERCENT_DEFAULT = SHIFTSHOULDSTARTLIMITPERCENT_0_5;

        private const int TEST_YEAR = 2020;
        private const int TEST_MONTH = 7;
        private const int TEST_DAY_MONDAY = 6;
        private const int TEST_DAY_TUESDAY = 7;
        private const int TEST_DAY_WEDNESDAY = 8;
        private const int TEST_DAY_THURSDAY = 9;
        private const int TEST_DAY_FRIDAY = 10;
        private const int TEST_DAY_SATURDAY = 11;
        private const int TEST_DAY_SUNDAY = 12;
        private DateTime _testMonday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_MONDAY);
        private DateTime _testTuesday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_TUESDAY);
        private DateTime _testWednesday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_WEDNESDAY);
        private DateTime _testThursday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_THURSDAY);
        private DateTime _testFriday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_FRIDAY);
        private DateTime _testSaturday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_SATURDAY);
        private DateTime _testSunday = new DateTime(TEST_YEAR, TEST_MONTH, TEST_DAY_SUNDAY);
    }
}
