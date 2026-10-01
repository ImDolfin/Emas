using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Identifies one ghost within one anchor and kind.
    /// </summary>
    /// <remarks>Equality is case-sensitive and includes all three fields. The same key may exist in different realms.</remarks>
    [Serializable]
    public struct Key : IEquatable<Key>
    {
        /// <summary>
        /// Creates an identity value, normalizing null anchor and entity IDs to empty strings.
        /// </summary>
        /// <param name="anchorId">
        /// The anchor identifier.
        /// </param>
        /// <param name="kind">
        /// The ghost kind.
        /// </param>
        /// <param name="entityId">
        /// The source entity identifier.
        /// </param>
        /// <remarks>This constructor does not validate identity completeness; tracking APIs require nonempty IDs and a valid kind.</remarks>
        public Key(string anchorId, Kind kind, string entityId)
        {
            AnchorId = anchorId ?? string.Empty;
            Kind = kind;
            EntityId = entityId ?? string.Empty;
        }

        /// <summary>
        /// Gets the anchor identifier.
        /// </summary>
        /// <value>
        /// The anchor identifier.
        /// </value>
        public string AnchorId
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the ghost kind.
        /// </summary>
        /// <value>
        /// The exact ghost kind.
        /// </value>
        public Kind Kind
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source entity identifier.
        /// </summary>
        /// <value>
        /// The source entity identifier.
        /// </value>
        public string EntityId
        {
            get;
            private set;
        }

        /// <inheritdoc />
        public bool Equals(Key other)
        {
            return string.Equals(AnchorId, other.AnchorId, StringComparison.Ordinal)
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
                int hash = StringComparer.Ordinal.GetHashCode(AnchorId ?? string.Empty);
                hash = (hash * 397) ^ Kind.GetHashCode();
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EntityId ?? string.Empty);
            }
        }

        /// <summary>
        /// Compares two keys.
        /// </summary>
        /// <param name="left">
        /// The first key.
        /// </param>
        /// <param name="right">
        /// The second key.
        /// </param>
        /// <returns>
        /// True when the keys are equal.
        /// </returns>
        public static bool operator ==(Key left, Key right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two keys.
        /// </summary>
        /// <param name="left">
        /// The first key.
        /// </param>
        /// <param name="right">
        /// The second key.
        /// </param>
        /// <returns>
        /// True when the keys differ.
        /// </returns>
        public static bool operator !=(Key left, Key right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return AnchorId + ":" + Kind + ":" + EntityId;
        }
    }
}
