using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Extends linear and angular velocity while ignoring linear acceleration during the extrapolation gap.</summary>
    [Serializable]
    public sealed class ConstantVelocityExtrapolation : IExtrapolationStrategy
    {
        /// <summary>Extends linear velocity without applying acceleration.</summary>
        /// <param name="context">Pose and motion observations; this model uses linear velocity only.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>Source-space displacement equal to velocity times duration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        public Double3 ExtrapolateDisplacement(ExtrapolationContext context, double seconds)
        {
            ValidateSeconds(seconds);
            return context.Velocity.GetValueOrDefault() * seconds;
        }

        /// <summary>Extends a constant angular vector.</summary>
        /// <param name="context">Pose and motion observations; this model uses angular velocity only.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>A quaternion angular offset in the supplied vector's basis.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        public Quaternion ExtrapolateRotation(ExtrapolationContext context, double seconds)
        {
            ValidateSeconds(seconds);
            return SpatialMath.AngularDisplacement(context.AngularVelocity.GetValueOrDefault(), seconds);
        }

        internal static void ValidateSeconds(double seconds)
        {
            if (!SpatialMath.IsFinite(seconds) || seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), "Extrapolation time must be finite and nonnegative.");
            }
        }
    }
}
