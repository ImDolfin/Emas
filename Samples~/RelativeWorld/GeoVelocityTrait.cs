using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies timestamped SDK ENU velocity to Spatial.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoVelocityTrait : Trait<GeoPoseReading>
    {
        /// <summary>Converts optional ENU metres per second at the reading's location, or clears unavailable velocity.</summary>
        /// <param name="value">The SDK observation, including its tangent origin and optional ENU velocity; null clears supplied velocity.</param>
        /// <remarks>The observation carries its own geographic basis, so velocity conversion does not depend on position-trait order.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            // Use the packet's geographic origin so conversion does not depend on which reader runs first.
            if (value != null && value.EastNorthUpVelocity.HasValue)
            {
                spatial.SetGeographicVelocity(value.EastNorthUpVelocity.Value,
                    new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters), value.SampleTime);
            }
            else
            {
                // Clear missing velocity. A supplied zero velocity would mean the object is stationary.
                spatial.ClearVelocity();
            }
        }
    }
}
