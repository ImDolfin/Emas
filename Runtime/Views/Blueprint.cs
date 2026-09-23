using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Defines a ghost prefab and optional variant views for one kind.
    /// </summary>
    [CreateAssetMenu(fileName = "New Emas Blueprint", menuName = "Emas/Blueprint")]
    public sealed class Blueprint : ScriptableObject
    {
        [Tooltip("Entity kind this blueprint configures. Each SceneSetup can assign one blueprint per kind.")]
        [SerializeField]
        private string _kindId;
        [Tooltip("Optional root prefab. Leave empty to create a root with the requested Ghost component.")]
        [SerializeField]
        private Ghost _ghostPrefab;
        [Tooltip("View prefabs by variant and detail level. Selection uses an exact match, then the highest lower positive level, then the fallback prefab.")]
        [SerializeField]
        private List<ViewMapping> _views = new List<ViewMapping>();
        [Tooltip("Optional prefab used when no mapping matches the variant and requested detail level. Leave empty to create no view in that case.")]
        [SerializeField]
        private GameObject _fallbackViewPrefab;

        /// <summary>
        /// Gets the configured ghost kind.
        /// </summary>
        /// <value>
        /// The kind selected by this blueprint.
        /// </value>
        public Kind Kind
        {
            get
            {
                return string.IsNullOrEmpty(_kindId) ? default(Kind) : new Kind(_kindId);
            }
        }

        /// <summary>
        /// Gets the configured ghost prefab.
        /// </summary>
        /// <value>
        /// The root prefab, or null when a default root is allowed.
        /// </value>
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
        /// <value>
        /// The fallback prefab, or null when no view is available.
        /// </value>
        public GameObject FallbackViewPrefab
        {
            get
            {
                return _fallbackViewPrefab;
            }
        }

        /// <summary>
        /// Configures the blueprint for code-driven tests or authoring tools.
        /// </summary>
        /// <param name="kind">
        /// The ghost kind.
        /// </param>
        /// <param name="ghostPrefab">
        /// The optional root prefab; null creates the requested Ghost component automatically.
        /// </param>
        /// <param name="views">
        /// The variant and detail level mappings.
        /// </param>
        /// <param name="fallbackViewPrefab">
        /// The optional fallback prefab.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when the kind or view mappings are invalid.
        /// </exception>
        /// <remarks>
        /// Existing registrations keep their captured settings until each realm or anchor registers this asset again.
        /// </remarks>
        public void Configure(Kind kind, Ghost ghostPrefab, IEnumerable<ViewMapping> views, GameObject fallbackViewPrefab)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("The blueprint kind must be valid.", nameof(kind));
            }

            List<ViewMapping> copiedViews = views == null ? new List<ViewMapping>() : new List<ViewMapping>(views);
            string error = GetConfigurationError(kind, copiedViews);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(views));
            }

            _kindId = kind.Id;
            _ghostPrefab = ghostPrefab;
            _views = copiedViews;
            _fallbackViewPrefab = fallbackViewPrefab;
        }

        internal string GetConfigurationError()
        {
            return GetConfigurationError(Kind, _views);
        }

        internal BlueprintSnapshot CaptureSnapshot()
        {
            ViewMapping[] views = _views == null ? new ViewMapping[0] : _views.ToArray();
            return new BlueprintSnapshot(this, Kind, _ghostPrefab, views, _fallbackViewPrefab);
        }

        private static string GetConfigurationError(Kind kind, IReadOnlyList<ViewMapping> views)
        {
            if (!kind.IsValid)
            {
                return "Emas blueprint requires a non-empty kind ID.";
            }

            Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.Ordinal);
            if (views != null)
            {
                for (int index = 0; index < views.Count; index++)
                {
                    string error = GetMappingError(views[index], index, indices);
                    if (error != null)
                    {
                        return error;
                    }
                }
            }

            return null;
        }

        private static string GetMappingError(ViewMapping mapping, int index, Dictionary<string, int> indices)
        {
            string entry = "View mapping at index " + index + " (variant '" + mapping.Variant
                + "', detail level " + mapping.DetailLevel.Level + ")";
            if (mapping.Prefab == null)
            {
                return entry + " requires a non-null prefab.";
            }

            if (mapping.DetailLevel.Level <= 0)
            {
                return entry + " requires a positive detail level.";
            }

            if (!mapping.Variant.IsNone && string.IsNullOrWhiteSpace(mapping.Variant.Id))
            {
                return entry + " has a whitespace-only variant ID. Use Variant.None or a non-whitespace identifier.";
            }

            string key = mapping.Variant.Id + "\u001f" + mapping.DetailLevel.Level;
            int previousIndex;
            if (indices.TryGetValue(key, out previousIndex))
            {
                return entry + " duplicates index " + previousIndex + ". Use a unique variant and detail level pair.";
            }

            indices.Add(key, index);
            return null;
        }

        /// <summary>
        /// Returns the best matching view prefab.
        /// </summary>
        /// <param name="variant">
        /// The requested variant.
        /// </param>
        /// <param name="detailLevel">
        /// The requested detail level.
        /// </param>
        /// <returns>
        /// The selected prefab, the fallback, or null. None always returns null.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the detail level is negative.
        /// </exception>
        public GameObject ResolveViewPrefab(Variant variant, DetailLevel detailLevel)
        {
            return ResolveViewPrefab(_views, _fallbackViewPrefab, variant, detailLevel);
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

            if (views == null)
            {
                return fallbackViewPrefab;
            }

            GameObject best = null;
            DetailLevel bestDetailLevel = DetailLevel.None;
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

                if (mapping.DetailLevel.Level > 0 && mapping.DetailLevel > bestDetailLevel && mapping.DetailLevel <= detailLevel)
                {
                    best = mapping.Prefab;
                    bestDetailLevel = mapping.DetailLevel;
                }
            }

            return best ?? fallbackViewPrefab;
        }

        /// <summary>
        /// Maps a variant and detail level to a view prefab.
        /// </summary>
        [Serializable]
        public struct ViewMapping
        {
            [Tooltip("Appearance identifier matched exactly. Use None for ghosts without a variant.")]
            [SerializeField]
            private Variant _variant;
            [Tooltip("Positive detail level supported by this prefab. Higher requests can reuse it when no closer mapping exists.")]
            [SerializeField]
            private DetailLevel _detailLevel;
            [Tooltip("Visual child prefab instantiated beneath the ghost root.")]
            [SerializeField]
            private GameObject _prefab;

            /// <summary>
            /// Gets the appearance identifier.
            /// </summary>
            /// <value>
            /// The typed appearance selected by this entry.
            /// </value>
            public Variant Variant
            {
                get
                {
                    return _variant;
                }
            }

            /// <summary>
            /// Gets the supported detail level.
            /// </summary>
            /// <value>
            /// The detail level selected by this entry.
            /// </value>
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
            /// <value>
            /// The prefab to instantiate when this entry is selected.
            /// </value>
            public GameObject Prefab
            {
                get
                {
                    return _prefab;
                }
            }

            /// <summary>
            /// Creates a mapping.
            /// </summary>
            /// <param name="variant">
            /// The variant identifier.
            /// </param>
            /// <param name="detailLevel">
            /// The detail level at which this prefab is selected.
            /// </param>
            /// <param name="prefab">
            /// The view prefab.
            /// </param>
            public ViewMapping(Variant variant, DetailLevel detailLevel, GameObject prefab)
            {
                _variant = variant;
                _detailLevel = detailLevel;
                _prefab = prefab;
            }
        }
    }
}
