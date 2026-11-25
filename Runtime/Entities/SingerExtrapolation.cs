using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Extrapolates the Singer model's mean trajectory with exponentially decaying linear acceleration.</summary>
    /// <remarks>Acceleration decays from the position observation as a(t) = a(0)*exp(-t/tau).
    /// Velocity is integrated from that acceleration; velocity itself is not damped toward zero.
    /// Uses time-aligned Cartesian or ECEF motion from the context and constant quaternion angular velocity.
    /// This deterministic strategy adds no process noise, estimator state, coordinated-turn assumption or
    /// packet smoothing. Use the optional Smoothing trait independently. Immutable instances may be shared.</remarks>
    [Serializable]
    public sealed class SingerExtrapolation : IExtrapolationStrategy, IPredictionModel
    {
        [Tooltip("Positive acceleration decay time in seconds; 37% remains after this duration. Smaller approaches constant velocity; larger approaches constant acceleration.")]
        [Min(float.Epsilon)]
        [SerializeField] private float _maneuverTimeConstant = 1f;

        /// <summary>Gets or sets the time for acceleration to decay to 1/e of its observed value.</summary>
        /// <value>A finite, positive duration in seconds, defaulting to 1. Larger values approach constant acceleration; smaller values approach constant velocity.</value>
        /// <remarks>This is a decay time constant, not a smoothing half-life or extrapolation horizon.
        /// Assign a new strategy instance to blend a runtime change through the existing live-tuning transition.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The duration is nonpositive or nonfinite.</exception>
        public float ManeuverTimeConstant
        {
            get => _maneuverTimeConstant;
            set
            {
                ValidateTimeConstant(value);
                _maneuverTimeConstant = value;
            }
        }

        /// <summary>Predicts the complete pose and motion using this model.</summary>
        /// <param name="request">Independently timed observations and bounded prediction intervals.</param>
        /// <returns>The absolute source pose and its linear and angular velocity after observation filtering.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Integrates the observed velocity and exponentially decaying acceleration over a bounded gap.</summary>
        /// <param name="context">Time-aligned source-space velocity and optional gravity-free inertial acceleration. Missing velocity holds position.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the position observation, already limited by the extrapolation horizon.</param>
        /// <returns>Source-space displacement. Missing acceleration produces constant velocity; zero duration produces zero displacement.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration, serialized time constant or resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            ValidateTimeConstant(_maneuverTimeConstant);
            if (seconds == 0 || !context.Velocity.HasValue)
            {
                return default(Double3);
            }

            Double3 displacement = context.Velocity.Value * seconds;
            // With no acceleration, this gives the same result as constant velocity.
            if (!context.Acceleration.HasValue || context.Acceleration.Value == default(Double3))
            {
                return displacement;
            }

            // Add the movement from acceleration as it fades toward zero. Keep the supplied velocity unchanged.
            double tau = _maneuverTimeConstant;
            double ratio = seconds / tau;
            double accelerationWeight;
            if (ratio < 0.01d)
            {
                // Integrating exp(-t/tau) twice gives tau^2*(t/tau - 1 + exp(-t/tau)).
                // Direct subtraction loses precision near zero. This series approaches t^2/2.
                double factor = 0.5d + ratio * (-1d / 6d + ratio * (1d / 24d
                    + ratio * (-1d / 120d + ratio * (1d / 720d - ratio / 5040d))));
                accelerationWeight = seconds * seconds * factor;
            }
            else
            {
                // Factoring out tau avoids squaring a large time constant or the time ratio.
                accelerationWeight = tau * (seconds - tau * (1d - Math.Exp(-ratio)));
            }

            return displacement + context.Acceleration.Value * accelerationWeight;
        }

        /// <summary>Extends the available angular vector with a quaternion exponential independently of linear acceleration decay.</summary>
        /// <param name="context">Time-aligned body or world angular velocity in radians per second, or no rate for a held orientation.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the orientation observation, already limited by the extrapolation horizon.</param>
        /// <returns>A quaternion offset using the existing body/world composition conventions; identity when no rate is available.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return SpatialMath.AngularDisplacement(context.AngularVelocity.GetValueOrDefault(), seconds);
        }

        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            ValidateTimeConstant(_maneuverTimeConstant);
            if (!context.Velocity.HasValue)
            {
                return default(Double3);
            }

            double ratio = seconds / _maneuverTimeConstant;
            // Avoid cancellation for short intervals relative to the maneuver time constant.
            double weight = ratio < 0.01d ? seconds * (1d + ratio * (-0.5d + ratio * (1d / 6d - ratio / 24d)))
                : _maneuverTimeConstant * (1d - Math.Exp(-ratio));
            return context.Velocity.Value + context.Acceleration.GetValueOrDefault() * weight;
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            return context.AngularVelocity.GetValueOrDefault();
        }

        private static void ValidateTimeConstant(float value)
        {
            if (!SpatialMath.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The maneuver time constant must be finite and positive.");
            }
        }
    }
}
