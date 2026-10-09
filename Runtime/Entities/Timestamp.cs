using System;

namespace Emas
{
    /// <summary>A sample time in a shared SDK clock, preserving whole seconds and nanoseconds separately.</summary>
    /// <remarks>No epoch is assumed. All timestamps in one Realm must use the same clock; this is not Unity game time.</remarks>
    public readonly struct Timestamp : IComparable<Timestamp>, IEquatable<Timestamp>
    {
        /// <summary>Creates a timestamp without converting an absolute epoch to floating point.</summary>
        /// <param name="seconds">Whole seconds since the SDK's shared epoch, which may be negative.</param>
        /// <param name="nanoseconds">The nonnegative fractional second, less than one billion nanoseconds.</param>
        /// <exception cref="ArgumentOutOfRangeException">Nanoseconds is at least one billion.</exception>
        public Timestamp(long seconds, uint nanoseconds)
        {
            if (nanoseconds >= 1000000000U)
            {
                throw new ArgumentOutOfRangeException(nameof(nanoseconds), "Nanoseconds must be less than one billion.");
            }

            Seconds = seconds;
            Nanoseconds = nanoseconds;
        }

        /// <summary>Gets whole seconds in the SDK clock.</summary>
        /// <value>The integral second relative to the SDK's epoch.</value>
        public long Seconds { get; }

        /// <summary>Gets the fractional second as nanoseconds in [0, 1000000000).</summary>
        /// <value>The fraction added to <see cref="Seconds"/>; always less than one billion.</value>
        public uint Nanoseconds { get; }

        /// <summary>Creates a timestamp from finite seconds when separate SDK seconds/nanoseconds are unavailable.</summary>
        /// <param name="seconds">Finite SDK time whose whole seconds fit in a signed 64-bit integer.</param>
        /// <returns>The whole-second and fractional-nanosecond representation, truncating sub-nanosecond precision.</returns>
        /// <remarks>For large epochs, use the constructor to preserve the original nanosecond precision.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The time is nonfinite or outside the supported whole-second range.</exception>
        public static Timestamp FromSeconds(double seconds)
        {
            if (!SpatialMath.IsFinite(seconds) || seconds < long.MinValue || seconds >= 9223372036854775808d)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            long whole = (long)Math.Floor(seconds);
            uint nanos = (uint)((seconds - whole) * 1000000000d);
            return new Timestamp(whole, Math.Min(nanos, 999999999U));
        }

        /// <summary>Returns elapsed seconds since another sample, subtracting integer seconds before converting to doubles.</summary>
        /// <param name="other">The sample time to subtract, in the same SDK clock.</param>
        /// <returns>The signed elapsed seconds; negative when this timestamp precedes the supplied timestamp.</returns>
        public double ElapsedSince(Timestamp other)
        {
            double seconds;
            try
            {
                seconds = checked(Seconds - other.Seconds);
            }
            catch (OverflowException)
            {
                // Extremely distant epochs cannot preserve nanosecond precision, but their elapsed time still fits in a double.
                seconds = (double)Seconds - other.Seconds;
            }

            return seconds + ((long)Nanoseconds - other.Nanoseconds) * 1e-9;
        }

        /// <summary>Compares chronological order in the shared SDK clock.</summary>
        /// <param name="other">The timestamp to compare, using the same epoch.</param>
        /// <returns>A negative value, zero, or a positive value when this timestamp is earlier, equal, or later.</returns>
        public int CompareTo(Timestamp other)
        {
            int seconds = Seconds.CompareTo(other.Seconds);
            return seconds != 0 ? seconds : Nanoseconds.CompareTo(other.Nanoseconds);
        }

        /// <summary>Compares both timestamp parts exactly.</summary>
        /// <param name="other">The timestamp to compare.</param>
        /// <returns>True when whole seconds and fractional nanoseconds are both equal.</returns>
        public bool Equals(Timestamp other)
        {
            return Seconds == other.Seconds && Nanoseconds == other.Nanoseconds;
        }

        /// <summary>Compares another object with this timestamp.</summary>
        /// <param name="obj">The object to compare, including null.</param>
        /// <returns>True when the object is a timestamp with exactly equal seconds and nanoseconds.</returns>
        public override bool Equals(object obj)
        {
            return obj is Timestamp other && Equals(other);
        }

        /// <summary>Returns a hash of the two timestamp parts.</summary>
        /// <returns>A hash consistent with exact timestamp equality.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Seconds.GetHashCode() * 397) ^ Nanoseconds.GetHashCode();
            }
        }
    }
}
