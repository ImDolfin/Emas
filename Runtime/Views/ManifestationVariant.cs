using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Maps a named appearance to a view prefab, serialized directly inside a blueprint.
    /// </summary>
    [Serializable]
    public struct ManifestationVariant
    {
        [Tooltip("Unique, case-sensitive name used as the Variant identifier in code.")]
        [SerializeField]
        private string _name;
        [Tooltip("Visual child prefab instantiated beneath the ghost root.")]
        [SerializeField]
        private GameObject _prefab;

        /// <summary>
        /// Creates a named appearance and its view prefab.
        /// </summary>
        /// <param name="name">The non-empty, case-sensitive appearance name.</param>
        /// <param name="prefab">The view prefab.</param>
        /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The prefab is null.</exception>
        public ManifestationVariant(string name, GameObject prefab)
        {
            _name = new Variant(name).Id;
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            _prefab = prefab;
        }

        /// <summary>
        /// Gets the appearance name used by this blueprint row.
        /// </summary>
        /// <value>The case-sensitive appearance name, or an empty string for an unconfigured row.</value>
        public string Name
        {
            get
            {
                return _name ?? string.Empty;
            }
        }

        /// <summary>
        /// Gets the runtime appearance identifier for this blueprint row.
        /// </summary>
        /// <value>The identifier constructed from the configured appearance name.</value>
        /// <exception cref="ArgumentException">The row is unconfigured or its serialized name is blank.</exception>
        public Variant Variant
        {
            get
            {
                return new Variant(_name);
            }
        }

        /// <summary>
        /// Gets the view prefab selected for this appearance.
        /// </summary>
        /// <value>The authored view prefab, or null for an unconfigured or missing asset reference.</value>
        public GameObject Prefab
        {
            get
            {
                return _prefab;
            }
        }
    }
}
