using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies SDK ENU velocity for Spatial's motion-assisted smoothing.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoVelocityTrait : Trait<GeoPoseReading>
    {
        /// <summary>Converts optional ENU metres per second at the reading's location, or clears unavailable velocity.</summary>
        /// <param name="value">The SDK observation, including its tangent origin and optional ENU velocity.</param>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            if (value != null && value.EastNorthUpVelocity.HasValue)
            {
                spatial.SetGeographicVelocity(value.EastNorthUpVelocity.Value,
                    new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters));
            }
            else
            {
                spatial.ClearVelocity();
            }
        }
    }
}
