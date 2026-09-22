using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Defines a ghost prefab and optional variant views for one kind.</summary>
    [CreateAssetMenu(fileName = "New Emas Blueprint", menuName = "Emas/Blueprint")]

    public sealed class Blueprint : ScriptableObject
    {
        [SerializeField] private string _kindId;
        [SerializeField] private Ghost _ghostPrefab;
        [SerializeField] private List<ViewMapping> _views = new List<ViewMapping>();
        [SerializeField] private GameObject _fallbackView;

        /// <summary>Gets the configured ghost kind.</summary>
        /// <value>The kind selected by this blueprint.</value>
        public Kind Kind
        {
            get { return string.IsNullOrEmpty(_kindId) ? default(Kind) : new Kind(_kindId); }
        }

        /// <summary>Gets the configured ghost prefab.</summary>
        /// <value>The root prefab, or null when a default root is allowed.</value>
        public Ghost GhostPrefab
        {
            get { return _ghostPrefab; }
        }

        /// <summary>Gets the fallback view prefab.</summary>
        /// <value>The fallback prefab, or null when no view is available.</value>
        public GameObject FallbackView
        {
            get { return _fallbackView; }
        }

        /// <summary>Configures the blueprint for code-driven tests or authoring tools.</summary>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="ghostPrefab">The ghost prefab.</param>
        /// <param name="views">The variant and degree mappings.</param>
        /// <param name="fallbackView">The optional fallback prefab.</param>
        /// <exception cref="ArgumentException">Thrown when the kind or view mappings are invalid.</exception>
        public void Configure(Kind kind, Ghost ghostPrefab, IEnumerable<ViewMapping> views, GameObject fallbackView)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("The blueprint kind must be valid.", nameof(kind));
            }

            var copiedViews = views == null ? new List<ViewMapping>() : new List<ViewMapping>(views);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < copiedViews.Count; index++)
            {
                var mapping = copiedViews[index];
                if (mapping.Prefab == null)
                {
                    throw new ArgumentException("A view mapping requires a prefab.", nameof(views));
                }

                if (mapping.Degree.Level <= 0)
                {
                    throw new ArgumentException("A view mapping requires a positive degree.", nameof(views));
                }

                var key = mapping.Variant.Id + "\u001f" + mapping.Degree.Level;
                if (!keys.Add(key))
                {
                    throw new ArgumentException("A blueprint cannot contain duplicate variant and degree mappings.", nameof(views));
                }
            }

            _kindId = kind.Id;
            _ghostPrefab = ghostPrefab;
            _views = copiedViews;
            _fallbackView = fallbackView;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_kindId))
            {
                Debug.LogError("Emas blueprint requires a non-empty kind ID.", this);
            }
            if (_ghostPrefab == null)
            {
                Debug.LogWarning("Emas blueprint has no ghost prefab; default roots will be used.", this);
            }
            if (_views == null)
            {
                _views = new List<ViewMapping>();
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _views.Count; index++)
            {
                var mapping = _views[index];
                if (mapping.Prefab == null || mapping.Degree.Level <= 0)
                {
                    Debug.LogError("Emas blueprint contains an invalid view mapping at index " + index + ".", this);
                    continue;
                }

                if (!keys.Add(mapping.Variant.Id + "\u001f" + mapping.Degree.Level))
                {
                    Debug.LogError("Emas blueprint contains duplicate variant and degree mappings.", this);
                }
            }
        }

        /// <summary>Returns the best matching view prefab.</summary>
        /// <param name="variant">The requested variant.</param>
        /// <param name="degree">The requested degree.</param>
        /// <returns>The selected prefab, or the fallback, or null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is negative.</exception>
        public GameObject GetView(Variant variant, DetailLevel degree)
        {
            if (degree.Level < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(degree), "A detail level cannot be negative.");
            }

            if (_views == null)
            {
                return _fallbackView;
            }

            GameObject best = null;
            var bestDegree = DetailLevel.None;
            for (var index = 0; index < _views.Count; index++)
            {
                var mapping = _views[index];
                if (mapping.Prefab == null || mapping.Variant != variant)
                {
                    continue;
                }

                if (mapping.Degree == degree && mapping.Degree.Level > 0)
                {
                    return mapping.Prefab;
                }

                if (mapping.Degree.Level > 0 && mapping.Degree > bestDegree && mapping.Degree <= degree)
                {
                    best = mapping.Prefab;
                    bestDegree = mapping.Degree;
                }
            }

            return best ?? _fallbackView;
        }

        /// <summary>Maps a variant and degree to a view prefab.</summary>
        [Serializable]
        public struct ViewMapping
        {
            [SerializeField] private Variant _variant;
            [SerializeField] private DetailLevel _degree;
            [SerializeField] private GameObject _prefab;

            /// <summary>Gets the appearance identifier.</summary>
            /// <value>The typed appearance selected by this entry.</value>
            public Variant Variant
            {
                get
                {
                    return _variant;
                }
            }

            /// <summary>Gets the supported degree.</summary>
            /// <value>The detail level selected by this entry.</value>
            public DetailLevel Degree
            {
                get
                {
                    return _degree;
                }
            }

            /// <summary>Gets the view prefab.</summary>
            /// <value>The prefab to instantiate when this entry is selected.</value>
            public GameObject Prefab
            {
                get
                {
                    return _prefab;
                }
            }

            /// <summary>Creates a mapping.</summary>
            /// <param name="variant">The variant identifier.</param>
            /// <param name="degree">The detail level at which this prefab is selected.</param>
            /// <param name="prefab">The view prefab.</param>
            public ViewMapping(Variant variant, DetailLevel degree, GameObject prefab)
            {
                _variant = variant;
                _degree = degree;
                _prefab = prefab;
            }
        }
    }
}
