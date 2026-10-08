using System;

namespace Emas
{
    /// <summary>A sample time in a shared SDK clock, preserving whole seconds and nanoseconds separately.</summary>
    /// <remarks>No epoch is assumed. All timestamps in one Realm must use the same clock; this is not Unity game time.</remarks>
    public readonly struct Timestamp : IComparable<Timestamp>, IEquatable<Timestamp>
    {
        /// <summary>Creates a timestamp without converting an absolute epoch to floating point.</summary>
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
        public long Seconds { get; }

        /// <summary>Gets the fractional second as nanoseconds in [0, 1000000000).</summary>
        public uint Nanoseconds { get; }

        /// <summary>Creates a timestamp from finite seconds when separate SDK seconds/nanoseconds are unavailable.</summary>
        /// <remarks>For large epochs, use the constructor to preserve the original nanosecond precision.</remarks>
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
        public double ElapsedSince(Timestamp other)
        {
            double seconds;
            try
            {
                seconds = checked(Seconds - other.Seconds);
            }
            catch (OverflowException)
            {
                seconds = (double)Seconds - other.Seconds;
            }

            return seconds + ((long)Nanoseconds - other.Nanoseconds) * 1e-9;
        }

        /// <summary>Compares chronological order in the shared SDK clock.</summary>
        public int CompareTo(Timestamp other)
        {
            int seconds = Seconds.CompareTo(other.Seconds);
            return seconds != 0 ? seconds : Nanoseconds.CompareTo(other.Nanoseconds);
        }

        /// <summary>Compares both timestamp parts exactly.</summary>
        public bool Equals(Timestamp other)
        {
            return Seconds == other.Seconds && Nanoseconds == other.Nanoseconds;
        }

        /// <summary>Compares another object with this timestamp.</summary>
        public override bool Equals(object obj)
        {
            return obj is Timestamp other && Equals(other);
        }

        /// <summary>Returns a hash of the two timestamp parts.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Seconds.GetHashCode() * 397) ^ Nanoseconds.GetHashCode();
            }
        }
    }
}
