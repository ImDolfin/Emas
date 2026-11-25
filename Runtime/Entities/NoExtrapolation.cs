using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Holds the newest observed pose in gaps while retaining buffered interpolation and SDK motion assistance for smoothing.</summary>
    [Serializable]
    public sealed class NoExtrapolation : IExtrapolationStrategy, IPredictionModel
    {
        /// <summary>Predicts the complete pose and motion using this model.</summary>
        /// <param name="request">Independently timed observations and bounded prediction intervals.</param>
        /// <returns>The absolute source pose and its linear and angular velocity after observation filtering.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Holds position regardless of supplied motion.</summary>
        /// <param name="context">Pose and motion observations, unused by this model.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>Zero displacement.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration is nonfinite or negative.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return default(Double3);
        }

        /// <summary>Holds orientation regardless of supplied angular motion.</summary>
        /// <param name="context">Pose and motion observations, unused by this model.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>The identity quaternion.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration is nonfinite or negative.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return Quaternion.identity;
        }
        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            return default(Double3);
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            return default(Double3);
        }

    }
}
