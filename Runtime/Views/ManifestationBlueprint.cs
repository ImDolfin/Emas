using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Defines the optional ghost root and visual manifestations for one kind.
    /// </summary>
    [CreateAssetMenu(fileName = "New Emas Manifestation Blueprint", menuName = "Emas/Manifestation Blueprint")]
    public sealed class ManifestationBlueprint : ScriptableObject
    {
        [Tooltip("Entity kind this blueprint configures. Each AnchorSetup can assign one blueprint per kind.")]
        [SerializeField]
        private string _kindId;
        [Tooltip("Optional root prefab. Leave empty to create a root with the requested Ghost component.")]
        [SerializeField]
        private Ghost _ghostPrefab;
        [Tooltip("One asset per variant, each containing its view prefabs by detail level.")]
        [SerializeField]
        private List<ManifestationVariant> _variants = new List<ManifestationVariant>();
        [Tooltip("Optional view prefab when no variant and detail level mapping matches.")]
        [SerializeField]
        private GameObject _fallbackViewPrefab;

        /// <summary>
        /// Gets the configured ghost kind.
        /// </summary>
        public Kind Kind
        {
            get
            {
                return string.IsNullOrEmpty(_kindId) ? default(Kind) : new Kind(_kindId);
            }
        }

        /// <summary>
        /// Gets the optional ghost root prefab.
        /// </summary>
        public Ghost GhostPrefab
        {
            get
            {
                return _ghostPrefab;
            }
        }

        /// <summary>
        /// Gets the fallback view prefab.
        /// </summary>
        public GameObject FallbackViewPrefab
        {
            get
            {
                return _fallbackViewPrefab;
            }
        }

        internal bool HasManifestationPrefab
        {
            get
            {
                if (_fallbackViewPrefab != null)
                {
                    return true;
                }

                if (_variants != null)
                {
                    foreach (ManifestationVariant variant in _variants)
                    {
                        if (variant != null && variant.HasPrefab)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Configures this kind and its variant assets.
        /// </summary>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="ghostPrefab">The optional root prefab.</param>
        /// <param name="variants">Variant assets containing detail level views.</param>
        /// <param name="fallbackViewPrefab">The optional fallback view prefab.</param>
        /// <exception cref="ArgumentException">The kind or a variant is invalid.</exception>
        /// <remarks>
        /// Existing registrations keep their captured settings until each realm or anchor registers this asset again.
        /// </remarks>
        public void Configure(Kind kind, Ghost ghostPrefab, IEnumerable<ManifestationVariant> variants,
            GameObject fallbackViewPrefab)
        {
            List<ManifestationVariant> copiedVariants = variants == null
                ? new List<ManifestationVariant>() : new List<ManifestationVariant>(variants);
            string error = GetConfigurationError(kind, copiedVariants);
            if (error != null)
            {
                throw new ArgumentException(error, kind.IsValid ? nameof(variants) : nameof(kind));
            }

            _kindId = kind.Id;
            _ghostPrefab = ghostPrefab;
            _variants = copiedVariants;
            _fallbackViewPrefab = fallbackViewPrefab;
        }

        internal string GetConfigurationError()
        {
            return GetConfigurationError(Kind, _variants);
        }

        internal ManifestationBlueprintSnapshot CaptureSnapshot()
        {
            ViewMapping[] views = CaptureMappings();
            return new ManifestationBlueprintSnapshot(this, Kind, _ghostPrefab, views, _fallbackViewPrefab);
        }

        private static string GetConfigurationError(Kind kind, IReadOnlyList<ManifestationVariant> variants)
        {
            if (!kind.IsValid)
            {
                return "Emas manifestation blueprint requires a non-empty kind ID.";
            }

            Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.Ordinal);
            if (variants != null)
            {
                for (int index = 0; index < variants.Count; index++)
                {
                    ManifestationVariant variant = variants[index];
                    string entry = "Manifestation variant at index " + index;
                    if (variant == null)
                    {
                        return entry + " is null. Assign a variant asset or remove the entry.";
                    }

                    string error = variant.GetConfigurationError();
                    if (error != null)
                    {
                        return entry + " ('" + variant.name + "'): " + error;
                    }

                    int previousIndex;
                    if (indices.TryGetValue(variant.Variant.Id, out previousIndex))
                    {
                        return entry + " ('" + variant.name + "') duplicates variant '"
                            + variant.Variant.Id + "' at index " + previousIndex + ".";
                    }

                    indices.Add(variant.Variant.Id, index);
                }
            }

            return null;
        }

        private ViewMapping[] CaptureMappings()
        {
            List<ViewMapping> views = new List<ViewMapping>();
            if (_variants != null)
            {
                foreach (ManifestationVariant variant in _variants)
                {
                    if (variant == null)
                    {
                        continue;
                    }

                    ManifestationVariant.DetailMapping[] mappings = variant.CaptureMappings();
                    for (int index = 0; index < mappings.Length; index++)
                    {
                        views.Add(new ViewMapping(variant.Variant, mappings[index].DetailLevel, mappings[index].Prefab));
                    }
                }
            }

            return views.ToArray();
        }

        /// <summary>
        /// Returns the best matching view prefab from this asset's current settings.
        /// </summary>
        /// <param name="variant">The requested variant.</param>
        /// <param name="detailLevel">The requested detail level.</param>
        /// <returns>The selected prefab, the fallback, or null. None always returns null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The detail level is negative.</exception>
        public GameObject ResolveViewPrefab(Variant variant, DetailLevel detailLevel)
        {
            return ResolveViewPrefab(CaptureMappings(), _fallbackViewPrefab, variant, detailLevel);
        }

        internal static GameObject ResolveViewPrefab(IReadOnlyList<ViewMapping> views, GameObject fallbackViewPrefab,
            Variant variant, DetailLevel detailLevel)
        {
            if (detailLevel.Level < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(detailLevel), "A detail level cannot be negative.");
            }

            if (detailLevel.Level == 0)
            {
                return null;
            }

            GameObject best = null;
            DetailLevel bestDetailLevel = DetailLevel.None;
            if (views != null)
            {
                for (int index = 0; index < views.Count; index++)
                {
                    ViewMapping mapping = views[index];
                    if (mapping.Prefab == null || mapping.Variant != variant)
                    {
                        continue;
                    }

                    if (mapping.DetailLevel == detailLevel && mapping.DetailLevel.Level > 0)
                    {
                        return mapping.Prefab;
                    }

                    if (mapping.DetailLevel.Level > 0 && mapping.DetailLevel > bestDetailLevel
                        && mapping.DetailLevel <= detailLevel)
                    {
                        best = mapping.Prefab;
                        bestDetailLevel = mapping.DetailLevel;
                    }
                }
            }

            return best ?? fallbackViewPrefab;
        }

        internal struct ViewMapping
        {
            internal ViewMapping(Variant variant, DetailLevel detailLevel, GameObject prefab)
            {
                Variant = variant;
                DetailLevel = detailLevel;
                Prefab = prefab;
            }

            internal readonly Variant Variant;
            internal readonly DetailLevel DetailLevel;
            internal readonly GameObject Prefab;
        }
    }
}
