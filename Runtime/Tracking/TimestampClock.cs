namespace Emas
{
    /// <summary>Selects how a reference aligns followed creation timestamps with the local presentation clock.</summary>
    public enum TimestampClock
    {
        /// <summary>Recognizes Unix timestamps within one day of calibrated host UTC; otherwise uses the shared simulation clock.</summary>
        Automatic,

        /// <summary>Uses an arbitrary shared simulation epoch and estimates relative age from the least-delayed arrival.</summary>
        Simulation,

        /// <summary>Uses seconds and nanoseconds since 1970-01-01 UTC, measuring packet age against host UTC.</summary>
        /// <remarks>The source and host UTC clocks must agree after ClockOffset calibration. No additional SDK timestamp calls are needed.</remarks>
        Unix
    }
}
