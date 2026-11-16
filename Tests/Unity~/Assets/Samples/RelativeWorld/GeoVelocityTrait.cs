using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies timestamped SDK ENU velocity to the Extrapolation behavior trait.</summary>
    [RequireComponent(typeof(Extrapolation))]
    public sealed class GeoVelocityTrait : Trait<GeoPoseReading>
    {
        /// <summary>Converts optional ENU metres per second at the reading's location, or clears unavailable velocity.</summary>
        /// <param name="value">The SDK observation, including its tangent origin and optional ENU velocity; null clears supplied velocity.</param>
        /// <remarks>The observation carries its own geographic basis, so velocity conversion does not depend on position-trait order.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Extrapolation extrapolation = Ghost.GetRequired<Extrapolation>();
            if (value != null && value.EastNorthUpVelocity.HasValue)
            {
                extrapolation.SetGeographicVelocity(value.EastNorthUpVelocity.Value,
                    new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters), value.SampleTime);
            }
            else
            {
                extrapolation.ClearVelocity();
            }
        }
    }
}
