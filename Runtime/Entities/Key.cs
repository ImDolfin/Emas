using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Identifies one ghost within one origin and kind.</summary>
    [Serializable]

    public struct Key : IEquatable<Key>
    {
        /// <summary>Creates a key.</summary>
        /// <param name="originId">The origin identifier.</param>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="entityId">The source entity identifier.</param>
        public Key(string originId, Kind kind, string entityId)
        {
            OriginId = originId ?? string.Empty;
            Kind = kind;
            EntityId = entityId ?? string.Empty;
        }

        /// <summary>Gets the origin identifier.</summary>
        /// <value>The origin identifier.</value>
        public string OriginId { get; private set; }

        /// <summary>Gets the ghost kind.</summary>
        /// <value>The exact ghost kind.</value>
        public Kind Kind { get; private set; }

        /// <summary>Gets the source entity identifier.</summary>
        /// <value>The source entity identifier.</value>
        public string EntityId { get; private set; }

        /// <inheritdoc />
        public bool Equals(Key other)
        {
            return string.Equals(OriginId, other.OriginId, StringComparison.Ordinal)
                && Kind == other.Kind
                && string.Equals(EntityId, other.EntityId, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is Key && Equals((Key)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(OriginId ?? string.Empty);
                hash = (hash * 397) ^ Kind.GetHashCode();
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EntityId ?? string.Empty);
            }
        }

        /// <summary>Compares two keys.</summary>
        /// <param name="left">The first key.</param>
        /// <param name="right">The second key.</param>
        /// <returns>True when the keys are equal.</returns>
        public static bool operator ==(Key left, Key right)
        {
            return left.Equals(right);
        }

        /// <summary>Compares two keys.</summary>
        /// <param name="left">The first key.</param>
        /// <param name="right">The second key.</param>
        /// <returns>True when the keys differ.</returns>
        public static bool operator !=(Key left, Key right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return OriginId + ":" + Kind + ":" + EntityId;
        }
    }
}
