using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies timestamped SDK world ENU or aircraft body angular velocity to Prediction.</summary>
    [RequireComponent(typeof(Prediction))]
    public sealed class GeoAngularVelocityTrait : Trait<GeoPoseReading>
    {
        /// <summary>Supplies world ENU angular velocity when available, otherwise true aircraft body rates; clears unavailable input.</summary>
        /// <param name="value">The SDK observation with optional angular rates in radians per second and their original timestamp; null clears the channel.</param>
        /// <remarks>World ENU uses the reading's tangent origin; body input uses X forward, Y right, Z down.
        /// Both paths are independent of pose-reader order. Euler-angle derivatives require conversion to an angular vector first.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Prediction prediction = Ghost.GetRequired<Prediction>();
            if (value != null && value.EastNorthUpAngularVelocity.HasValue)
            {
                GeoPosition origin = new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters);
                prediction.SetGeographicAngularVelocity(value.EastNorthUpAngularVelocity.Value, origin, value.SampleTime);
            }
            else if (value != null && value.BodyAngularVelocity.HasValue)
            {
                prediction.SetBodyAngularVelocity(value.BodyAngularVelocity.Value, CoordinateSystem.NorthEastDown, value.SampleTime);
            }
            else
            {
                prediction.ClearAngularVelocity();
            }
        }
    }
}
