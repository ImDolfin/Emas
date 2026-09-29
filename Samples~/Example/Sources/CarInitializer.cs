using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Maps the SDK source to the modules authored on this Anchor's Ghosts.</summary>
    public sealed class CarInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            string id = presence.Key.EntityId;
            PositionModule position = root.GetComponent<PositionModule>();
            ArticulationModule articulation = root.GetComponent<ArticulationModule>();
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
