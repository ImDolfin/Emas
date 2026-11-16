using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Extends linear velocity with constant acceleration and angular velocity with a constant rate; the default extrapolation model.</summary>
    [Serializable]
    public sealed class ConstantAccelerationExtrapolation : IExtrapolationStrategy
    {
        /// <summary>Extends linear velocity with one half acceleration times squared duration.</summary>
        /// <param name="context">Pose and motion observations; this model uses linear velocity and optional acceleration.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>Source-space displacement from constant linear acceleration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        public Double3 ExtrapolateDisplacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (!context.Velocity.HasValue)
            {
                return default(Double3);
            }

            return context.Velocity.GetValueOrDefault() * seconds
                + context.Acceleration.GetValueOrDefault() * (0.5d * seconds * seconds);
        }

        /// <summary>Extends a constant angular vector independently of linear acceleration.</summary>
        /// <param name="context">Pose and motion observations; this model uses angular velocity only.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>A quaternion angular offset in the supplied vector's basis.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        public Quaternion ExtrapolateRotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return SpatialMath.AngularDisplacement(context.AngularVelocity.GetValueOrDefault(), seconds);
        }
    }
}
