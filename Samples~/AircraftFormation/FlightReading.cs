using UnityEngine;

namespace Emas.AircraftFormation
{
    /// <summary>One immutable aircraft observation in shared Cartesian Unity axes and metres.</summary>
    public readonly struct FlightReading
    {
        /// <summary>Gets the measured position, including this packet's measurement error.</summary>
        /// <value>Double-precision metres in X-right, Y-up, Z-forward simulation axes.</value>
        public Double3 Position { get; }

        /// <summary>Gets the measured body-to-simulation orientation.</summary>
        /// <value>A Unity quaternion with independently sampled attitude noise.</value>
        public Quaternion Rotation { get; }

        /// <summary>Gets the noise-free SDK velocity at capture time.</summary>
        /// <value>Shared Cartesian metres per second; its magnitude is 343.</value>
        public Double3 Velocity { get; }

        /// <summary>Gets the noise-free angular velocity at capture time.</summary>
        /// <value>Body-local radians per second in Unity quaternion axes, rather than Euler derivatives.</value>
        public Double3 BodyAngularVelocity { get; }

        /// <summary>Gets the original capture timestamp, preserved through transport delay.</summary>
        /// <value>Seconds and nanoseconds on the sample's shared SDK clock.</value>
        public Timestamp SampleTime { get; }

        internal FlightReading(Double3 position, Quaternion rotation, Double3 velocity,
            Double3 bodyAngularVelocity, double time)
        {
            Position = position;
            Rotation = rotation;
            Velocity = velocity;
            BodyAngularVelocity = bodyAngularVelocity;
            SampleTime = Timestamp.FromSeconds(time);
        }
    }
}
