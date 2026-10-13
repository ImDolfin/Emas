using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies timestamped SDK aircraft body angular velocity to the Prediction behavior trait.</summary>
    [RequireComponent(typeof(Prediction))]
    public sealed class GeoAngularVelocityTrait : Trait<GeoPoseReading>
    {
        /// <summary>Supplies true X-forward, Y-right, Z-down body angular rates in radians per second, or clears an unavailable channel.</summary>
        /// <param name="value">The SDK observation, including optional roll/pitch/yaw body rates and their original timestamp; null clears the channel.</param>
        /// <remarks>Body axes are explicit and independent of pose-reader order. Euler-angle derivatives require conversion into true body rates first.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Prediction prediction = Ghost.GetRequired<Prediction>();
            if (value != null && value.BodyAngularVelocity.HasValue)
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
