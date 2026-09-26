using System;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies spatial arithmetic retains useful precision over large and small distances.
    /// </summary>
    public sealed class Double3Tests
    {
        /// <summary>
        /// Coordinate access and subtraction retain nearby fractional differences at large origins.
        /// </summary>
        [Test]
        public void Subtraction_PreservesFractionalOffsetsAtLargeCoordinates()
        {
            Double3 origin = new Double3(1e12 + 0.125d, -1e12 + 0.25d, 1e12 + 0.5d);
            Double3 position = new Double3(1e12 + 20.375d, -1e12 + 4.5d, 1e12 - 0.25d);
            Double3 offset = position - origin;

            Assert.That(offset.X, Is.EqualTo(20.25d));
            Assert.That(offset.Y, Is.EqualTo(4.25d));
            Assert.That(offset.Z, Is.EqualTo(-0.75d));
            Assert.That(origin + offset, Is.EqualTo(position));
        }

        /// <summary>
        /// Invalid input and arithmetic overflow cannot enter spatial state through the public API.
        /// </summary>
        [Test]
        public void Coordinates_RejectNonfiniteValuesAndArithmeticOverflow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Double3(double.NaN, 0d, 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Double3(0d, double.PositiveInfinity, 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Double3(0d, 0d, double.NegativeInfinity));
            Double3 largest = new Double3(double.MaxValue, 0d, 0d);
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = largest + largest; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = largest * 2d; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = default(Double3) * double.NaN; });
        }

        /// <summary>
        /// Distance uses the relative double coordinates, with no float conversion.
        /// </summary>
        [Test]
        public void Distance_UsesNearbyDoubleCoordinates()
        {
            Double3 first = new Double3(1e12 + 0.125d, 1e12 + 0.25d, 0d);
            Double3 second = new Double3(1e12 + 3.125d, 1e12 + 4.25d, 0d);

            Assert.That(Double3.Distance(first, second), Is.EqualTo(5d));
            Assert.That(Double3.Distance(first, first), Is.Zero);
        }

        /// <summary>
        /// Distance avoids overflow or underflow from squaring large or small representable offsets.
        /// </summary>
        [Test]
        public void Distance_HandlesVeryLargeAndVerySmallOffsets()
        {
            Assert.That(Double3.Distance(default, new Double3(3e200, 4e200, 0d)), Is.EqualTo(5e200).Within(1e185));
            Assert.That(Double3.Distance(default, new Double3(3e-200, 4e-200, 0d)), Is.EqualTo(5e-200).Within(1e-215));
            Assert.That(Double3.Distance(new Double3(double.MaxValue, 0d, 0d), new Double3(-double.MaxValue, 0d, 0d)),
                Is.EqualTo(double.PositiveInfinity));
        }

    }
}
