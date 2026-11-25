using UnityEngine;

namespace Emas
{
    /// <summary>A complete predicted source pose and its instantaneous motion, after observation filtering.</summary>
    public readonly struct Prediction
    {
        /// <summary>Gets the predicted absolute position.</summary>
        /// <value>Cartesian source units or ECEF metres, matching Spatial.Position.</value>
        public Double3 Position { get; }

        /// <summary>Gets the predicted absolute orientation.</summary>
        /// <value>A normalized quaternion in the request's RotationSpace.</value>
        public Quaternion Rotation { get; }

        /// <summary>Gets the position derivative at the predicted time.</summary>
        /// <value>Cartesian source units per second or ECEF metres per second.</value>
        public Double3 Velocity { get; }

        /// <summary>Gets the orientation derivative at the predicted time.</summary>
        /// <value>Radians per second in the world axes of Rotation, with Quaternion.AngleAxis signs.</value>
        public Double3 AngularVelocity { get; }

        /// <summary>Creates a full prediction. Missing pose channels must retain the request's base values.</summary>
        /// <param name="position">Finite absolute position in source coordinates.</param>
        /// <param name="rotation">Finite, nonzero absolute orientation in RotationSpace.</param>
        /// <param name="velocity">Finite source-space position derivative per second.</param>
        /// <param name="angularVelocity">Finite world-axis orientation derivative in radians per second.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">The pose or motion is invalid.</exception>
        public Prediction(Double3 position, Quaternion rotation, Double3 velocity = default(Double3), Double3 angularVelocity = default(Double3))
        {
            if (!SpatialMath.IsFinite(position) || !SpatialMath.IsFinite(velocity) || !SpatialMath.IsFinite(angularVelocity))
            {
                throw new System.ArgumentOutOfRangeException(nameof(position), "Predicted pose and motion must be finite.");
            }

            Position = position;
            Rotation = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            Velocity = velocity;
            AngularVelocity = angularVelocity;
        }
    }
}
