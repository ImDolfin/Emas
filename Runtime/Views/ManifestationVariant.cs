using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Defines the view prefabs available for one appearance at different detail levels.
    /// </summary>
    [CreateAssetMenu(fileName = "New Emas Manifestation Variant", menuName = "Emas/Manifestation Variant")]
    public sealed class ManifestationVariant : ScriptableObject
    {
        [Tooltip("Appearance identifier matched exactly. Leave empty for Variant.None.")]
        [SerializeField]
        private Variant _variant;
        [Tooltip("View prefabs by positive detail level. Higher requests reuse the closest lower level.")]
        [SerializeField]
        private List<DetailMapping> _details = new List<DetailMapping>();

        /// <summary>
        /// Gets the appearance identifier configured by this asset.
        /// </summary>
        public Variant Variant
        {
            get
            {
                return _variant;
            }
        }

        internal bool HasPrefab
        {
            get
            {
                if (_details != null)
                {
                    foreach (DetailMapping mapping in _details)
                    {
                        if (mapping.Prefab != null && mapping.DetailLevel.Level > 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Configures one appearance and its view prefabs by detail level.
        /// </summary>
        /// <param name="variant">The appearance identifier, or Variant.None.</param>
        /// <param name="details">Mappings from positive detail levels to view prefabs.</param>
        /// <exception cref="ArgumentException">A detail mapping is invalid.</exception>
        public void Configure(Variant variant, IEnumerable<DetailMapping> details)
        {
            List<DetailMapping> copiedDetails = details == null
                ? new List<DetailMapping>() : new List<DetailMapping>(details);
            string error = GetConfigurationError(variant, copiedDetails);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(details));
            }

            _variant = variant;
            _details = copiedDetails;
        }

        internal string GetConfigurationError()
        {
            return GetConfigurationError(_variant, _details);
        }

        internal DetailMapping[] CaptureMappings()
        {
            return _details == null ? new DetailMapping[0] : _details.ToArray();
        }

        private static string GetConfigurationError(Variant variant, IReadOnlyList<DetailMapping> details)
        {
            if (!variant.IsNone && string.IsNullOrWhiteSpace(variant.Id))
            {
                return "Manifestation variant has a whitespace-only variant ID. Use Variant.None or a non-whitespace identifier.";
            }

            Dictionary<int, int> indices = new Dictionary<int, int>();
            if (details != null)
            {
                for (int index = 0; index < details.Count; index++)
                {
                    DetailMapping mapping = details[index];
                    string entry = "Detail mapping at index " + index + " (detail level "
                        + mapping.DetailLevel.Level + ")";
                    if (mapping.Prefab == null)
                    {
                        return entry + " requires a non-null prefab.";
                    }

                    if (mapping.DetailLevel.Level <= 0)
                    {
                        return entry + " requires a positive detail level.";
                    }

                    int previousIndex;
                    if (indices.TryGetValue(mapping.DetailLevel.Level, out previousIndex))
                    {
                        return entry + " duplicates index " + previousIndex + ". Use a unique detail level.";
                    }

                    indices.Add(mapping.DetailLevel.Level, index);
                }
            }

            return null;
        }

        /// <summary>
        /// Maps one detail level to a view prefab.
        /// </summary>
        [Serializable]
        public struct DetailMapping
        {
            [Tooltip("Positive detail level supported by this prefab.")]
            [SerializeField]
            private DetailLevel _detailLevel;
            [Tooltip("Visual child prefab instantiated beneath the ghost root.")]
            [SerializeField]
            private GameObject _prefab;

            /// <summary>
            /// Gets the supported detail level.
            /// </summary>
            public DetailLevel DetailLevel
            {
                get
                {
                    return _detailLevel;
                }
            }

            /// <summary>
            /// Gets the view prefab.
            /// </summary>
            public GameObject Prefab
            {
                get
                {
                    return _prefab;
                }
            }

            /// <summary>
            /// Creates a detail level mapping.
            /// </summary>
            /// <param name="detailLevel">The supported detail level.</param>
            /// <param name="prefab">The view prefab.</param>
            public DetailMapping(DetailLevel detailLevel, GameObject prefab)
            {
                _detailLevel = detailLevel;
                _prefab = prefab;
            }
        }
    }
}
