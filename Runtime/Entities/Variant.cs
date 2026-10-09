using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Identifies an application-defined appearance independently of a ghost kind.
    /// </summary>
    /// <remarks>
    /// Declare named static readonly values in application assemblies for autocomplete.
    /// IDs use ordinal equality. There are no implicit conversions from strings or kinds.
    /// The default value represents no specified appearance and permits manifestation blueprint fallback.
    /// </remarks>
    [Serializable]
    public struct Variant : IEquatable<Variant>
    {
        [Tooltip("Case-sensitive appearance ID. Leave empty for Variant.None.")]
        [SerializeField]
        private string _id;

        /// <summary>
        /// Represents an unspecified appearance.
        /// </summary>
        public static readonly Variant None = default(Variant);

        /// <summary>
        /// Declares an appearance with a stable identifier.
        /// </summary>
        /// <param name="id">
        /// The non-empty, case-sensitive identifier.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The identifier is null, empty or whitespace.
        /// </exception>
        public Variant(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A variant requires an identifier. Use Variant.None for no appearance.", nameof(id));
            }

            _id = id;
        }

        /// <summary>
        /// Gets the stable appearance identifier.
        /// </summary>
        /// <value>
        /// The identifier, or an empty string for <see cref="None"/>.
        /// </value>
        public string Id
        {
            get
            {
                return _id ?? string.Empty;
            }
        }

        /// <summary>
        /// Gets whether no appearance is specified.
        /// </summary>
        /// <value>
        /// True for the default value or an empty serialized identifier.
        /// </value>
        public bool IsNone
        {
            get
            {
                return string.IsNullOrEmpty(_id);
            }
        }

        /// <summary>Compares appearance identifiers using case-sensitive ordinal equality.</summary>
        /// <param name="other">The appearance to compare.</param>
        /// <returns>True when the identifiers match; default and empty serialized identifiers both represent no appearance.</returns>
        public bool Equals(Variant other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        /// <summary>Compares another object with this appearance identifier.</summary>
        /// <param name="obj">The object to compare, including null.</param>
        /// <returns>True when the object is a variant with an equal normalized identifier.</returns>
        public override bool Equals(object obj)
        {
            return obj is Variant && Equals((Variant)obj);
        }

        /// <summary>Hashes the appearance identifier using ordinal comparison rules.</summary>
        /// <returns>A hash consistent with normalized appearance equality.</returns>
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Id);
        }

        /// <summary>Formats the appearance as its application-defined identifier.</summary>
        /// <returns>The identifier, or an empty string when no appearance is specified.</returns>
        public override string ToString()
        {
            return Id;
        }

        /// <summary>
        /// Compares appearance identifiers for equality.
        /// </summary>
        /// <param name="left">
        /// The first appearance.
        /// </param>
        /// <param name="right">
        /// The second appearance.
        /// </param>
        /// <returns>
        /// True when the identifiers are equal.
        /// </returns>
        public static bool operator ==(Variant left, Variant right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares appearance identifiers for inequality.
        /// </summary>
        /// <param name="left">
        /// The first appearance.
        /// </param>
        /// <param name="right">
        /// The second appearance.
        /// </param>
        /// <returns>
        /// True when the identifiers differ.
        /// </returns>
        public static bool operator !=(Variant left, Variant right)
        {
            return !left.Equals(right);
        }
    }
}
