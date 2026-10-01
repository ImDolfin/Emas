using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Maps an Anchor's detected sources to the modules configured on its Ghosts.</summary>
    /// <remarks>
    /// Add one enabled initializer beside AnchorSetup before starting it. This replaces the Realm's
    /// Kind initializer for that Anchor. Bind readers through Presence.Source to retain weak ownership.
    /// Bindings are established on first detection, rediscovery and handover, and when the source or capability set changes.
    /// Label and variant changes alone do not rerun initialization. Modules must already exist on the Ghost root.
    /// Restart the Anchor after changing its initializer configuration.
    /// </remarks>
    [DisallowMultipleComponent]
    public abstract class GhostInitializer : MonoBehaviour
    {
        /// <summary>Binds the detected source to existing Ghost modules before the Ghost becomes available.</summary>
        /// <param name="presence">The stable handle with detection metadata and the weak Source already assigned.</param>
        /// <param name="ghost">The initialized root with its authored module components.</param>
        protected abstract void Initialize(Presence presence, Ghost ghost);

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
