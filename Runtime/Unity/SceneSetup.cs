using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Registers Inspector-assigned blueprints and owns one scene anchor and its optional automatic views.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Scene Setup")]
    public sealed class SceneSetup : MonoBehaviour
    {
        [Tooltip("Unique among active anchors in Realm.Default. Tracking places ghosts under this object; disabling it removes its anchor and ghosts.")]
        [SerializeField]
        private string _anchorId = "default";
        [Tooltip("One blueprint per kind. Leave empty for tracking without automatic views. Registered blueprints remain in Realm.Default after this component stops.")]
        [SerializeField]
        private Blueprint[] _blueprints = new Blueprint[0];
        [Tooltip("Create views for available ghosts in this anchor whose kinds have an assigned blueprint. A matching or fallback prefab is required.")]
        [SerializeField]
        private bool _automaticViews = true;
        private readonly HashSet<Kind> _viewKinds = new HashSet<Kind>();
        private Anchor _anchor;
        private IDisposable _subscription;
        private bool _starting;
        private int _lifetime;

        /// <summary>
        /// Gets the owned anchor, or null when not tracking.
        /// </summary>
        public Anchor Anchor
        {
            get
            {
                return _anchor;
            }
        }

        /// <summary>
        /// Starts sources under this transform using the Inspector settings.
        /// </summary>
        /// <param name="sources">
        /// Application sources to attach.
        /// </param>
        /// <returns>
        /// The owned anchor, also usable for source replacement.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Disabled, already tracking, invalid settings, duplicate anchor ID or interrupted startup.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// A source entry is null.
        /// </exception>
        /// <remarks>
        /// Call once per tracking lifetime while enabled. StopTracking or disabling cleans up; call Track again to restart.
        /// Startup exceptions propagate after cleaning up this attempt. Sources may throw application-specific errors.
        /// SDK clients remain application-owned. Registered blueprints persist until the default realm is disposed.
        /// </remarks>
        public Anchor Track(params PresenceSource[] sources)
        {
            if ((!enabled || !gameObject.activeInHierarchy) || _starting || _anchor != null)
            {
                throw new InvalidOperationException("SceneSetup must be enabled and not already tracking.");
            }

            string error = GetConfigurationError();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            Realm realm = Realm.Default;
            if (realm.ContainsAnchor(_anchorId))
            {
                throw new InvalidOperationException("SceneSetup requires an unused anchor ID: " + _anchorId);
            }

            _viewKinds.Clear();
            Blueprint[] blueprints = _blueprints ?? new Blueprint[0];
            foreach (Blueprint blueprint in blueprints)
            {
                _viewKinds.Add(blueprint.Kind);
            }

            _starting = true;
            int lifetime = ++_lifetime;
            Anchor created = null;
            try
            {
                foreach (Blueprint blueprint in blueprints)
                {
                    realm.RegisterBlueprint(blueprint);
                }

                created = realm.GetOrCreateAnchor(_anchorId, transform);
                _anchor = created;
                // Subscribe before startup so immediately available ghosts also receive views.
                if (_automaticViews)
                {
                    _subscription = realm.Query().InAnchor(_anchorId).OnAvailable(ghost =>
                    {
                        if (_lifetime == lifetime && _viewKinds.Contains(ghost.Key.Kind))
                        {
                            realm.Manifest(ghost);
                        }
                    });
                }

                if (sources != null)
                {
                    foreach (PresenceSource source in sources)
                    {
                        if (_lifetime != lifetime || (!enabled || !gameObject.activeInHierarchy))
                        {
                            throw new InvalidOperationException("SceneSetup stopped during source startup.");
                        }

                        created.AddSource(source);
                    }
                }

                if (_lifetime != lifetime || (!enabled || !gameObject.activeInHierarchy))
                {
                    throw new InvalidOperationException("SceneSetup stopped during source startup.");
                }

                return created;
            }
            catch
            {
                Release();
                if (created != null)
                {
                    created.Dispose();
                }

                throw;
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>
        /// Stops tracking and releases the owned anchor, ghosts, views and subscriptions.
        /// </summary>
        /// <remarks>
        /// Safe to call repeatedly, including during startup. The component stays enabled; call Track to start again.
        /// Registered blueprints remain in the realm. SDK clients remain application-owned.
        /// </remarks>
        public void StopTracking()
        {
            Release();
        }

        internal string GetConfigurationError()
        {
            if (string.IsNullOrEmpty(_anchorId))
            {
                return "SceneSetup requires a non-empty anchor ID.";
            }

            HashSet<Kind> kinds = new HashSet<Kind>();
            Blueprint[] blueprints = _blueprints ?? new Blueprint[0];
            for (int index = 0; index < blueprints.Length; index++)
            {
                Blueprint blueprint = blueprints[index];
                string entry = "SceneSetup blueprint at index " + index;
                if (blueprint == null)
                {
                    return entry + " is null. Assign a blueprint or remove the entry.";
                }

                string error = blueprint.GetConfigurationError();
                if (error != null)
                {
                    return entry + " ('" + blueprint.name + "'): " + error;
                }

                if (!kinds.Add(blueprint.Kind))
                {
                    return entry + " ('" + blueprint.name + "') duplicates kind '"
                        + blueprint.Kind.Id + "'. Assign one blueprint per kind.";
                }
            }

            return null;
        }

        private void OnDisable()
        {
            StopTracking();
        }

        private void Release()
        {
            _lifetime++;
            IDisposable subscription = _subscription;
            Anchor anchor = _anchor;
            _subscription = null;
            _anchor = null;
            _viewKinds.Clear();
            if (subscription != null)
            {
                subscription.Dispose();
            }

            if (anchor != null)
            {
                anchor.Dispose();
            }
        }
    }
}
