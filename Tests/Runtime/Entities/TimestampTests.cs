using System;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>Verifies SDK seconds/nanoseconds retain precision for packet ordering and elapsed motion time.</summary>
    public sealed class TimestampTests
    {
        /// <summary>Large epochs preserve nanosecond intervals, second rollover and chronological ordering; malformed fractions fail before ingestion.</summary>
        [Test]
        public void SampleIntervals_PreserveNanosecondsAcrossEpochsAndSecondRollover()
        {
            Timestamp first = new Timestamp(1700000000L, 999999998U);
            Timestamp next = new Timestamp(1700000000L, 999999999U);
            Timestamp rollover = new Timestamp(1700000001L, 0U);
            Assert.That(next.ElapsedSince(first), Is.EqualTo(1e-9).Within(1e-16));
            Assert.That(rollover.ElapsedSince(next), Is.EqualTo(1e-9).Within(1e-16));
            Assert.That(first.CompareTo(next), Is.LessThan(0));
            Assert.That(rollover.CompareTo(next), Is.GreaterThan(0));
            Assert.That(next.Equals(new Timestamp(1700000000L, 999999999U)), Is.True);
            Assert.That(Timestamp.FromSeconds(-0.25).ElapsedSince(new Timestamp(-1, 750000000U)), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Timestamp(0, 1000000000U));
            Assert.Throws<ArgumentOutOfRangeException>(() => Timestamp.FromSeconds(double.PositiveInfinity));
            Assert.That(new Timestamp(long.MaxValue, 0).ElapsedSince(new Timestamp(long.MinValue, 0)),
                Is.GreaterThan(0), "Extremely separated clocks do not overflow integer subtraction.");
        }
    }
}
