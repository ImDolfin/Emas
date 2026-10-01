using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Defines the optional ghost root and named visual manifestations for one kind.
    /// </summary>
    [CreateAssetMenu(fileName = "New Emas Manifestation Blueprint", menuName = "Emas/Manifestation Blueprint")]
    public sealed class ManifestationBlueprint : ScriptableObject
    {
        [Tooltip("Entity Kind this blueprint configures. Assign one blueprint per Kind on Realm Setup, shared by every Anchor.")]
        [SerializeField]
        private string _kindId;
        [Tooltip("Optional root prefab. Leave empty to create a root with the requested Ghost component.")]
        [SerializeField]
        private Ghost _ghostPrefab;
        [Tooltip("Named appearances stored in this blueprint. Each name selects one view prefab.")]
        [SerializeField]
        private List<ManifestationVariant> _variants = new List<ManifestationVariant>();
        [Tooltip("Optional view prefab for an unspecified or unknown variant.")]
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
                        if (variant.Prefab != null)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Configures this kind and its inline named appearances.
        /// </summary>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="ghostPrefab">The optional root prefab.</param>
        /// <param name="variants">Named appearances, each selecting one view prefab.</param>
        /// <param name="fallbackViewPrefab">The optional view for unspecified or unknown appearances.</param>
        /// <exception cref="ArgumentException">The kind or a variant is invalid.</exception>
        /// <remarks>
        /// Existing registrations keep their captured settings until each realm registers this asset again.
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
            ManifestationVariant[] variants = _variants == null
                ? new ManifestationVariant[0] : _variants.ToArray();
            return new ManifestationBlueprintSnapshot(this, Kind, _ghostPrefab, variants, _fallbackViewPrefab);
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
                    string entry = "Variant at index " + index;
                    if (string.IsNullOrWhiteSpace(variant.Name))
                    {
                        return entry + " requires a non-empty name. Use the fallback prefab for an unspecified variant.";
                    }

                    entry += " ('" + variant.Name + "')";
                    if (variant.Prefab == null)
                    {
                        return entry + " requires a non-null view prefab.";
                    }

                    int previousIndex;
                    if (indices.TryGetValue(variant.Name, out previousIndex))
                    {
                        return entry + " duplicates the name at index " + previousIndex + ". Use a unique name.";
                    }

                    indices.Add(variant.Name, index);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the prefab for an exact variant name, or the optional fallback.
        /// </summary>
        /// <param name="variant">The requested appearance.</param>
        /// <returns>The matching prefab, the fallback, or null when neither is configured.</returns>
        public GameObject ResolveViewPrefab(Variant variant)
        {
            return ResolveViewPrefab(_variants, _fallbackViewPrefab, variant);
        }

        internal static GameObject ResolveViewPrefab(IReadOnlyList<ManifestationVariant> variants,
            GameObject fallbackViewPrefab, Variant variant)
        {
            if (!variant.IsNone && variants != null)
            {
                for (int index = 0; index < variants.Count; index++)
                {
                    ManifestationVariant mapping = variants[index];
                    if (mapping.Prefab != null && string.Equals(mapping.Name, variant.Id, StringComparison.Ordinal))
                    {
                        return mapping.Prefab;
                    }
                }
            }

            return fallbackViewPrefab;
        }
    }
}
