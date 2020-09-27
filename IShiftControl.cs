using System;

namespace CfMes
{
    /// <summary>
    /// Interface to for shift management.
    /// </summary>
    public interface IShiftControl
    {
        /// <summary>
        /// Implement IDisposable.
        /// </summary>
        void Dispose();

        int CurrentShift(out DateTime nextShiftStart);

        void SetShiftControlNow(DateTime now);
    }
}
