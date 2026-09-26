using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Owns one automatically updated realm and the configured anchors beneath this GameObject.
    /// </summary>
    /// <remarks>
    /// Add AnchorSetup components to this prefab or its children. Each anchor has one detector provider.
    /// Realm manifestation blueprints supply defaults; an anchor can override them for its own kinds. This component owns its realm; it never changes Realm.Default.
    /// Disable it to stop sources and remove all owned ghosts and views. Direct Realm construction
    /// and manual Update calls remain available independently.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Realm Setup")]
    [DefaultExecutionOrder(-32000)]
    public sealed class RealmSetup : MonoBehaviour
    {
        [Tooltip("One realm-wide manifestation blueprint per kind. Anchors may override these.")]
        [SerializeField]
        private ManifestationBlueprint[] _blueprints = new ManifestationBlueprint[0];

        [Header("Reference Frame")]
        [Tooltip("Project Spatial ghosts relative to a manual position or a followed ghost.")]
        [SerializeField]
        private bool _useReferenceFrame;
        [Tooltip("Use a ghost in this realm as the moving simulation reference.")]
        [SerializeField]
        private bool _followGhost;
        [SerializeField]
        private string _referenceAnchorId;
        [SerializeField]
        private Kind _referenceKind;
        [SerializeField]
        private string _referenceEntityId;
        [Tooltip("Manual simulation position, in double precision.")]
        [SerializeField]
        private Double3 _position;
        [SerializeField]
        private Quaternion _rotation = Quaternion.identity;
        [SerializeField]
        private Vector3 _unityPosition;
        [SerializeField]
        private Quaternion _unityRotation = Quaternion.identity;
        [SerializeField]
        private bool _followRotation = true;
        [SerializeField]
        private bool _limitDistance;
        [SerializeField]
        private double _maxDistance = 5000.0;

        private readonly Dictionary<AnchorSetup, IDisposable> _subscriptions =
            new Dictionary<AnchorSetup, IDisposable>();
        private readonly HashSet<Kind> _realmViewKinds = new HashSet<Kind>();
        private Realm _realm;
        private bool _starting;
        private bool _autoStartPending;
        private int _lifetime;

        /// <summary>
        /// Gets the owned realm, or null when this setup is stopped.
        /// </summary>
        public Realm Realm
        {
            get
            {
                return _realm;
            }
        }

        /// <summary>
        /// Starts an isolated realm from the active child anchor and reference-frame settings.
        /// </summary>
        /// <remarks>
        /// Called automatically on the first Update after enabling in Play Mode, after providers have enabled. It may also be called explicitly while enabled.
        /// Each anchor's detector provider creates one detector per start. StopRealm or disabling releases the realm.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The setup is disabled, already running, invalid, or stopped during source startup.
        /// </exception>
        public void StartRealm()
        {
            _autoStartPending = false;
            if (!isActiveAndEnabled || _starting || _realm != null)
            {
                throw new InvalidOperationException("RealmSetup must be enabled and not already running.");
            }

            string error = GetConfigurationError();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            ReferenceFrame frame = CreateReferenceFrame();
            AnchorSetup[] anchors = ActiveAnchors();
            _starting = true;
            int lifetime = ++_lifetime;
            Realm realm = new Realm();
            _realm = realm;
            try
            {
                realm.ReferenceFrame = frame;
                _realmViewKinds.Clear();
                foreach (ManifestationBlueprint blueprint in Blueprints)
                {
                    realm.RegisterManifestationBlueprint(blueprint);
                    if (blueprint.HasManifestationPrefab)
                    {
                        _realmViewKinds.Add(blueprint.Kind);
                    }
                }

                ConfigurePresenceInitializers(realm);

                foreach (AnchorSetup setup in anchors)
                {
                    AttachAnchor(setup, realm, lifetime, false);
                }

                if (_lifetime != lifetime || !isActiveAndEnabled || !ReferenceEquals(_realm, realm))
                {
                    throw new InvalidOperationException("RealmSetup stopped during source startup.");
                }
            }
            catch
            {
                if (ReferenceEquals(_realm, realm))
                {
                    StopRealm();
                }
                else
                {
                    realm.Dispose();
                }

                throw;
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>
        /// Stops this setup and disposes its realm, anchors, sources, ghosts, views and subscriptions.
        /// </summary>
        /// <remarks>
        /// Safe to call repeatedly. The prefab configuration stays in place for the next StartRealm call.
        /// Source providers and application-owned SDK clients remain application-owned.
        /// </remarks>
        public void StopRealm()
        {
            _autoStartPending = false;
            _lifetime++;
            Realm realm = _realm;
            _realm = null;
            KeyValuePair<AnchorSetup, IDisposable>[] subscriptions =
                new KeyValuePair<AnchorSetup, IDisposable>[_subscriptions.Count];
            int index = 0;
            foreach (KeyValuePair<AnchorSetup, IDisposable> item in _subscriptions)
            {
                subscriptions[index++] = item;
            }

            _subscriptions.Clear();
            _realmViewKinds.Clear();
            foreach (KeyValuePair<AnchorSetup, IDisposable> item in subscriptions)
            {
                if (item.Key != null)
                {
                    item.Key.Bind(null);
                }
            }

            foreach (KeyValuePair<AnchorSetup, IDisposable> item in subscriptions)
            {
                item.Value?.Dispose();
            }

            realm?.Dispose();
        }

        internal string GetConfigurationError()
        {
            HashSet<Kind> blueprintKinds = new HashSet<Kind>();
            ManifestationBlueprint[] blueprints = Blueprints;
            for (int index = 0; index < blueprints.Length; index++)
            {
                ManifestationBlueprint blueprint = blueprints[index];
                string entry = "RealmSetup manifestation blueprint at index " + index;
                if (blueprint == null)
                {
                    return entry + " is null. Assign a manifestation blueprint or remove the entry.";
                }

                string error = blueprint.GetConfigurationError();
                if (error != null)
                {
                    return entry + " ('" + blueprint.name + "'): " + error;
                }

                if (!blueprintKinds.Add(blueprint.Kind))
                {
                    return entry + " ('" + blueprint.name + "') duplicates kind '"
                        + blueprint.Kind.Id + "'. Assign one manifestation blueprint per kind.";
                }
            }

            if (_useReferenceFrame)
            {
                try
                {
                    CreateReferenceFrame();
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                {
                    return "RealmSetup reference frame: " + exception.Message;
                }
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            AnchorSetup[] anchors = ActiveAnchors();
            foreach (AnchorSetup setup in anchors)
            {
                string error = setup.GetConfigurationError();
                if (error != null)
                {
                    return "RealmSetup anchor '" + setup.name + "': " + error;
                }

                if (!ids.Add(setup.Id))
                {
                    return "RealmSetup has duplicate anchor ID '" + setup.Id + "'.";
                }
            }

            return null;
        }

        private ManifestationBlueprint[] Blueprints
        {
            get
            {
                return _blueprints ?? new ManifestationBlueprint[0];
            }
        }

        internal void StartAnchor(AnchorSetup setup)
        {
            Realm realm = _realm;
            if (realm == null || _starting || !isActiveAndEnabled || !setup.isActiveAndEnabled
                || setup.Owner() != this || _subscriptions.ContainsKey(setup))
            {
                return;
            }

            string error = setup.GetConfigurationError();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            if (realm.ContainsAnchor(setup.Id))
            {
                throw new InvalidOperationException("RealmSetup has duplicate anchor ID '" + setup.Id + "'.");
            }

            AttachAnchor(setup, realm, _lifetime, true);
        }

        internal void StopAnchor(AnchorSetup setup)
        {
            IDisposable subscription;
            if (!_subscriptions.TryGetValue(setup, out subscription))
            {
                return;
            }

            _subscriptions.Remove(setup);
            subscription?.Dispose();
            Anchor anchor = setup.Anchor;
            setup.Bind(null);
            anchor?.Dispose();
        }

        private void ConfigurePresenceInitializers(Realm realm)
        {
            MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour component in components)
            {
                if (component == null || !component.isActiveAndEnabled
                    || component.GetComponentInParent<RealmSetup>() != this)
                {
                    continue;
                }

                IRealmConfigurator configurator = component as IRealmConfigurator;
                if (configurator != null)
                {
                    configurator.ConfigureRealm(realm);
                }
            }
        }
        private void AttachAnchor(AnchorSetup setup, Realm realm, int lifetime, bool validate)
        {
            Anchor anchor = null;
            IDisposable subscription = null;
            try
            {
                if (validate)
                {
                    string error = setup.GetConfigurationError();
                    if (error != null)
                    {
                        throw new InvalidOperationException(error);
                    }
                }

                anchor = realm.GetOrCreateAnchor(setup.Id, setup.transform);
                setup.Bind(anchor);
                ManifestationBlueprint[] blueprints = setup.Blueprints;
                HashSet<Kind> kinds = new HashSet<Kind>(_realmViewKinds);
                foreach (ManifestationBlueprint blueprint in blueprints)
                {
                    anchor.RegisterManifestationBlueprint(blueprint);
                    if (blueprint.HasManifestationPrefab)
                    {
                        kinds.Add(blueprint.Kind);
                    }
                    else
                    {
                        kinds.Remove(blueprint.Kind);
                    }
                }

                if (setup.AutomaticViews)
                {
                    subscription = realm.Query().InAnchor(setup.Id).OnAvailable(ghost =>
                    {
                        if (_lifetime == lifetime && ReferenceEquals(_realm, realm)
                            && kinds.Contains(ghost.Key.Kind))
                        {
                            realm.Manifest(ghost);
                        }
                    });
                }

                _subscriptions.Add(setup, subscription);
                PresenceDetector source = setup.CreateDetectorProvider().CreateDetector();
                if (source == null)
                {
                    throw new InvalidOperationException("AnchorSetup detector provider returned null.");
                }

                anchor.AddDetector(source);
                if (_lifetime != lifetime || !setup.isActiveAndEnabled || !isActiveAndEnabled
                    || !ReferenceEquals(_realm, realm))
                {
                    throw new InvalidOperationException("RealmSetup stopped during source startup.");
                }
            }
            catch
            {
                if (_subscriptions.ContainsKey(setup))
                {
                    StopAnchor(setup);
                }
                else
                {
                    subscription?.Dispose();
                    setup.Bind(null);
                    anchor?.Dispose();
                }

                throw;
            }
        }

        private AnchorSetup[] ActiveAnchors()
        {
            AnchorSetup[] candidates = GetComponentsInChildren<AnchorSetup>(true);
            List<AnchorSetup> anchors = new List<AnchorSetup>(candidates.Length);
            foreach (AnchorSetup candidate in candidates)
            {
                if (candidate != null && candidate.isActiveAndEnabled && candidate.Owner() == this)
                {
                    anchors.Add(candidate);
                }
            }

            return anchors.ToArray();
        }

        private ReferenceFrame CreateReferenceFrame()
        {
            if (!_useReferenceFrame)
            {
                return null;
            }

            ReferenceFrame frame = new ReferenceFrame
            {
                UnityPosition = _unityPosition,
                UnityRotation = _unityRotation,
                FollowRotation = _followRotation,
                MaxDistance = _limitDistance ? (double?)_maxDistance : null
            };

            if (_followGhost)
            {
                if (string.IsNullOrEmpty(_referenceAnchorId) || !_referenceKind.IsValid
                    || string.IsNullOrEmpty(_referenceEntityId))
                {
                    throw new ArgumentException("A followed ghost needs an anchor ID, kind and entity ID.");
                }

                frame.FollowedGhost = new Key(_referenceAnchorId, _referenceKind, _referenceEntityId);
                frame.Rotation = _rotation;
            }
            else
            {
                frame.Position = _position;
                frame.Rotation = _rotation;
            }

            return frame;
        }

        private void OnEnable()
        {
            _autoStartPending = Application.isPlaying;
        }

        private void Update()
        {
            if (_autoStartPending)
            {
                StartRealm();
            }

            Realm realm = _realm;
            if (realm != null)
            {
                realm.Update();
            }
        }

        private void OnDisable()
        {
            _autoStartPending = false;
            StopRealm();
        }

        private void OnDestroy()
        {
            StopRealm();
        }
    }
}
