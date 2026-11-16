using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Holds the newest observed pose in gaps while retaining buffered interpolation and SDK motion assistance for smoothing.</summary>
    [Serializable]
    public sealed class NoExtrapolation : IExtrapolationStrategy
    {
        /// <summary>Holds position regardless of supplied motion.</summary>
        /// <param name="context">Pose and motion observations, unused by this model.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>Zero displacement.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration is nonfinite or negative.</exception>
        public Double3 ExtrapolateDisplacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return default(Double3);
        }

        /// <summary>Holds orientation regardless of supplied angular motion.</summary>
        /// <param name="context">Pose and motion observations, unused by this model.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>The identity quaternion.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration is nonfinite or negative.</exception>
        public Quaternion ExtrapolateRotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return Quaternion.identity;
        }
    }
}
