using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Predicts constant turn rate with constant body-frame acceleration, estimating missing acceleration from history.</summary>
    /// <remarks>Extends Coordinated Turn's three-dimensional trajectory by fitting the change in rotating body velocity.
    /// Supplied gravity-free inertial acceleration takes priority, including zero. Otherwise timestamped SDK velocities
    /// or positions estimate body acceleration and endpoint velocity. Angular velocity remains constant during prediction.
    /// Missing history falls back to Coordinated Turn; missing attitude uses world constant-acceleration prediction.
    /// Instances may be shared when their settings are immutable. Timing, prediction caps and optional Smoothing remain caller-owned.</remarks>
    [Serializable]
    public sealed class ConstantTurnAccelerationExtrapolation : IExtrapolationStrategy, IPredictionModel
    {
        [Tooltip("Infer a steady level-turn rate from bank and horizontal speed only when no supplied or estimated angular rate is available.")]
        [SerializeField] private bool _useBankAngleWhenRatesUnavailable = true;
        [HideInInspector]
        [SerializeField] private float _gravity = 9.80665f;

        /// <summary>Gets or sets whether missing angular rates may be inferred from bank and horizontal speed.</summary>
        /// <value>True by default. Supplied or estimated angular rates, including zero, override the bank fallback.</value>
        /// <remarks>Assumes a steady coordinated level turn with negligible sideslip and wind. The fallback declines
        /// negligible horizontal speed, vertical heading and bank magnitudes of 85 degrees or more.</remarks>
        public bool UseBankAngleWhenRatesUnavailable
        {
            get => _useBankAngleWhenRatesUnavailable;
            set => _useBankAngleWhenRatesUnavailable = value;
        }

        /// <summary>Gets or sets gravity used by the bank-derived turn-rate fallback.</summary>
        /// <value>A finite, positive acceleration in source distance units per second squared; defaults to 9.80665 for metres.</value>
        /// <remarks>Available through code and retained in serialized settings; hidden in the Inspector.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Gravity is nonpositive or nonfinite.</exception>
        public float Gravity
        {
            get => _gravity;
            set
            {
                if (!SpatialMath.IsFinite(value) || value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Gravity must be finite and positive.");
                }

                _gravity = value;
            }
        }

        /// <summary>Predicts the complete pose and motion using this model.</summary>
        /// <param name="request">Independently timed observations and bounded prediction intervals.</param>
        /// <returns>The absolute source pose and its linear and angular velocity after observation filtering.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Integrates rotating body velocity and its constant derivative over the prediction interval.</summary>
        /// <param name="context">Time-aligned pose, SDK motion and retained timestamped histories. SDK acceleration overrides estimation.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the observed position, already bounded by the prediction cap.</param>
        /// <returns>Displacement in Cartesian source units or ECEF metres. Missing velocity information holds position.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            return CoordinatedTurnExtrapolation.PredictDisplacement(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, true);
        }

        /// <summary>Integrates constant angular velocity using the coordinated-turn body/world conventions.</summary>
        /// <param name="context">Time-aligned angular motion and attitude; missing rates may use the bank fallback.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the observed rotation, already bounded by the prediction cap.</param>
        /// <returns>A quaternion offset for the existing rotation-composition pipeline; identity without angular motion.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            return CoordinatedTurnExtrapolation.PredictRotation(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, true);
        }
        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            return CoordinatedTurnExtrapolation.PredictVelocity(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, true);
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            return CoordinatedTurnExtrapolation.PredictAngularVelocity(context, _useBankAngleWhenRatesUnavailable, _gravity, true);
        }

    }
}
