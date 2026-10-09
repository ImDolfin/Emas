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
        /// <value>The bridge detector that invokes this component's lifecycle methods.</value>
        public PresenceDetector Detector
        {
            get
            {
                return Bridge;
            }
        }

        /// <summary>Gets the attached Anchor, or null while detached.</summary>
        /// <value>The anchor hosting the bridge detector, including a registration stopped after failure.</value>
        public Anchor Anchor
        {
            get
            {
                return Bridge.Anchor;
            }
        }

        /// <summary>Gets the attached Anchor's Realm, or null while detached.</summary>
        /// <remarks>Available before OnStart and through OnStop; scene reparenting retains the current attachment until reattachment.</remarks>
        /// <value>The realm that owns the current detector attachment, or null while detached.</value>
        public Realm Realm
        {
            get
            {
                return Bridge.Realm;
            }
        }

        /// <summary>Gets a snapshot of owned presences, including unavailable ones.</summary>
        /// <value>A copied membership list; retained handles remain subject to their own tracking lifetimes.</value>
        protected IReadOnlyList<Presence> OwnedPresences
        {
            get
            {
                return Bridge.Presences;
            }
        }

        /// <summary>Subscribes to the SDK and detects existing entities for this attachment.</summary>
        /// <remarks>
        /// Runs on Unity's main thread after <see cref="Anchor"/> and <see cref="Realm"/> are assigned.
        /// A startup exception rolls back publication and still invokes <see cref="OnStop"/> for cleanup.
        /// </remarks>
        protected virtual void OnStart()
        {
        }

        /// <summary>Processes one Realm update before traits read their mapped inputs.</summary>
        /// <remarks>Runs on Unity's main thread. An exception stops this detector and removes its population; other detectors continue.</remarks>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>Releases subscriptions once per started attachment, including failed startup.</summary>
        /// <remarks>Detach SDK callbacks here, including any captured dispatcher. Application-owned SDK clients remain application-owned.</remarks>
        protected virtual void OnStop()
        {
        }

        /// <summary>Creates or refreshes a detected presence and binds the authored Ghost traits to its source.</summary>
        /// <param name="entityId">The stable, nonempty identifier supplied by the SDK.</param>
        /// <param name="kind">The valid kind used to choose the root configuration and initializer.</param>
        /// <param name="name">The display label, or null to retain an existing label and use the ID for a new root.</param>
        /// <param name="variant">The appearance, or null to retain it; <see cref="Variant.None"/> selects the fallback.</param>
        /// <param name="capabilities">Advertised interface types; null retains the set, and an empty sequence clears it.</param>
        /// <param name="source">The weakly retained SDK object or proxy; null preserves the current source.</param>
        /// <returns>The stable realm-owned presence handle for this identity.</returns>
        /// <remarks>
        /// Complete the publication on Unity's main thread. New roots become available after successful startup or the next realm update.
        /// Source and capability changes rerun initialization; label and variant changes alone do not.
        /// </remarks>
        /// <exception cref="ArgumentException">The identity is invalid or a capability is null or not an interface.</exception>
        /// <exception cref="InvalidOperationException">The detector is inactive, the identity has another owner, or its root type is incompatible.</exception>
        /// <exception cref="ObjectDisposedException">The owning anchor or realm has been disposed.</exception>
        protected Presence Detect(string entityId, Kind kind, string name = null, Variant? variant = null,
            IEnumerable<Type> capabilities = null, object source = null)
        {
            return Bridge.DetectEntity(entityId, kind, name, variant, capabilities, source);
        }

        /// <summary>Marks an owned presence unavailable and removes it after the configured disappearance grace period.</summary>
        /// <param name="kind">The kind of the disappeared entity.</param>
        /// <param name="entityId">The SDK entity identifier within this anchor and kind.</param>
        /// <remarks>Unknown identities and inactive registrations are ignored. Call on Unity's main thread.</remarks>
        /// <exception cref="ObjectDisposedException">The active attachment's anchor or realm has been disposed.</exception>
        protected void Disappear(Kind kind, string entityId)
        {
            Bridge.RemoveEntity(kind, entityId);
        }

        /// <summary>Queues source work for a bounded batch in a later realm update.</summary>
        /// <param name="action">The work to execute on Unity's main thread; null is ignored.</param>
        /// <remarks>
        /// Call on Unity's main thread; this does not marshal background work. Inactive attachments ignore queued work.
        /// The realm runs at most 256 queued actions per update. Use <see cref="CaptureDispatcher"/> for callbacks retained by an SDK.
        /// </remarks>
        protected void Dispatch(Action action)
        {
            Bridge.Enqueue(action);
        }

        /// <summary>Captures a dispatcher that accepts work only for the current detector attachment.</summary>
        /// <returns>A callback that queues work while this attachment is active and ignores calls after it ends.</returns>
        /// <remarks>Capture during <see cref="OnStart"/> and invoke only on Unity's main thread; release external subscriptions in <see cref="OnStop"/>.</remarks>
        /// <exception cref="InvalidOperationException">The detector is not active on an anchor.</exception>
        /// <exception cref="ObjectDisposedException">The owning anchor or realm has been disposed.</exception>
        protected Action<Action> CaptureDispatcher()
        {
            return Bridge.Capture();
        }

        /// <summary>Supplies this component's reusable bridge detector to the owning anchor setup.</summary>
        /// <returns>The same bridge instance exposed by <see cref="Detector"/>.</returns>
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

            internal IReadOnlyList<Presence> Presences
            {
                get
                {
                    return OwnedPresences;
                }
            }

            /// <summary>Forwards attachment startup to the owning scene component.</summary>
            protected override void OnStart()
            {
                _owner.OnStart();
            }

            /// <summary>Forwards the detector update to the owning scene component.</summary>
            protected override void OnUpdate()
            {
                _owner.OnUpdate();
            }

            /// <summary>Forwards attachment cleanup to the owning scene component.</summary>
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
