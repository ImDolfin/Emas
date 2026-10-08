using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Maps an Anchor's detected sources to the traits configured on its Ghosts.</summary>
    /// <remarks>
    /// Add one enabled initializer beside AnchorSetup before starting it. This replaces the Realm's
    /// Kind initializer for that Anchor. Bind readers through Presence.Source to retain weak ownership.
    /// Bindings are established on first detection, rediscovery and handover, and when the source or capability set changes.
    /// Label and variant changes alone do not rerun initialization. Traits must already exist on the Ghost root.
    /// Restart the Anchor after changing its initializer configuration.
    /// </remarks>
    [DisallowMultipleComponent]
    public abstract class GhostInitializer : MonoBehaviour
    {
        /// <summary>Gets the Anchor using this initializer, or null while unbound.</summary>
        /// <remarks>
        /// Bound before detector startup and Initialize callbacks. Remains bound until that Anchor is disposed,
        /// including across detector restart or replacement. Scene reparenting does not change the current owner.
        /// </remarks>
        public Anchor Anchor { get; private set; }

        /// <summary>Gets the bound Anchor's Realm, or null while unbound.</summary>
        public Realm Realm
        {
            get
            {
                return Anchor == null ? null : Anchor.Realm;
            }
        }

        /// <summary>Binds the detected source to existing Ghost traits before the Ghost becomes available.</summary>
        /// <param name="presence">The stable handle with detection metadata and the weak Source already assigned.</param>
        /// <param name="ghost">The initialized root with its authored trait components.</param>
        protected abstract void Initialize(Presence presence, Ghost ghost);

        internal void Bind(Anchor anchor)
        {
            Anchor = anchor;
        }

        internal void Unbind(Anchor anchor)
        {
            // Cleanup for an old lifetime must not clear a new binding established from a stop callback.
            if (ReferenceEquals(Anchor, anchor))
            {
                Anchor = null;
            }
        }

        internal void Apply(Presence presence, Ghost ghost)
        {
            if (this == null)
            {
                throw new InvalidOperationException("The Anchor's GhostInitializer was destroyed. Restart the Anchor after changing its configuration.");
            }

            Initialize(presence, ghost);
        }
    }
}
