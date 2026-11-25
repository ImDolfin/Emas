using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies timestamped SDK world ENU or aircraft body angular velocity to Spatial.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoAngularVelocityTrait : Trait<GeoPoseReading>
    {
        /// <summary>Supplies world ENU angular velocity when available, otherwise true aircraft body rates; clears unavailable input.</summary>
        /// <param name="value">The SDK observation with optional angular rates in radians per second and their original timestamp; null clears the channel.</param>
        /// <remarks>World ENU uses the reading's tangent origin; body input uses X forward, Y right, Z down.
        /// Both paths are independent of pose-reader order. Euler-angle derivatives require conversion to an angular vector first.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            // Use world angular rates when available. Otherwise use body rates, which differ from Euler angle rates.
            if (value != null && value.EastNorthUpAngularVelocity.HasValue)
            {
                GeoPosition origin = new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters);
                spatial.SetGeographicAngularVelocity(value.EastNorthUpAngularVelocity.Value, origin, value.SampleTime);
            }
            else if (value != null && value.BodyAngularVelocity.HasValue)
            {
                spatial.SetBodyAngularVelocity(value.BodyAngularVelocity.Value, CoordinateSystem.NorthEastDown, value.SampleTime);
            }
            else
            {
                // Clear a missing rate so Emas estimates it from poses rather than keeping the old supplied value.
                spatial.ClearAngularVelocity();
            }
        }
    }
}
