using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Configures one prefab anchor with one detector provider and any number of manifestation blueprints.
    /// </summary>
    /// <remarks>
    /// Place on the anchor GameObject under a RealmSetup. Add exactly one enabled MonoBehaviour
    /// implementing IDetectorProvider to the same GameObject. Disabling this component removes its anchor;
    /// enabling it again starts a fresh source attachment while its realm is running.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Anchor Setup")]
    public sealed class AnchorSetup : MonoBehaviour
    {
        [Tooltip("Unique among anchors in the owning realm. Ghost keys use this ID.")]
        [SerializeField]
        private string _anchorId = "default";
        [Tooltip("One manifestation blueprint per kind on this anchor. Leave empty for data-only tracking.")]
        [SerializeField]
        private ManifestationBlueprint[] _blueprints = new ManifestationBlueprint[0];
        [Tooltip("Request views for available ghosts whose kinds have an assigned manifestation blueprint with views.")]
        [SerializeField]
        private bool _automaticViews = true;
        private Anchor _anchor;

        /// <summary>
        /// Gets the configured anchor ID.
        /// </summary>
        public string Id
        {
            get
            {
                return _anchorId;
            }
        }

        /// <summary>
        /// Gets the live anchor, or null while this setup is stopped.
        /// </summary>
        public Anchor Anchor
        {
            get
            {
                return _anchor;
            }
        }

        internal bool AutomaticViews
        {
            get
            {
                return _automaticViews;
            }
        }

        internal ManifestationBlueprint[] Blueprints
        {
            get
            {
                return _blueprints ?? new ManifestationBlueprint[0];
            }
        }

        internal IDetectorProvider CreateDetectorProvider()
        {
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

            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            int sourceCount = 0;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IDetectorProvider)
                {
                    sourceCount++;
                    if (!behaviour.isActiveAndEnabled)
                    {
                        return "AnchorSetup requires its detector provider to be enabled.";
                    }
                }
            }

            if (sourceCount != 1)
            {
                return "AnchorSetup requires exactly one IDetectorProvider component on the same GameObject.";
            }

            HashSet<Kind> kinds = new HashSet<Kind>();
            ManifestationBlueprint[] blueprints = Blueprints;
            for (int index = 0; index < blueprints.Length; index++)
            {
                ManifestationBlueprint blueprint = blueprints[index];
                string entry = "AnchorSetup manifestation blueprint at index " + index;
                if (blueprint == null)
                {
                    return entry + " is null. Assign a manifestation blueprint or remove the entry.";
                }

                string error = blueprint.GetConfigurationError();
                if (error != null)
                {
                    return entry + " ('" + blueprint.name + "'): " + error;
                }

                if (!kinds.Add(blueprint.Kind))
                {
                    return entry + " ('" + blueprint.name + "') duplicates kind '"
                        + blueprint.Kind.Id + "'. Assign one manifestation blueprint per kind.";
                }
            }

            return null;
        }

        internal void Bind(Anchor anchor)
        {
            _anchor = anchor;
        }

        internal RealmSetup Owner()
        {
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
            RealmSetup owner = Owner();
            if (owner != null)
            {
                owner.StopAnchor(this);
            }
        }
    }
}
