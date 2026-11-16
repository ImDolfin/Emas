using UnityEngine;

namespace Emas.AircraftFormation
{
    /// <summary>Applies a coherent aircraft packet to authored Spatial and optional Prediction components.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class FlightPoseTrait : Trait<FlightReading>
    {
        /// <summary>Stores captured pose and SDK motion at the original observation time, before shared reference projection.</summary>
        /// <param name="data">One immutable reading in Cartesian metres and Unity quaternion axes.</param>
        public override void Apply(FlightReading data)
        {
            Spatial spatial = GetComponent<Spatial>();
            spatial.SetCartesianPosition(data.Position, data.SampleTime);
            spatial.SetSourceRotation(data.Rotation, data.SampleTime);
            Prediction prediction = GetComponent<Prediction>();
            if (prediction != null)
            {
                prediction.SetCartesianVelocity(data.Velocity, data.SampleTime);
                prediction.SetBodyAngularVelocity(data.BodyAngularVelocity, CoordinateSystem.Unity, data.SampleTime);
            }
        }
    }
}
