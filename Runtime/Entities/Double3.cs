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
        public double X => _x;

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        public double Y => _y;

        /// <summary>
        /// Gets the Z coordinate.
        /// </summary>
        public double Z => _z;

        /// <summary>
        /// Adds two positions or offsets without converting to float precision.
        /// </summary>
        public static Double3 operator +(Double3 left, Double3 right)
        {
            return new Double3(left._x + right._x, left._y + right._y, left._z + right._z);
        }

        /// <summary>
        /// Subtracts two positions or offsets without converting to float precision.
        /// </summary>
        public static Double3 operator -(Double3 left, Double3 right)
        {
            return new Double3(left._x - right._x, left._y - right._y, left._z - right._z);
        }

        /// <summary>
        /// Scales a position or offset by a finite scalar.
        /// </summary>
        public static Double3 operator *(Double3 value, double scalar)
        {
            return new Double3(value._x * scalar, value._y * scalar, value._z * scalar);
        }

        /// <summary>
        /// Scales a position or offset by a finite scalar.
        /// </summary>
        public static Double3 operator *(double scalar, Double3 value)
        {
            return value * scalar;
        }

        /// <summary>
        /// Calculates distance in coordinate units without converting to float precision.
        /// Returns positive infinity if the distance exceeds the capacity of a double.
        /// </summary>
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

            x /= largest;
            y /= largest;
            z /= largest;
            return largest * Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>
        /// Compares coordinates exactly.
        /// </summary>
        public bool Equals(Double3 other)
        {
            return _x.Equals(other._x) && _y.Equals(other._y) && _z.Equals(other._z);
        }

        /// <summary>
        /// Compares this value with another double-precision position or offset.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is Double3 && Equals((Double3)obj);
        }

        /// <summary>
        /// Returns a hash based on all three coordinates.
        /// </summary>
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
        public static bool operator ==(Double3 left, Double3 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns whether any coordinate differs.
        /// </summary>
        public static bool operator !=(Double3 left, Double3 right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Formats all coordinates using invariant culture and round-trip precision.
        /// </summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", _x, _y, _z);
        }
    }
}
