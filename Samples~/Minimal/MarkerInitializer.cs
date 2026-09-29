using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>Maps the SDK source to the modules authored on this Anchor's Ghosts.</summary>
    public sealed class MarkerInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            root.GetComponent<MarkerPositionModule>().Bind(() =>
                (presence.Source as MarkerSource)?.ReadPosition(presence.Key.EntityId) ?? root.transform.localPosition);
        }
    }
}
