using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Combines quaternion body-frame second-order dead reckoning, coordinated turns and cubic packet correction.</summary>
    /// <remarks>
    /// Assumes velocity turns with the body without changing sideslip. Acceleration must be gravity-free inertial
    /// acceleration; Gravity must use the same distance unit. Supplied rates, including zero, override bank-derived
    /// rates. Missing attitude falls back to world second-order motion. Instances may be shared when immutable; omit
    /// Smoothing to evaluate cubic correction alone. Timed position history estimates endpoint velocity in the
    /// selected rotating-body model; supplied SDK velocity, including zero, takes precedence.
    /// </remarks>
    [Serializable]
    public sealed class CoordinatedTurnExtrapolation : IExtrapolationStrategy, IPredictionModel, IPacketSmoothingStrategy
    {
        private struct AngularMotion
        {
            internal Double3 Rate;
        }

        private struct Motion
        {
            internal bool HasVelocity;
            internal bool HasAttitude;
            internal GeographicBasis Basis;
            internal Quaternion Attitude;
            internal Double3 Velocity;
            internal Double3 Acceleration;
            internal Double3 Rate;
        }

        [Tooltip("Cubic position and quaternion correction duration on accepted packet arrivals, in unscaled seconds. Zero: immediate correction. Independent of the extrapolation horizon.")]
        [Min(0)]
        [SerializeField] private float _packetSmoothingDuration = 0.1f;
        [Tooltip("Use steady coordinated level-turn rate g*tan(bank)/horizontal speed only when neither supplied nor estimated angular velocity is available. Supplied zero rates override this fallback.")]
        [SerializeField] private bool _useBankAngleWhenRatesUnavailable = true;
        [Tooltip("Gravity magnitude in Cartesian source distance units per second squared; use 9.80665 for metres and geographic input. Used only for the bank-derived turn-rate fallback.")]
        [Min(float.Epsilon)]
        [HideInInspector]
        [SerializeField] private float _gravity = 9.80665f;
        private const int MaximumPositionSamples = 8;
        private const double PositionWindowSeconds = 0.25d;
        private const double MinimumSpan = 0.0001d;
        private static readonly ConstantAccelerationExtrapolation _linearFallback = new ConstantAccelerationExtrapolation();

        /// <summary>Gets or sets cubic packet correction duration in unscaled seconds.</summary>
        /// <value>A finite, nonnegative duration, defaulting to 0.1 seconds. Zero bypasses packet correction.</value>
        /// <remarks>The correction can outlast the extrapolation horizon but converges to the capped pose.
        /// Assign a new strategy instance for a continuous transition when changing other model settings.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The duration is negative or nonfinite.</exception>
        public float PacketSmoothingDuration
        {
            get => _packetSmoothingDuration;
            set
            {
                Smoothing.ValidateTime(value, nameof(value));
                _packetSmoothingDuration = value;
            }
        }

        /// <summary>Gets or sets whether missing angular rates may be inferred from bank and horizontal speed.</summary>
        /// <value>True by default. Does not override a supplied or estimated angular rate, including zero.</value>
        /// <remarks>Assumes a steady coordinated turn with negligible sideslip and no wind. Holds angular motion
        /// at zero when speed is negligible, heading is vertical, or bank reaches 85 degrees or more.</remarks>
        public bool UseBankAngleWhenRatesUnavailable
        {
            get => _useBankAngleWhenRatesUnavailable;
            set => _useBankAngleWhenRatesUnavailable = value;
        }

        /// <summary>Gets or sets gravity used to infer turn rate from bank.</summary>
        /// <value>A finite, positive acceleration in source distance units per second squared; defaults to 9.80665.</value>
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
        /// <returns>The absolute source pose and its linear and angular velocity before smoothing.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Integrates rotating body velocity and its constant first derivative to obtain a second-order displacement.</summary>
        /// <param name="context">Time-aligned pose and inertial motion. Missing velocity uses timed position history, or holds position if unavailable.</param>
        /// <param name="seconds">Finite, nonnegative seconds already bounded by the extrapolation horizon.</param>
        /// <returns>Displacement in Cartesian source axes or ECEF metres, without modifying the observed pose.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or the resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            return PredictDisplacement(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, false);
        }

        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            return PredictVelocity(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, false);
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            return PredictAngularVelocity(context, _useBankAngleWhenRatesUnavailable, _gravity, false);
        }

        /// <summary>Integrates a constant angular vector with the existing body or world composition.</summary>
        /// <param name="context">Time-aligned rates and attitude. Missing rates may use the bank fallback.</param>
        /// <param name="seconds">Finite, nonnegative seconds already bounded by the extrapolation horizon.</param>
        /// <returns>The quaternion offset expected by the body's rotation representation.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            return PredictRotation(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, false);
        }

        internal static Double3 PredictDisplacement(ExtrapolationContext context, double seconds,
            bool useBank, float gravity, bool estimateAcceleration)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (seconds == 0)
            {
                return default(Double3);
            }

            Motion motion = MotionAt(context, useBank, gravity, estimateAcceleration);

            if (!motion.HasVelocity)
            {
                return default(Double3);
            }

            if (!motion.HasAttitude)
            {
                return estimateAcceleration ? ((IPredictionModel)_linearFallback).Displacement(context, seconds) : motion.Velocity * seconds
                    + motion.Acceleration * (0.5d * seconds * seconds);
            }

            // Evaluate the prepared trajectory at this exact duration. Filtering and
            // packet previews share preparation without sharing their evaluated poses.
            Double3 displacement = SpatialMath.Rotate(motion.Attitude, Integrate(motion.Velocity, motion.Acceleration, motion.Rate, seconds));
            return context.Space == ReferenceSpace.Geographic
                ? motion.Basis.ToEarthCentered(displacement) : context.SourceCoordinates.ToSource(displacement);
        }

        /// <summary>Integrates a constant angular vector with a quaternion exponential, retaining body or world composition.</summary>
        /// <param name="context">Time-aligned rates and attitude. Missing rates may use the coordinated-turn fallback.</param>
        /// <param name="seconds">Finite, nonnegative seconds already bounded by the extrapolation horizon.</param>
        /// <returns>The quaternion offset expected by Extrapolation's body/world composition pipeline.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or angular displacement is invalid.</exception>
        internal static Quaternion PredictRotation(ExtrapolationContext context, double seconds,
            bool useBank, float gravity, bool estimateAcceleration)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            return SpatialMath.AngularDisplacement(PredictAngularVelocity(context, useBank, gravity, estimateAcceleration), seconds);
        }

        internal static Double3 PredictAngularVelocity(ExtrapolationContext context,
            bool useBank, float gravity, bool estimateAcceleration)
        {
            if (context.AngularVelocity.HasValue)
            {
                return context.AngularVelocity.Value;
            }

            int options = (useBank ? 1 : 0) | (estimateAcceleration ? 2 : 0);
            if (!context.TryPrediction(typeof(CoordinatedTurnExtrapolation), options, gravity, out AngularMotion motion))
            {
                // Pose, filter motion and the endpoint tangent share the same inferred rate.
                motion.Rate = CalculateAngularVelocity(context, useBank, gravity, estimateAcceleration);
                context.StorePrediction(typeof(CoordinatedTurnExtrapolation), options, gravity, motion);
            }

            return motion.Rate;
        }

        internal static Double3 PredictVelocity(ExtrapolationContext context, double seconds,
            bool useBank, float gravity, bool estimateAcceleration)
        {
            Motion motion = MotionAt(context, useBank, gravity, estimateAcceleration);
            if (!motion.HasVelocity)
            {
                return default(Double3);
            }

            if (!motion.HasAttitude)
            {
                return estimateAcceleration ? ((IPredictionModel)_linearFallback).Velocity(context, seconds)
                    : motion.Velocity + motion.Acceleration * seconds;
            }

            Double3 velocity = SpatialMath.Rotate(motion.Attitude * SpatialMath.AngularDisplacement(motion.Rate, seconds),
                motion.Velocity + motion.Acceleration * seconds);
            return context.Space == ReferenceSpace.Geographic ? motion.Basis.ToEarthCentered(velocity)
                : context.SourceCoordinates.ToSource(velocity);
        }

        internal static Double3 ToLocal(in ExtrapolationContext context, in GeographicBasis basis, Double3 vector)
        {
            return context.Space == ReferenceSpace.Geographic ? basis.ToLocal(vector) : context.SourceCoordinates.ToUnity(vector);
        }

        internal static void Factors(double angle, out double f1, out double f2, out double f3, out double f4)
        {
            if (Math.Abs(angle) < 0.01d)
            {
                // For nearly straight flight, use a series expansion to avoid rounding errors and division by a tiny rate.
                double squared = angle * angle;
                f1 = angle * (0.5d + squared * (-1d / 24d + squared / 720d));
                f2 = squared * (1d / 6d + squared * (-1d / 120d + squared / 5040d));
                f3 = angle * (1d / 3d + squared * (-1d / 30d + squared / 840d));
                f4 = squared * (1d / 8d + squared * (-1d / 144d + squared / 5760d));
            }
            else
            {
                double sine = Math.Sin(angle);
                double cosine = Math.Cos(angle);
                f1 = (1d - cosine) / angle;
                f2 = 1d - sine / angle;
                f3 = (sine - angle * cosine) / (angle * angle);
                f4 = 0.5d - sine / angle + (1d - cosine) / (angle * angle);
            }
        }

        internal static bool LocalAttitude(in ExtrapolationContext context, in GeographicBasis basis, out Quaternion attitude)
        {
            return TryAttitude(context, basis, out attitude);
        }

        internal static Double3 BodyAngularVelocity(in ExtrapolationContext context, in GeographicBasis basis,
            Quaternion attitude, Double3 velocity, bool useBank, float gravity)
        {
            return BodyRate(context, basis, attitude, velocity, useBank, gravity);
        }

        internal static bool EndpointVelocity(in ExtrapolationContext context, in GeographicBasis basis,
            Quaternion attitude, Double3 rate, out Double3 velocity)
        {
            return TryEndpointVelocity(context, basis, attitude, rate, out velocity);
        }

        internal static Double3 IntegrateMotion(Double3 velocity, Double3 acceleration, Double3 rate, double seconds)
        {
            return Integrate(velocity, acceleration, rate, seconds);
        }

        private static Double3 CalculateAngularVelocity(ExtrapolationContext context,
            bool useBank, float gravity, bool estimateAcceleration)
        {
            if (context.AngularVelocity.HasValue)
            {
                return context.AngularVelocity.Value;
            }

            GeographicBasis basis = Basis(context);
            Double3? velocity = context.Velocity;
            if (!velocity.HasValue && TryChord(context, out Double3 chord))
            {
                velocity = chord;
            }

            if (!TryAttitude(context, basis, out Quaternion attitude) || !velocity.HasValue)
            {
                return default(Double3);
            }

            Double3 localVelocity = ToLocal(context, basis, velocity.Value);
            Double3 rate = BodyRate(context, basis, attitude, localVelocity, useBank, gravity);
            if (estimateAcceleration)
            {
                Double3 bodyVelocity = SpatialMath.Rotate(Quaternion.Inverse(attitude), localVelocity);
                EstimateMotion(context, basis, attitude, useBank, gravity, ref rate, ref bodyVelocity, out _);
            }

            return rate;
        }

        private static Motion MotionAt(ExtrapolationContext context, bool useBank, float gravity, bool estimateAcceleration)
        {
            int options = (useBank ? 1 : 0) | (estimateAcceleration ? 2 : 0);
            if (!context.TryPrediction(typeof(CoordinatedTurnExtrapolation), options, gravity, out Motion motion))
            {
                motion = PrepareMotion(context, useBank, gravity, estimateAcceleration);
                context.StorePrediction(typeof(CoordinatedTurnExtrapolation), options, gravity, motion);
            }

            return motion;
        }

        private static Motion PrepareMotion(in ExtrapolationContext context, bool useBank, float gravity, bool estimateAcceleration)
        {
            Motion motion = default(Motion);
            // Raw history also covers a batch received before the first presentation pass.
            Double3? sourceVelocity = context.Velocity;
            if (!sourceVelocity.HasValue && TryChord(context, out Double3 chord))
            {
                sourceVelocity = chord;
            }

            if (!sourceVelocity.HasValue)
            {
                return motion;
            }

            motion.HasVelocity = true;
            motion.Velocity = sourceVelocity.Value;
            motion.Acceleration = context.Acceleration.GetValueOrDefault();
            motion.Basis = Basis(context);
            motion.HasAttitude = TryAttitude(context, motion.Basis, out motion.Attitude);
            if (!motion.HasAttitude)
            {
                return motion;
            }

            // Convert once to the turning body frame. The velocity and acceleration fit
            // depends on observations and model settings, not the requested future time.
            Double3 localVelocity = ToLocal(context, motion.Basis, sourceVelocity.Value);
            Quaternion inverse = Quaternion.Inverse(motion.Attitude);
            motion.Velocity = SpatialMath.Rotate(inverse, localVelocity);
            motion.Rate = BodyRate(context, motion.Basis, motion.Attitude, localVelocity, useBank, gravity);
            motion.Acceleration = default(Double3);
            bool estimated = estimateAcceleration && EstimateMotion(context, motion.Basis, motion.Attitude, useBank, gravity,
                ref motion.Rate, ref motion.Velocity, out motion.Acceleration);
            if (!estimated && (!context.Velocity.HasValue || context.VelocityIsEstimated)
                && TryEndpointVelocity(context, motion.Basis, motion.Attitude, motion.Rate, out Double3 endpoint))
            {
                motion.Velocity = endpoint;
            }

            if (context.Acceleration.HasValue)
            {
                // Remove centripetal acceleration already represented by turning.
                Double3 acceleration = ToLocal(context, motion.Basis, context.Acceleration.Value);
                motion.Acceleration = SpatialMath.Rotate(inverse, acceleration) - Cross(motion.Rate, motion.Velocity);
            }

            return motion;
        }

        private static bool EstimateMotion(in ExtrapolationContext context, in GeographicBasis basis, Quaternion attitude,
            bool useBank, float gravity, ref Double3 rate, ref Double3 velocity, out Double3 acceleration)
        {
            acceleration = default(Double3);
            if (context.Acceleration.HasValue || !TurnAcceleration.TryEstimate(context, basis, attitude, rate, ref velocity, out acceleration))
            {
                return false;
            }

            if (!context.AngularVelocity.HasValue && useBank)
            {
                // Bank inference needs the endpoint speed. Refine the coupled fit once,
                // and use that same rate for both position and orientation prediction.
                Double3 refinedRate = BodyRate(context, basis, attitude, SpatialMath.Rotate(attitude, velocity), useBank, gravity);
                Double3 refinedVelocity = velocity;
                if (TurnAcceleration.TryEstimate(context, basis, attitude, refinedRate, ref refinedVelocity, out Double3 refinedAcceleration))
                {
                    rate = refinedRate;
                    velocity = refinedVelocity;
                    acceleration = refinedAcceleration;
                }
            }

            return true;
        }

        private static Double3 BodyRate(in ExtrapolationContext context, in GeographicBasis basis, Quaternion attitude,
            Double3 velocity, bool useBank, float gravity)
        {
            // Use supplied or estimated turn rates, including zero, before trying to infer a turn from bank.
            if (context.AngularVelocity.HasValue)
            {
                Double3 rate = context.AngularVelocity.Value;
                if (context.AngularVelocityIsWorld)
                {
                    if (context.RotationSpace == RotationSpace.EarthCentered)
                    {
                        rate = basis.ToLocal(rate) * -1d;
                    }

                    rate = SpatialMath.Rotate(Quaternion.Inverse(attitude), rate);
                }

                return rate;
            }

            if (!useBank || !SpatialMath.IsFinite(gravity) || gravity <= 0)
            {
                return default(Double3);
            }

            // Estimate only a steady, level turn. Stop if bank or horizontal speed cannot be calculated.
            double speed = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            Double3 forward = SpatialMath.Rotate(attitude, new Double3(0, 0, 1));
            double headingLength = Math.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
            if (speed < 0.001d || headingLength < 0.001d)
            {
                return default(Double3);
            }

            Double3 right = SpatialMath.Rotate(attitude, new Double3(1, 0, 0));
            double levelRight = (right.X * forward.Z - right.Z * forward.X) / headingLength;
            double bank = Math.Atan2(-right.Y, levelRight);
            if (Math.Abs(bank) >= 85d * Math.PI / 180d)
            {
                return default(Double3);
            }

            double turnRate = gravity * Math.Tan(bank) / speed;
            return SpatialMath.Rotate(Quaternion.Inverse(attitude), new Double3(0, turnRate, 0));
        }

        private static bool TryAttitude(in ExtrapolationContext context, in GeographicBasis basis, out Quaternion attitude)
        {
            // Calculate turns from the local body rotation, even when the SDK supplies an ECEF rotation.
            attitude = Quaternion.identity;
            if (!context.ObservationRotation.HasValue || (context.Space == ReferenceSpace.Geographic && !context.ObservationPosition.HasValue))
            {
                return false;
            }

            Quaternion rotation = context.ObservationRotation.Value;
            if (context.RotationSpace == RotationSpace.EarthCentered)
            {
                if (!context.ObservationPosition.HasValue)
                {
                    return false;
                }

                attitude = basis.ToLocalEarthCenteredRotation(rotation, context.EarthCenteredBodyAxes.Value);
            }
            else
            {
                attitude = context.RotationSpace == RotationSpace.Source ? context.SourceCoordinates.ToUnityRotation(rotation) : rotation;
            }

            return true;
        }

        private static GeographicBasis Basis(in ExtrapolationContext context)
        {
            return context.ObservationRotation.HasValue && context.ObservationPosition.HasValue
                && (context.Space == ReferenceSpace.Geographic || context.RotationSpace == RotationSpace.EarthCentered)
                    ? context.GeographicBasis : default(GeographicBasis);
        }

        private static bool TryChord(in ExtrapolationContext context, out Double3 velocity)
        {
            velocity = default(Double3);
            if (!context.ObservationTime.HasValue
                || !context.TryGetPositionSample(0, out Timestamp newest, out Double3 position)
                || !context.TryGetPositionSample(1, out Timestamp previous, out Double3 older))
            {
                return false;
            }

            double span = newest.ElapsedSince(previous);
            if (span < MinimumSpan)
            {
                return false;
            }

            velocity = (position - older) * (1d / span);
            return true;
        }

        private static bool TryEndpointVelocity(in ExtrapolationContext context, in GeographicBasis basis,
            Quaternion attitude, Double3 rate, out Double3 velocity)
        {
            velocity = default(Double3);
            if (!context.ObservationTime.HasValue
                || !context.TryGetPositionSample(0, out Timestamp newest, out Double3 position)
                || !newest.Equals(context.ObservationTime.Value))
            {
                return false;
            }

            Quaternion inverse = Quaternion.Inverse(attitude);
            Double3? acceleration = context.Acceleration.HasValue
                ? SpatialMath.Rotate(inverse, ToLocal(context, basis, context.Acceleration.Value)) : (Double3?)null;
            double weight = 0;
            for (int index = 1; index < MaximumPositionSamples; index++)
            {
                if (!context.TryGetPositionSample(index, out Timestamp time, out Double3 older))
                {
                    break;
                }

                double span = newest.ElapsedSince(time);
                if (span > PositionWindowSeconds && index > 1)
                {
                    break;
                }

                if (span < MinimumSpan)
                {
                    continue;
                }

                // Each chord is an integral of rotating velocity, not an endpoint tangent.
                // Invert that same integral before combining samples. Weight longer chords
                // more strongly because dividing position noise by a short span amplifies it.
                Double3 delta = SpatialMath.Rotate(inverse, ToLocal(context, basis, older - position));
                if (TryInvertDisplacement(delta, acceleration, rate, -span, out Double3 endpoint))
                {
                    double sampleWeight = span * span;
                    velocity += endpoint * sampleWeight;
                    weight += sampleWeight;
                }
            }

            if (weight == 0)
            {
                return false;
            }

            velocity *= 1d / weight;
            return true;
        }

        private static bool TryInvertDisplacement(Double3 displacement, Double3? acceleration,
            Double3 rate, double seconds, out Double3 velocity)
        {
            Double3 average = displacement * (1d / seconds);
            double magnitude = Math.Sqrt(rate.X * rate.X + rate.Y * rate.Y + rate.Z * rate.Z);
            if (magnitude == 0)
            {
                velocity = average - acceleration.GetValueOrDefault() * (0.5d * seconds);
                return true;
            }

            Double3 axis = rate * (1d / magnitude);
            double angle = magnitude * seconds;
            Factors(angle, out double f1, out double f2, out double f3, out double f4);
            double real = 1d - f2;
            double imaginary = f1;
            if (acceleration.HasValue)
            {
                // Supplied acceleration is inertial. Since aBody = aInBody - omega x vBody,
                // the velocity coefficient is A - B*omega, rather than A alone.
                Double3 cross = Cross(axis, acceleration.Value);
                average -= (acceleration.Value * 0.5d + cross * f3 + Cross(axis, cross) * f4) * seconds;
                real += angle * f3;
                imaginary -= angle * (0.5d - f4);
            }

            // Rotation leaves the axial component unchanged. In its perpendicular plane,
            // invert the two coefficients as a complex number. A full revolution can make
            // a position chord uninformative; skip that sample instead of magnifying noise.
            double denominator = real * real + imaginary * imaginary;
            velocity = default(Double3);
            if (denominator < 0.0001d)
            {
                return false;
            }

            Double3 parallel = axis * (axis.X * average.X + axis.Y * average.Y + axis.Z * average.Z);
            Double3 perpendicular = average - parallel;
            velocity = parallel + (perpendicular * real - Cross(axis, perpendicular) * imaginary) * (1d / denominator);
            return true;
        }

        private static Double3 Integrate(Double3 velocity, Double3 acceleration, Double3 rate, double seconds)
        {
            double magnitude = Math.Sqrt(rate.X * rate.X + rate.Y * rate.Y + rate.Z * rate.Z);
            if (magnitude == 0)
            {
                return velocity * seconds + acceleration * (0.5d * seconds * seconds);
            }

            Double3 axis = rate * (1d / magnitude);
            double angle = magnitude * seconds;
            Factors(angle, out double f1, out double f2, out double f3, out double f4);

            // Use the exact Rodrigues formula to sum exp(omega*t)*(v_body + a_body*t) over this time interval.
            Double3 vCross = Cross(axis, velocity);
            Double3 aCross = Cross(axis, acceleration);
            return (velocity + vCross * f1 + Cross(axis, vCross) * f2) * seconds
                + (acceleration * 0.5d + aCross * f3 + Cross(axis, aCross) * f4) * (seconds * seconds);
        }

        private static Double3 Cross(Double3 left, Double3 right)
        {
            return new Double3(left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);
        }
    }
}
