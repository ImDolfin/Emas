using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>Maps the SDK source to the traits authored on this Anchor's Ghosts.</summary>
    public sealed class MarkerInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            // Read through the weak Source; keep the current pose if the source no longer resolves.
            root.GetRequired<MarkerPositionTrait>().Bind(() =>
                (presence.Source as MarkerSource)?.ReadPosition(presence.Key.EntityId) ?? root.transform.localPosition);
        }
    }
}
