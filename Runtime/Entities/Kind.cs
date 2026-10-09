using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Identifies an application-defined category of ghost.
    /// </summary>
    [Serializable]
    public struct Kind : IEquatable<Kind>, IComparable<Kind>
    {
        [Tooltip("Stable, case-sensitive identifier for this ghost kind.")]
        [SerializeField]
        private string _id;

        /// <summary>
        /// Creates a kind with a stable, case-sensitive identifier.
        /// </summary>
        /// <param name="id">
        /// The stable identifier.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="id"/> is null or empty.
        /// </exception>
        public Kind(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A ghost kind requires a non-empty identifier.", nameof(id));
            }

            _id = id;
        }

        /// <summary>
        /// Gets the stable identifier.
        /// </summary>
        /// <value>
        /// The exact, case-sensitive identifier.
        /// </value>
        public string Id
        {
            get
            {
                return _id;
            }
        }

        /// <summary>
        /// Gets whether the kind is valid.
        /// </summary>
        /// <value>
        /// True when the identifier is non-empty.
        /// </value>
        public bool IsValid
        {
            get
            {
                return !string.IsNullOrEmpty(_id);
            }
        }

        /// <summary>Compares category identifiers using case-sensitive ordinal equality.</summary>
        /// <param name="other">The category to compare.</param>
        /// <returns>True when both stored identifiers match, including two default values.</returns>
        public bool Equals(Kind other)
        {
            return string.Equals(_id, other._id, StringComparison.Ordinal);
        }

        /// <summary>Orders categories by their case-sensitive identifiers.</summary>
        /// <param name="other">The category to compare.</param>
        /// <returns>A negative value, zero, or a positive value when this identifier sorts before, equals, or sorts after the other.</returns>
        public int CompareTo(Kind other)
        {
            return string.Compare(_id, other._id, StringComparison.Ordinal);
        }

        /// <summary>Compares another object with this category identifier.</summary>
        /// <param name="obj">The object to compare, including null.</param>
        /// <returns>True when the object is a kind with the same ordinal identifier.</returns>
        public override bool Equals(object obj)
        {
            return obj is Kind && Equals((Kind)obj);
        }

        /// <summary>Hashes the category identifier using ordinal comparison rules.</summary>
        /// <returns>The identifier's ordinal hash, or zero for a default kind.</returns>
        public override int GetHashCode()
        {
            return _id == null ? 0 : StringComparer.Ordinal.GetHashCode(_id);
        }

        /// <summary>
        /// Compares two kinds.
        /// </summary>
        /// <param name="left">
        /// The first kind.
        /// </param>
        /// <param name="right">
        /// The second kind.
        /// </param>
        /// <returns>
        /// True when the identifiers are equal.
        /// </returns>
        public static bool operator ==(Kind left, Kind right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two kinds.
        /// </summary>
        /// <param name="left">
        /// The first kind.
        /// </param>
        /// <param name="right">
        /// The second kind.
        /// </param>
        /// <returns>
        /// True when the identifiers differ.
        /// </returns>
        public static bool operator !=(Kind left, Kind right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Compares two kinds using ordinal identifier ordering.
        /// </summary>
        /// <param name="left">
        /// The first kind.
        /// </param>
        /// <param name="right">
        /// The second kind.
        /// </param>
        /// <returns>
        /// True when the first identifier is lower.
        /// </returns>
        public static bool operator <(Kind left, Kind right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Compares two kinds using ordinal identifier ordering.
        /// </summary>
        /// <param name="left">
        /// The first kind.
        /// </param>
        /// <param name="right">
        /// The second kind.
        /// </param>
        /// <returns>
        /// True when the first identifier is greater.
        /// </returns>
        public static bool operator >(Kind left, Kind right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Compares two kinds using ordinal identifier ordering.
        /// </summary>
        /// <param name="left">
        /// The first kind.
        /// </param>
        /// <param name="right">
        /// The second kind.
        /// </param>
        /// <returns>
        /// True when the first identifier is no greater.
        /// </returns>
        public static bool operator <=(Kind left, Kind right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <summary>
        /// Compares two kinds using ordinal identifier ordering.
        /// </summary>
        /// <param name="left">
        /// The first kind.
        /// </param>
        /// <param name="right">
        /// The second kind.
        /// </param>
        /// <returns>
        /// True when the first identifier is no lower.
        /// </returns>
        public static bool operator >=(Kind left, Kind right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <summary>Formats the category as its application-defined identifier.</summary>
        /// <returns>The identifier, or an empty string for a default kind.</returns>
        public override string ToString()
        {
            return _id ?? string.Empty;
        }
    }
}
