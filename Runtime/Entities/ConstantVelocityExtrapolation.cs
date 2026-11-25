using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Extends linear and angular velocity while ignoring linear acceleration during the extrapolation gap.</summary>
    [Serializable]
    public sealed class ConstantVelocityExtrapolation : IExtrapolationStrategy, IPredictionModel
    {
        /// <summary>Predicts the complete pose and motion using this model.</summary>
        /// <param name="request">Independently timed observations and bounded prediction intervals.</param>
        /// <returns>The absolute source pose and its linear and angular velocity before smoothing.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Extends linear velocity without applying acceleration.</summary>
        /// <param name="context">Pose and motion observations; this model uses linear velocity only.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>Source-space displacement equal to velocity times duration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            ValidateSeconds(seconds);
            return context.Velocity.GetValueOrDefault() * seconds;
        }

        /// <summary>Extends a constant angular vector.</summary>
        /// <param name="context">Pose and motion observations; this model uses angular velocity only.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>A quaternion angular offset in the supplied vector's basis.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            ValidateSeconds(seconds);
            return SpatialMath.AngularDisplacement(context.AngularVelocity.GetValueOrDefault(), seconds);
        }

        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            return context.Velocity.GetValueOrDefault();
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            return context.AngularVelocity.GetValueOrDefault();
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
