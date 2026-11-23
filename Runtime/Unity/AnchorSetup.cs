using UnityEngine;

namespace Emas
{
    /// <summary>Configures one prefab anchor with a detector component and automatic view requests.</summary>
    /// <remarks>
    /// Place on the anchor GameObject under a RealmSetup. Add exactly one enabled MonoBehaviour
    /// deriving from PresenceDetectorComponent (or implementing IDetectorProvider) to the same GameObject.
    /// Add an optional GhostInitializer beside it to map sources to traits. Disabling this component removes its anchor;
    /// enabling it again starts a fresh source attachment while its realm is running.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Anchor Setup")]
    public sealed class AnchorSetup : MonoBehaviour
    {
        [Tooltip("Unique among anchors in the owning realm. Ghost keys use this ID.")]
        [SerializeField]
        private string _anchorId = "default";
        [Tooltip("Automatically manifest available Ghosts with configured view prefabs. Disable to request views through code.")]
        [SerializeField]
        private bool _automaticViews = true;
        [Tooltip("Use full pose processing only within the configured distance from the Realm reference. Farther entities keep their latest raw pose and reference projection.")]
        [SerializeField]
        private bool _limitPoseProcessing;
        [Tooltip("Positive processing radius in shared source units, or metres in Geographic space. This does not hide distant entities. The followed reference keeps its normal processing.")]
        [Min(0)]
        [SerializeField]
        private double _poseProcessingDistance = 1000;
        private Anchor _anchor;
        private RealmSetup _attachmentOwner;
        private double? _appliedPoseProcessingDistance;

        /// <summary>Gets the configured anchor ID.</summary>
        /// <value>The serialized identifier, which must be unique within the owning realm.</value>
        public string Id
        {
            get
            {
                return _anchorId;
            }
        }

        /// <summary>Gets the live anchor, or null while this setup is stopped.</summary>
        /// <value>The current runtime attachment; disabling and re-enabling creates a new anchor.</value>
        public Anchor Anchor
        {
            get
            {
                return _anchor;
            }
        }

        internal double? PoseProcessingDistance => _limitPoseProcessing ? _poseProcessingDistance : (double?)null;

        internal bool WantsAttachment
        {
            get
            {
                PresenceDetectorComponent detector = GetComponent<PresenceDetectorComponent>();
                return detector == null || detector.isActiveAndEnabled;
            }
        }

        internal bool AutomaticViews
        {
            get
            {
                return _automaticViews;
            }
        }

        internal RealmSetup AttachmentOwner
        {
            get
            {
                return _attachmentOwner;
            }
        }

        internal IDetectorProvider CreateDetectorProvider()
        {
            // Find exactly one provider on this GameObject. Do not use a child anchor's detector.
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            IDetectorProvider provider = null;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                IDetectorProvider candidate = behaviour as IDetectorProvider;
                if (candidate == null)
                {
                    continue;
                }

                if (provider != null)
                {
                    return null;
                }

                provider = candidate;
            }

            return provider;
        }

        internal string GetConfigurationError()
        {
            if (string.IsNullOrEmpty(_anchorId))
            {
                return "AnchorSetup requires a non-empty anchor ID.";
            }

            string processingError = GetPoseProcessingError();
            if (processingError != null)
            {
                return processingError;
            }

            // Check that the provider is enabled and belongs here before callbacks can publish entities.
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            int sourceCount = 0;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IDetectorProvider)
                {
                    sourceCount++;
                    if (!behaviour.enabled || !behaviour.gameObject.activeInHierarchy)
                    {
                        return "Enable the Anchor's detector component before starting it.";
                    }
                }
            }

            if (sourceCount != 1)
            {
                return "Add exactly one PresenceDetectorComponent (or IDetectorProvider) beside Anchor Setup.";
            }

            if (GetComponents<GhostInitializer>().Length > 1)
            {
                return "Add at most one GhostInitializer beside Anchor Setup.";
            }

            return null;
        }

        internal string GetPoseProcessingError()
        {
            return Emas.Anchor.IsValidPoseProcessingDistance(PoseProcessingDistance)
                ? null : "The Anchor pose processing distance must be positive and finite.";
        }

        internal void ApplyPoseProcessingSettings()
        {
            // Only propagate authoring changes. A direct runtime Anchor edit remains intact on repaint.
            double? distance = PoseProcessingDistance;
            if (_anchor != null && _anchor.Transform != null && !_anchor.Realm.IsDisposed
                && _appliedPoseProcessingDistance != distance && GetPoseProcessingError() == null)
            {
                _anchor.PoseProcessingDistance = distance;
                _appliedPoseProcessingDistance = distance;
            }
        }

        internal void Bind(Anchor anchor, RealmSetup owner)
        {
            _anchor = anchor;
            _attachmentOwner = owner;
            _appliedPoseProcessingDistance = PoseProcessingDistance;
        }

        internal RealmSetup Owner()
        {
            // The nearest setup owns the anchor. Nested Realms must not take each other's anchors.
            Transform current = transform;
            while (current != null)
            {
                RealmSetup setup = current.GetComponent<RealmSetup>();
                if (setup != null)
                {
                    return setup;
                }

                current = current.parent;
            }

            return null;
        }

        private void Update()
        {
            // Apply live serialized edits and Undo on Unity's main thread, even if the Inspector is closed.
            ApplyPoseProcessingSettings();
        }

        private void OnEnable()
        {
            RealmSetup owner = Owner();
            if (owner != null)
            {
                owner.StartAnchor(this);
            }
        }

        private void OnDisable()
        {
            RealmSetup owner = _attachmentOwner;
            if (owner != null)
            {
                owner.StopAnchor(this);
            }
        }
    }
}
