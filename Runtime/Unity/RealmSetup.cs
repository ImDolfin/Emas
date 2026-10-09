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
    /// Realm manifestation blueprints map each Kind for every anchor in this realm. This component owns its realm; it never changes Realm.Default.
    /// Disable it to stop sources and remove all owned ghosts and views. Direct Realm construction
    /// and manual Update calls remain available independently.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Realm Setup")]
    [DefaultExecutionOrder(-32000)]
    public sealed class RealmSetup : MonoBehaviour
    {
        [Tooltip("One blueprint per Kind, shared by all anchors in this realm.")]
        [SerializeField]
        private ManifestationBlueprint[] _blueprints = new ManifestationBlueprint[0];

        [Tooltip("Shared playback delay in seconds for timestamped position and rotation. 0: immediate. Start at 0.1 for noisy 60 Hz packets. Prediction only fills gaps beyond buffered observations; all roots and the reference use the same time.")]
        [Min(0)]
        [SerializeField]
        private float _interpolationDelay;

        [Tooltip("Project Spatial Ghosts relative to a manual or followed reference point. When off, spatial poses map directly to Unity world space.")]
        [SerializeField]
        private bool _useReferenceFrame;
        [Tooltip("Cartesian uses shared XYZ positions. Geographic uses WGS84 ECEF positions and a tangent frame at the moving reference.")]
        [SerializeField]
        private ReferenceSpace _referenceSpace;
        [Tooltip("Manual WGS84 reference: latitude/longitude in degrees and ellipsoidal height in metres.")]
        [SerializeField]
        private GeoPosition _geographicPosition;
        [Tooltip("Cartesian pose axes, or SetSourceRotation quaternion axes in Geographic space. Geographic Unity means east/up/north. Named geographic yaw/pitch/roll and ECEF inputs use their explicit conventions.")]
        [SerializeField]
        private CoordinateSystem _coordinates = CoordinateSystem.Unity;
        [Tooltip("Use a Ghost in this realm as the moving reference. Leave Entity ID empty to assign ReferenceFrame.FollowedGhost at runtime.")]
        [SerializeField]
        private bool _followGhost;
        [Tooltip("Anchor ID for the Entity ID configured here; must match an Anchor Setup in this realm. Runtime assignment supplies the complete Key.")]
        [SerializeField]
        private string _referenceAnchorId;
        [Tooltip("Kind of the Entity ID configured here. Runtime assignment supplies the complete Key.")]
        [SerializeField]
        private Kind _referenceKind;
        [Tooltip("Optional entity ID to follow at startup. Leave empty to assign ReferenceFrame.FollowedGhost at runtime; spatial presentation waits until a reference position is available.")]
        [SerializeField]
        private string _referenceEntityId;
        [Tooltip("Manual Cartesian reference position in the same units as Spatial positions. For WGS84 latitude/longitude/height, choose Geographic space.")]
        [SerializeField]
        private Double3 _position;
        [Tooltip("Reference orientation in the shared Cartesian frame or local geographic attitude axes. A followed Ghost's published Spatial rotation replaces it.")]
        [SerializeField]
        private Quaternion _rotation = Quaternion.identity;
        [Tooltip("Unity world position where the reference point appears, usually near the scene origin.")]
        [SerializeField]
        private Vector3 _unityPosition;
        [Tooltip("Unity world orientation of the reference; aligns shared Cartesian axes with the scene.")]
        [SerializeField]
        private Quaternion _unityRotation = Quaternion.identity;
        [Tooltip("Cancel the reference's full heading, pitch and roll for positions and attitudes, fixing its Unity pose at Unity Rotation. Disable to follow position only.")]
        [SerializeField]
        private bool _followRotation = true;
        [Tooltip("Hide distant spatial presentation without removing tracked Presences.")]
        [SerializeField]
        private bool _limitDistance;
        [Tooltip("Positive distance from the reference in Spatial position units; farther views are suppressed.")]
        [SerializeField]
        private double _maxDistance = 5000.0;

        private readonly Dictionary<AnchorSetup, IDisposable> _subscriptions =
            new Dictionary<AnchorSetup, IDisposable>();
        private readonly HashSet<Kind> _realmViewKinds = new HashSet<Kind>();
        private readonly HashSet<MonoBehaviour> _configuredComponents = new HashSet<MonoBehaviour>();
        private Realm _realm;
        private bool _starting;
        private bool _autoStartPending;
        private int _lifetime;

        /// <summary>
        /// Gets the owned realm, or null when this setup is stopped.
        /// </summary>
        /// <value>The isolated realm for this running setup; a later startup creates a new instance.</value>
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
        /// Each anchor's provider supplies a detached detector per start; component providers can reuse their detector.
        /// StopRealm or disabling releases the realm. A startup failure disposes the partially configured realm.
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
            _starting = true;
            int lifetime = ++_lifetime;
            Realm realm = new Realm();
            _realm = realm;
            try
            {
                realm.ReferenceFrame = frame;
                realm.InterpolationDelay = _interpolationDelay;
                _realmViewKinds.Clear();
                foreach (ManifestationBlueprint blueprint in Blueprints)
                {
                    realm.RegisterManifestationBlueprint(blueprint);
                    if (blueprint.HasManifestationPrefab)
                    {
                        _realmViewKinds.Add(blueprint.Kind);
                    }
                }

                AttachActiveAnchors(realm, lifetime, false);

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
            // Clear the live binding before cleanup callbacks can stop or start this setup again.
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
            _configuredComponents.Clear();
            foreach (KeyValuePair<AnchorSetup, IDisposable> item in subscriptions)
            {
                if (item.Key != null)
                {
                    item.Key.Bind(null, null);
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
            if (!SpatialMath.IsFinite(_interpolationDelay) || _interpolationDelay < 0)
            {
                return "RealmSetup interpolation delay must be finite and nonnegative.";
            }

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
                || !setup.WantsAttachment || setup.Owner() != this || setup.AttachmentOwner != null || _subscriptions.ContainsKey(setup))
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

            int lifetime = _lifetime;
            _starting = true;
            try
            {
                AttachActiveAnchors(realm, lifetime, true);
            }
            finally
            {
                _starting = false;
            }
        }

        internal void StopAnchor(AnchorSetup setup)
        {
            IDisposable subscription;
            if (!_subscriptions.TryGetValue(setup, out subscription))
            {
                return;
            }

            _subscriptions.Remove(setup);
            Anchor anchor = setup.Anchor;
            setup.Bind(null, null);
            subscription?.Dispose();
            anchor?.Dispose();
        }

        private void AttachActiveAnchors(Realm realm, int lifetime, bool validate)
        {
            bool attached;
            do
            {
                // A detector callback can enable another anchor or configurator before its next attachment.
                ConfigurePresenceInitializers(realm, lifetime);
                attached = false;
                foreach (AnchorSetup candidate in ActiveAnchors())
                {
                    if (candidate != null && candidate.isActiveAndEnabled && candidate.WantsAttachment && candidate.Owner() == this
                        && candidate.AttachmentOwner == null && !_subscriptions.ContainsKey(candidate))
                    {
                        AttachAnchor(candidate, realm, lifetime, validate);
                        attached = true;
                        break;
                    }
                }
            }
            while (attached);
        }

        private void ConfigurePresenceInitializers(Realm realm, int lifetime)
        {
            bool configured;
            do
            {
                configured = false;
                MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour component in components)
                {
                    if (component == null || !component.enabled || !component.gameObject.activeInHierarchy
                        || component.GetComponentInParent<RealmSetup>() != this)
                    {
                        continue;
                    }

                    IRealmConfigurator configurator = component as IRealmConfigurator;
                    if (configurator == null || !_configuredComponents.Add(component))
                    {
                        continue;
                    }

                    try
                    {
                        configurator.ConfigureRealm(realm);
                    }
                    catch
                    {
                        _configuredComponents.Remove(component);
                        throw;
                    }

                    if (_lifetime != lifetime || !isActiveAndEnabled || !ReferenceEquals(_realm, realm))
                    {
                        throw new InvalidOperationException("RealmSetup stopped during configuration.");
                    }

                    configured = true;
                }
            }
            // A configurator can enable another component; rescan until all active configurators have run.
            while (configured);
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

                if (realm.ContainsAnchor(setup.Id))
                {
                    throw new InvalidOperationException("RealmSetup has duplicate anchor ID '" + setup.Id + "'.");
                }

                anchor = realm.GetOrCreateAnchor(setup.Id, setup.transform);
                setup.Bind(anchor, this);
                GhostInitializer initializer = setup.GetComponent<GhostInitializer>();
                if (initializer != null && initializer.enabled)
                {
                    anchor.SetGhostInitializer(initializer);
                }
                if (setup.AutomaticViews)
                {
                    subscription = realm.Query().InAnchor(setup.Id).OnAvailable(ghost =>
                    {
                        if (_lifetime == lifetime && ReferenceEquals(_realm, realm)
                            && _realmViewKinds.Contains(ghost.Key.Kind))
                        {
                            realm.Manifest(ghost);
                        }
                    });
                }

                // Record ownership before OnStart can disable or reparent the setup and trigger cleanup.
                _subscriptions.Add(setup, subscription);
                PresenceDetector source = setup.CreateDetectorProvider().CreateDetector();
                if (source == null)
                {
                    throw new InvalidOperationException("AnchorSetup detector provider returned null.");
                }

                anchor.AddDetector(source);
                if (_lifetime != lifetime || !setup.isActiveAndEnabled || !isActiveAndEnabled
                    || !ReferenceEquals(_realm, realm) || !ReferenceEquals(setup.Anchor, anchor))
                {
                    throw new InvalidOperationException("RealmSetup stopped during source startup.");
                }
            }
            catch
            {
                if (ReferenceEquals(setup.Anchor, anchor) && _subscriptions.ContainsKey(setup))
                {
                    StopAnchor(setup);
                }
                else
                {
                    subscription?.Dispose();
                    if (ReferenceEquals(setup.Anchor, anchor))
                    {
                        setup.Bind(null, null);
                    }

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
                if (candidate != null && candidate.isActiveAndEnabled && candidate.WantsAttachment && candidate.Owner() == this
                    && (candidate.AttachmentOwner == null || candidate.AttachmentOwner == this))
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
                Space = _referenceSpace,
                Coordinates = _coordinates,
                UnityPosition = _unityPosition,
                UnityRotation = _unityRotation,
                FollowRotation = _followRotation,
                MaxDistance = _limitDistance ? (double?)_maxDistance : null
            };

            if (_followGhost)
            {
                // Leave position undefined even with an empty target: runtime selection must wait for actual data.
                if (!string.IsNullOrEmpty(_referenceEntityId))
                {
                    if (string.IsNullOrEmpty(_referenceAnchorId) || !_referenceKind.IsValid)
                    {
                        throw new ArgumentException("A configured reference Entity ID needs an anchor ID and kind.");
                    }

                    frame.FollowedGhost = new Key(_referenceAnchorId, _referenceKind, _referenceEntityId);
                }
                frame.Rotation = _rotation;
            }
            else
            {
                if (_referenceSpace == ReferenceSpace.Geographic)
                {
                    frame.GeographicPosition = _geographicPosition;
                }
                else
                {
                    frame.Position = _position;
                }
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
