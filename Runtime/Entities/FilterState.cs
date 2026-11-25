using System;
using UnityEngine;

namespace Emas
{
    /// <summary>A source-space observation estimate passed between filters before extrapolation.</summary>
    /// <remarks>Null channels are unavailable; supplied zero motion remains available. Filters modify this copy,
    /// never Spatial's accepted SDK values. Times use the SDK clock, independently for each channel.</remarks>
    public struct FilterState
    {
        /// <summary>Gets or sets the estimated absolute position.</summary>
        /// <value>Cartesian source units or ECEF metres; null before position input.</value>
        public Double3? Position { get; set; }

        /// <summary>Gets or sets the estimated absolute orientation.</summary>
        /// <value>A finite, nonzero quaternion in FilterContext.RotationSpace, or null.</value>
        public Quaternion? Rotation { get; set; }

        /// <summary>Gets or sets linear velocity.</summary>
        /// <value>Source units per second or ECEF metres per second; null permits strategy estimation.</value>
        public Double3? Velocity { get; set; }

        /// <summary>Gets or sets linear acceleration.</summary>
        /// <value>Source units per second squared or ECEF metres per second squared; null permits estimation.</value>
        public Double3? Acceleration { get; set; }

        /// <summary>Gets or sets angular velocity.</summary>
        /// <value>Radians per second. Body rates use model right/up/forward axes with Quaternion.AngleAxis signs;
        /// world rates use right-handed ECEF axes, matching Spatial's stored angular motion.</value>
        public Double3? AngularVelocity { get; set; }

        /// <summary>Gets or sets the angular vector's frame.</summary>
        /// <value>True for ECEF world rates; false for model body rates.</value>
        public bool AngularVelocityIsWorld { get; set; }

        /// <summary>Gets or sets the time represented by Position.</summary>
        /// <value>SDK creation time, including any filter delay; null for untimed input.</value>
        public Timestamp? PositionTime { get; set; }

        /// <summary>Gets or sets the time represented by Rotation.</summary>
        /// <value>SDK creation time, including any filter delay; null for untimed input.</value>
        public Timestamp? RotationTime { get; set; }

        /// <summary>Gets or sets the velocity sample time.</summary>
        /// <value>SDK time, or null for untimed or unavailable velocity.</value>
        public Timestamp? VelocityTime { get; set; }

        /// <summary>Gets or sets the acceleration sample time.</summary>
        /// <value>SDK time, or null for untimed or unavailable acceleration.</value>
        public Timestamp? AccelerationTime { get; set; }

        /// <summary>Gets or sets the angular-velocity sample time.</summary>
        /// <value>SDK time, or null for untimed or unavailable angular velocity.</value>
        public Timestamp? AngularVelocityTime { get; set; }

        internal void Validate()
        {
            if (Position.HasValue)
            {
                ReferenceFrame.ValidatePosition(Position.Value, nameof(Position));
            }

            if (Rotation.HasValue)
            {
                Rotation = SpatialMath.NormalizeRotation(Rotation.Value, nameof(Rotation));
            }

            if ((Velocity.HasValue && !SpatialMath.IsFinite(Velocity.Value))
                || (Acceleration.HasValue && !SpatialMath.IsFinite(Acceleration.Value))
                || (AngularVelocity.HasValue && !SpatialMath.IsFinite(AngularVelocity.Value)))
            {
                throw new ArgumentOutOfRangeException("Motion", "Filtered motion must be finite.");
            }
        }
    }
}
