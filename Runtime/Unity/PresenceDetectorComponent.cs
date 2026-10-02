using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>A scene component using the plain C# PresenceDetector lifecycle.</summary>
    /// <remarks>
    /// Add beside AnchorSetup. Override OnStart, OnUpdate and OnStop instead of Unity enable/disable messages.
    /// Disabling this component stops its Anchor; enabling it starts a fresh attachment.
    /// Use all operations on Unity's main thread. Code-based integrations can use PresenceDetector directly.
    /// </remarks>
    [DisallowMultipleComponent]
    public abstract class PresenceDetectorComponent : MonoBehaviour, IDetectorProvider
    {
        private ComponentDetector _detector;

        /// <summary>Gets the lazily created detector reused for this component's attachments.</summary>
        /// <remarks>Use for diagnostics and Anchor operations. Configure timeouts and grace only while detached.</remarks>
        public PresenceDetector Detector
        {
            get { return Bridge; }
        }

        /// <summary>Gets the attached Anchor, or null while detached.</summary>
        public Anchor Anchor { get { return Bridge.Anchor; } }

        /// <summary>Gets the attached Anchor's Realm, or null while detached.</summary>
        /// <remarks>Available before OnStart and through OnStop; scene reparenting retains the current attachment until reattachment.</remarks>
        public Realm Realm { get { return Bridge.Realm; } }

        /// <summary>Gets a snapshot of owned presences, including unavailable ones.</summary>
        protected IReadOnlyList<Presence> OwnedPresences { get { return Bridge.Presences; } }

        /// <summary>Subscribes to the SDK and detects existing entities for this attachment.</summary>
        protected virtual void OnStart()
        {
        }

        /// <summary>Processes one Realm update before modules read their mapped inputs.</summary>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>Releases subscriptions once per started attachment, including failed startup.</summary>
        protected virtual void OnStop()
        {
        }

        /// <inheritdoc cref="PresenceDetector.Detect"/>
        protected Presence Detect(string entityId, Kind kind, string name = null, Variant? variant = null,
            IEnumerable<Type> capabilities = null, object source = null)
        {
            return Bridge.DetectEntity(entityId, kind, name, variant, capabilities, source);
        }

        /// <inheritdoc cref="PresenceDetector.Disappear"/>
        protected void Disappear(Kind kind, string entityId)
        {
            Bridge.RemoveEntity(kind, entityId);
        }

        /// <inheritdoc cref="PresenceDetector.Dispatch"/>
        protected void Dispatch(Action action)
        {
            Bridge.Enqueue(action);
        }

        /// <inheritdoc cref="PresenceDetector.CaptureDispatcher"/>
        protected Action<Action> CaptureDispatcher()
        {
            return Bridge.Capture();
        }

        PresenceDetector IDetectorProvider.CreateDetector()
        {
            return Detector;
        }

        private ComponentDetector Bridge
        {
            get
            {
                if (_detector == null)
                {
                    _detector = new ComponentDetector(this) { Name = GetType().Name };
                }
                return _detector;
            }
        }

        private void OnEnable()
        {
            AnchorSetup setup = GetComponent<AnchorSetup>();
            if (setup != null && setup.isActiveAndEnabled)
            {
                RealmSetup owner = setup.Owner();
                if (owner != null)
                {
                    owner.StartAnchor(setup);
                }
            }
        }

        private void OnDisable()
        {
            AnchorSetup setup = GetComponent<AnchorSetup>();
            if (setup != null && setup.AttachmentOwner != null)
            {
                setup.AttachmentOwner.StopAnchor(setup);
            }
        }

        // The bridge shares the plain detector's ownership, failure handling and callback-generation checks.
        private sealed class ComponentDetector : PresenceDetector
        {
            private readonly PresenceDetectorComponent _owner;

            internal ComponentDetector(PresenceDetectorComponent owner)
            {
                _owner = owner;
            }

            internal IReadOnlyList<Presence> Presences { get { return OwnedPresences; } }
            protected override void OnStart()
            {
                _owner.OnStart();
            }
            protected override void OnUpdate()
            {
                _owner.OnUpdate();
            }
            protected override void OnStop()
            {
                _owner.OnStop();
            }
            internal Presence DetectEntity(string id, Kind kind, string name, Variant? variant,
                IEnumerable<Type> capabilities, object source)
            {
                return Detect(id, kind, name, variant, capabilities, source);
            }
            internal void RemoveEntity(Kind kind, string id)
            {
                Disappear(kind, id);
            }
            internal void Enqueue(Action action)
            {
                Dispatch(action);
            }
            internal Action<Action> Capture()
            {
                return CaptureDispatcher();
            }
        }
    }
}
