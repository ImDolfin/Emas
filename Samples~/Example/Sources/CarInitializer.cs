using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Maps the SDK source to the traits authored on this Anchor's Ghosts.</summary>
    public sealed class CarInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            string id = presence.Key.EntityId;
            PositionTrait position = root.GetRequired<PositionTrait>();
            ArticulationTrait articulation = root.GetRequired<ArticulationTrait>();
            // Resolve Source on each read so a binding retains the Presence, rather than either SDK feed.
            if (presence.Source is SdkTwoVehicleFeed)
            {
                position.Bind(() => ((SdkTwoVehicleFeed)presence.Source).Current[id].Coordinates);
                articulation.Bind(() => ((SdkTwoVehicleFeed)presence.Source).Current[id].WheelAngle);
            }
            else
            {
                position.Bind(() =>
                {
                    SdkOneVehicleProxy proxy = ((SdkOneVehicleFeed)presence.Source).Current[id];
                    return new Vector3(proxy.PositionX, proxy.PositionY, proxy.PositionZ);
                });
                articulation.Bind(() => ((SdkOneVehicleFeed)presence.Source).Current[id].Steering);
            }
        }
    }
}
