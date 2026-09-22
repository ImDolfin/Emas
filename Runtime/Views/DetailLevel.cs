using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Represents the requested detail level of a view.
    /// </summary>
    [Serializable]
    public struct DetailLevel : IEquatable<DetailLevel>, IComparable<DetailLevel>
    {
        /// <summary>
        /// Represents no view.
        /// </summary>
        public static readonly DetailLevel None = new DetailLevel(0);

        /// <summary>
        /// Represents a minimal view.
        /// </summary>
        public static readonly DetailLevel Minimal = new DetailLevel(1);

        /// <summary>
        /// Represents a reduced view.
        /// </summary>
        public static readonly DetailLevel Reduced = new DetailLevel(2);

        /// <summary>
        /// Represents a full view.
        /// </summary>
        public static readonly DetailLevel Full = new DetailLevel(3);

        [SerializeField]
        private int _level;

        /// <summary>
        /// Creates a non-negative detail level.
        /// </summary>
        /// <param name="level">
        /// The detail level.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the level is negative.
        /// </exception>
        public DetailLevel(int level)
        {
            if (level < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(level), "A detail level cannot be negative.");
            }

            _level = level;
        }

        /// <summary>
        /// Gets the integer level.
        /// </summary>
        /// <value>
        /// The non-negative detail level.
        /// </value>
        public int Level
        {
            get
            {
                return _level;
            }
        }

        /// <inheritdoc />
        public bool Equals(DetailLevel other)
        {
            return _level == other._level;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is DetailLevel && Equals((DetailLevel)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _level;
        }

        /// <inheritdoc />
        public int CompareTo(DetailLevel other)
        {
            return _level.CompareTo(other._level);
        }

        /// <summary>
        /// Compares two detail levels for equality.
        /// </summary>
        /// <param name="left">
        /// The first detail level.
        /// </param>
        /// <param name="right">
        /// The second detail level.
        /// </param>
        /// <returns>
        /// True when the detail levels are equal.
        /// </returns>
        public static bool operator ==(DetailLevel left, DetailLevel right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two detail levels for inequality.
        /// </summary>
        /// <param name="left">
        /// The first detail level.
        /// </param>
        /// <param name="right">
        /// The second detail level.
        /// </param>
        /// <returns>
        /// True when the detail levels differ.
        /// </returns>
        public static bool operator !=(DetailLevel left, DetailLevel right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Compares two detail levels.
        /// </summary>
        /// <param name="left">
        /// The first detail level.
        /// </param>
        /// <param name="right">
        /// The second detail level.
        /// </param>
        /// <returns>
        /// True when the first detail level is lower.
        /// </returns>
        public static bool operator <(DetailLevel left, DetailLevel right)
        {
            return left._level < right._level;
        }

        /// <summary>
        /// Compares two detail levels.
        /// </summary>
        /// <param name="left">
        /// The first detail level.
        /// </param>
        /// <param name="right">
        /// The second detail level.
        /// </param>
        /// <returns>
        /// True when the first detail level is greater.
        /// </returns>
        public static bool operator >(DetailLevel left, DetailLevel right)
        {
            return left._level > right._level;
        }

        /// <summary>
        /// Compares two detail levels.
        /// </summary>
        /// <param name="left">
        /// The first detail level.
        /// </param>
        /// <param name="right">
        /// The second detail level.
        /// </param>
        /// <returns>
        /// True when the first detail level is no greater.
        /// </returns>
        public static bool operator <=(DetailLevel left, DetailLevel right)
        {
            return left._level <= right._level;
        }

        /// <summary>
        /// Compares two detail levels.
        /// </summary>
        /// <param name="left">
        /// The first detail level.
        /// </param>
        /// <param name="right">
        /// The second detail level.
        /// </param>
        /// <returns>
        /// True when the first detail level is no lower.
        /// </returns>
        public static bool operator >=(DetailLevel left, DetailLevel right)
        {
            return left._level >= right._level;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            switch (_level)
            {
                case 0:
                    return "None (0)";
                case 1:
                    return "Minimal (1)";
                case 2:
                    return "Reduced (2)";
                case 3:
                    return "Full (3)";
                default:
                    return "Detail level (" + _level + ")";
            }
        }
    }
}
