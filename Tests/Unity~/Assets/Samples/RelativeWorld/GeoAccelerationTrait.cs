using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies SDK ENU linear acceleration to assist Spatial's velocity-based prediction.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoAccelerationTrait : Trait<GeoPoseReading>
    {
        /// <summary>Converts optional ENU metres per second squared at the reading's location, or clears unavailable acceleration.</summary>
        /// <param name="value">The SDK observation, with gravity-free acceleration and its tangent origin.</param>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            if (value != null && value.EastNorthUpAcceleration.HasValue)
            {
                spatial.SetGeographicAcceleration(value.EastNorthUpAcceleration.Value,
                    new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters));
            }
            else
            {
                spatial.ClearAcceleration();
            }
        }
    }
}
