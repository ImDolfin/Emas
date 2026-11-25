using UnityEngine;

namespace Emas.AircraftFormation
{
    /// <summary>Stores an aircraft packet on Spatial for optional prediction and smoothing.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class FlightPoseTrait : Trait<FlightReading>
    {
        /// <summary>Stores captured pose and SDK motion at the original observation time, before shared reference projection.</summary>
        /// <param name="data">One immutable reading in Cartesian metres and Unity quaternion axes.</param>
        public override void Apply(FlightReading data)
        {
            // Use each channel's capture time so reader order cannot change the sample's age.
            Spatial spatial = GetComponent<Spatial>();
            spatial.SetCartesianPosition(data.Position, data.SampleTime);
            spatial.SetSourceRotation(data.Rotation, data.SampleTime);
            spatial.SetCartesianVelocity(data.Velocity, data.SampleTime);
            spatial.SetBodyAngularVelocity(data.BodyAngularVelocity, CoordinateSystem.Unity, data.SampleTime);
        }
    }
}
