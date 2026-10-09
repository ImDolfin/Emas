using System;
using System.Globalization;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Stores a finite three-dimensional position or offset in double precision.
    /// </summary>
    [Serializable]
    public struct Double3 : IEquatable<Double3>
    {
        [Tooltip("X coordinate of this three-dimensional position or offset.")]
        [SerializeField] private double _x;
        [Tooltip("Y coordinate of this three-dimensional position or offset.")]
        [SerializeField] private double _y;
        [Tooltip("Z coordinate of this three-dimensional position or offset.")]
        [SerializeField] private double _z;

        /// <summary>
        /// Creates a position or offset with finite coordinates.
        /// </summary>
        /// <param name="x">The finite X coordinate in the caller's coordinate system.</param>
        /// <param name="y">The finite Y coordinate in the same units as X.</param>
        /// <param name="z">The finite Z coordinate in the same units as X.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A coordinate is NaN or infinite.
        /// </exception>
        public Double3(double x, double y, double z)
        {
            if (!SpatialMath.IsFinite(x))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "A coordinate must be finite.");
            }

            if (!SpatialMath.IsFinite(y))
            {
                throw new ArgumentOutOfRangeException(nameof(y), "A coordinate must be finite.");
            }

            if (!SpatialMath.IsFinite(z))
            {
                throw new ArgumentOutOfRangeException(nameof(z), "A coordinate must be finite.");
            }

            _x = x;
            _y = y;
            _z = z;
        }

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        /// <value>The first coordinate without a change of units or precision.</value>
        public double X => _x;

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        /// <value>The second coordinate without a change of units or precision.</value>
        public double Y => _y;

        /// <summary>
        /// Gets the Z coordinate.
        /// </summary>
        /// <value>The third coordinate without a change of units or precision.</value>
        public double Z => _z;

        /// <summary>
        /// Adds two positions or offsets without converting to float precision.
        /// </summary>
        /// <param name="left">The first vector in a shared coordinate system.</param>
        /// <param name="right">The vector to add, in the same units and axes.</param>
        /// <returns>The component-wise sum.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A resulting coordinate is not finite.</exception>
        public static Double3 operator +(Double3 left, Double3 right)
        {
            return new Double3(left._x + right._x, left._y + right._y, left._z + right._z);
        }

        /// <summary>
        /// Subtracts two positions or offsets without converting to float precision.
        /// </summary>
        /// <param name="left">The position or offset from which to subtract.</param>
        /// <param name="right">The position or offset to subtract, in the same units and axes.</param>
        /// <returns>The component-wise difference.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A resulting coordinate is not finite.</exception>
        public static Double3 operator -(Double3 left, Double3 right)
        {
            return new Double3(left._x - right._x, left._y - right._y, left._z - right._z);
        }

        /// <summary>
        /// Scales a position or offset by a finite scalar.
        /// </summary>
        /// <param name="value">The position or offset to scale.</param>
        /// <param name="scalar">The finite multiplier applied to every coordinate.</param>
        /// <returns>The scaled vector.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The scalar or a resulting coordinate is not finite.</exception>
        public static Double3 operator *(Double3 value, double scalar)
        {
            return new Double3(value._x * scalar, value._y * scalar, value._z * scalar);
        }

        /// <summary>
        /// Scales a position or offset by a finite scalar.
        /// </summary>
        /// <param name="scalar">The finite multiplier applied to every coordinate.</param>
        /// <param name="value">The position or offset to scale.</param>
        /// <returns>The scaled vector.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The scalar or a resulting coordinate is not finite.</exception>
        public static Double3 operator *(double scalar, Double3 value)
        {
            return value * scalar;
        }

        /// <summary>
        /// Calculates distance in coordinate units without converting to float precision.
        /// Returns positive infinity if the distance exceeds the capacity of a double.
        /// </summary>
        /// <param name="left">The first point in a shared coordinate system.</param>
        /// <param name="right">The second point in the same units and axes.</param>
        /// <returns>The nonnegative Euclidean distance, or positive infinity when it exceeds double range.</returns>
        public static double Distance(Double3 left, Double3 right)
        {
            double x = Math.Abs(left._x - right._x);
            double y = Math.Abs(left._y - right._y);
            double z = Math.Abs(left._z - right._z);
            double largest = Math.Max(x, Math.Max(y, z));
            if (largest == 0d || double.IsPositiveInfinity(largest))
            {
                return largest;
            }

            // Scale before squaring so a representable distance does not overflow in the intermediate sum.
            x /= largest;
            y /= largest;
            z /= largest;
            return largest * Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>
        /// Compares coordinates exactly.
        /// </summary>
        /// <param name="other">The position or offset to compare.</param>
        /// <returns>True when each corresponding double has the same value; no distance tolerance is applied.</returns>
        public bool Equals(Double3 other)
        {
            return _x.Equals(other._x) && _y.Equals(other._y) && _z.Equals(other._z);
        }

        /// <summary>
        /// Compares this value with another double-precision position or offset.
        /// </summary>
        /// <param name="obj">The object to compare, including null.</param>
        /// <returns>True when the object is a <see cref="Double3"/> with exactly equal coordinates.</returns>
        public override bool Equals(object obj)
        {
            return obj is Double3 && Equals((Double3)obj);
        }

        /// <summary>
        /// Returns a hash based on all three coordinates.
        /// </summary>
        /// <returns>A hash consistent with exact coordinate equality.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _x.GetHashCode();
                hash = (hash * 397) ^ _y.GetHashCode();
                return (hash * 397) ^ _z.GetHashCode();
            }
        }

        /// <summary>
        /// Returns whether all coordinates are equal.
        /// </summary>
        /// <param name="left">The first position or offset.</param>
        /// <param name="right">The second position or offset.</param>
        /// <returns>True when all corresponding coordinates compare exactly equal.</returns>
        public static bool operator ==(Double3 left, Double3 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns whether any coordinate differs.
        /// </summary>
        /// <param name="left">The first position or offset.</param>
        /// <param name="right">The second position or offset.</param>
        /// <returns>True when at least one corresponding coordinate differs.</returns>
        public static bool operator !=(Double3 left, Double3 right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Formats all coordinates using invariant culture and round-trip precision.
        /// </summary>
        /// <returns>The coordinates formatted as a parenthesized X, Y, Z triple.</returns>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", _x, _y, _z);
        }
    }
}
