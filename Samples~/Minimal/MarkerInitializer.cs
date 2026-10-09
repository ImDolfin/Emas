using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>Maps the SDK source to the traits authored on this Anchor's Ghosts.</summary>
    public sealed class MarkerInitializer : GhostInitializer
    {
        /// <summary>Binds the marker's position reader to the current source while retaining its last pose if the source disappears.</summary>
        /// <param name="presence">The tracked marker identity and its current source reference.</param>
        /// <param name="root">The authored marker Ghost whose position trait receives the reader.</param>
        protected override void Initialize(Presence presence, Ghost root)
        {
            // Read through the weak Source; keep the current pose if the source no longer resolves.
            root.GetRequired<MarkerPositionTrait>().Bind(() =>
                (presence.Source as MarkerSource)?.ReadPosition(presence.Key.EntityId) ?? root.transform.localPosition);
        }
    }
}
