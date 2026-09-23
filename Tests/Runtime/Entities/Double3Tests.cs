using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies double-precision positions and conversion of relative offsets to Unity.
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
            Assert.That(SpatialMath.TryToVector3(offset, out Vector3 unity), Is.True);
            Assert.That(unity, Is.EqualTo(new Vector3(20.25f, 4.25f, -0.75f)));
            Assert.That(origin + offset, Is.EqualTo(position));
        }

        /// <summary>
        /// Scalars on either side scale every coordinate.
        /// </summary>
        [Test]
        public void Multiplication_ScalesEveryCoordinate()
        {
            Double3 value = new Double3(1.25d, -2.5d, 4d);
            Double3 expected = new Double3(2.5d, -5d, 8d);

            Assert.That(value * 2d, Is.EqualTo(expected));
            Assert.That(2d * value, Is.EqualTo(expected));
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
        public void Distance_ScalesTheMagnitudeCalculation()
        {
            Assert.That(Double3.Distance(default, new Double3(3e200, 4e200, 0d)), Is.EqualTo(5e200).Within(1e185));
            Assert.That(Double3.Distance(default, new Double3(3e-200, 4e-200, 0d)), Is.EqualTo(5e-200).Within(1e-215));
            Assert.That(Double3.Distance(new Double3(double.MaxValue, 0d, 0d), new Double3(-double.MaxValue, 0d, 0d)),
                Is.EqualTo(double.PositiveInfinity));
        }

        /// <summary>
        /// Equality and hashing use all three coordinates, including fractional detail.
        /// </summary>
        [Test]
        public void Equality_UsesAllCoordinates()
        {
            Double3 value = new Double3(1e12 + 0.125d, 2d, 3d);
            Double3 equal = new Double3(value.X, value.Y, value.Z);
            Double3 different = new Double3(1e12 + 0.25d, 2d, 3d);

            Assert.That(value.Equals(equal), Is.True);
            Assert.That(value.Equals((object)equal), Is.True);
            Assert.That(value.Equals(null), Is.False);
            Assert.That(value.Equals("position"), Is.False);
            Assert.That(value == equal, Is.True);
            Assert.That(value != different, Is.True);
            Assert.That(value.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
            Assert.That(value != new Double3(value.X, 5d, 3d), Is.True);
            Assert.That(value != new Double3(value.X, 2d, 5d), Is.True);
            Assert.That(default(Double3), Is.EqualTo(new Double3(0d, 0d, 0d)));
        }

        /// <summary>
        /// Diagnostic text retains double precision regardless of the application's culture.
        /// </summary>
        [Test]
        public void ToString_PreservesPrecisionWithInvariantFormatting()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Assert.That(new Double3(1e12 + 0.125d, 2.5d, -3.25d).ToString(),
                    Is.EqualTo("(1000000000000.125, 2.5, -3.25)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// Quaternion normalization remains valid for the full finite float range.
        /// </summary>
        [Test]
        public void NormalizeRotation_HandlesLargeAndTinyComponents()
        {
            Quaternion large = SpatialMath.NormalizeRotation(new Quaternion(0f, float.MaxValue, 0f, float.MaxValue), "rotation");
            Quaternion tiny = SpatialMath.NormalizeRotation(new Quaternion(0f, 0f, 0f, float.Epsilon), "rotation");

            Assert.That(large.y, Is.EqualTo((float)Math.Sqrt(0.5d)));
            Assert.That(large.w, Is.EqualTo(large.y));
            Assert.That(tiny, Is.EqualTo(Quaternion.identity));
        }

        /// <summary>
        /// Invalid rotations are rejected before they can contaminate projected transforms.
        /// </summary>
        [Test]
        public void NormalizeRotation_RejectsInvalidComponentsAndZeroLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.NormalizeRotation(default, "rotation"));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.NormalizeRotation(new Quaternion(float.NaN, 0f, 0f, 1f), "rotation"));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.NormalizeRotation(new Quaternion(0f, float.PositiveInfinity, 0f, 1f), "rotation"));
        }

        /// <summary>
        /// Rotation keeps double fractional detail and compensates for float quaternion normalization error.
        /// </summary>
        [Test]
        public void Rotate_PreservesDoubleCoordinatesThroughAQuarterTurn()
        {
            Quaternion rotation = SpatialMath.NormalizeRotation(new Quaternion(0f, 1f, 0f, 1f), "rotation");
            Double3 value = new Double3(1e12 + 0.125d, 3.25d, 7.5d);
            Double3 rotated = SpatialMath.Rotate(rotation, value);

            Assert.That(rotated.X, Is.EqualTo(7.5d));
            Assert.That(rotated.Y, Is.EqualTo(3.25d));
            Assert.That(rotated.Z, Is.EqualTo(-1e12 - 0.125d));
            Assert.That(SpatialMath.Rotate(Quaternion.Inverse(rotation), rotated), Is.EqualTo(value));
            Assert.That(SpatialMath.Rotate(Quaternion.identity, value), Is.EqualTo(value));
        }

        /// <summary>
        /// Values too large for a finite Unity transform are rejected instead of becoming infinity.
        /// </summary>
        [Test]
        public void TryToVector3_RejectsOffsetsOutsideFloatRange()
        {
            Assert.That(SpatialMath.TryToVector3(new Double3((double)float.MaxValue * 2d, 0d, 0d), out Vector3 rejected), Is.False);
            Assert.That(rejected, Is.EqualTo(Vector3.zero));
            Assert.That(SpatialMath.TryToVector3(new Double3(0d, -float.MaxValue, 0d), out Vector3 accepted), Is.True);
            Assert.That(accepted.y, Is.EqualTo(-float.MaxValue));
            Assert.That(SpatialMath.IsFinite(accepted), Is.True);
            Assert.That(SpatialMath.IsFinite(new Vector3(0f, 0f, float.NaN)), Is.False);
        }
    }
}
