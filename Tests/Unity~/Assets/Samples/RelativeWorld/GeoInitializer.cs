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
                return GeoProjection.ToPosition(reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters);
            });
            root.GetComponent<GeoOrientationModule>().Bind(() =>
            {
                GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                return GeoProjection.ToRotation(reading.YawDegrees, reading.PitchDegrees, reading.RollDegrees);
            });
        }
    }
}
