using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies timestamped SDK ENU linear acceleration to Spatial.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoAccelerationTrait : Trait<GeoPoseReading>
    {
        /// <summary>Converts optional ENU metres per second squared at the reading's location, or clears unavailable acceleration.</summary>
        /// <param name="value">The SDK observation, with gravity-free acceleration and its tangent origin; null clears supplied acceleration.</param>
        /// <remarks>The observation's geographic basis and timestamp are preserved independently of position-trait order.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            // Convert using the packet's geographic axes and time without using the displayed rotation.
            if (value != null && value.EastNorthUpAcceleration.HasValue)
            {
                spatial.SetGeographicAcceleration(value.EastNorthUpAcceleration.Value,
                    new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters), value.SampleTime);
            }
            else
            {
                spatial.ClearAcceleration();
            }
        }
    }
}
