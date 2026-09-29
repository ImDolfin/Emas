using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Maps the SDK source to the modules authored on this Anchor's Ghosts.</summary>
    public sealed class AircraftInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            root.GetComponent<PositionModule>().Bind(() =>
                ((SimulatedAircraftFeed)presence.Source).Current[presence.Key.EntityId].Position);
        }
    }
}
