using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Maps the SDK source to the modules authored on this Anchor's Ghosts.</summary>
    public sealed class GeoInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            string id = presence.Key.EntityId;
            root.GetComponent<GeoPositionModule>().Bind(() =>
            {
                GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                return new GeoPosition(reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters);
            });
            root.GetComponent<GeoOrientationModule>().Bind(() =>
            {
                GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                // SDK yaw is clockwise from local north, pitch nose-up, roll right-wing-down.
                Quaternion yaw = Quaternion.AngleAxis((float)(reading.YawDegrees % 360.0), Vector3.up);
                Quaternion pitch = Quaternion.AngleAxis(-(float)(reading.PitchDegrees % 360.0), Vector3.right);
                Quaternion roll = Quaternion.AngleAxis(-(float)(reading.RollDegrees % 360.0), Vector3.forward);
                return yaw * pitch * roll;
            });
        }
    }
}
