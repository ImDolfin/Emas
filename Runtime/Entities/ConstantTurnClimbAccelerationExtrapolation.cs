using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Predicts a horizontal constant-rate turn with tangential acceleration and constant vertical climb rate.</summary>
    /// <remarks>Climb rate comes from the vertical velocity at the observation; vertical acceleration is excluded from
    /// the future trajectory. Roll and pitch predict attitude independently of horizontal translation. SDK velocity
    /// and gravity-free inertial acceleration take priority over history estimates. Missing motion uses bounded raw
    /// histories; acceleration alone cannot invent velocity. Geographic motion uses local east/up/north at the observation.
    /// Timing, caps and optional Smoothing remain caller-owned. Immutable instances may be shared.</remarks>
    [Serializable]
    public sealed class ConstantTurnClimbAccelerationExtrapolation : IExtrapolationStrategy, IPredictionModel, IPacketSmoothingStrategy
    {
        private struct Motion
        {
            internal bool HasVelocity;
            internal GeographicBasis Basis;
            internal Quaternion Attitude;
            internal bool HasAttitude;
            internal Double3 Velocity;
            internal Double3 Acceleration;
            internal double TurnRate;
        }

        [Tooltip("Cubic packet correction duration in unscaled seconds. Zero applies corrections immediately. Independent of the prediction limit.")]
        [Min(0)]
        [SerializeField] private float _packetSmoothingDuration = 0.1f;
        [Tooltip("Infer a steady level-turn rate from bank only when angular motion, supplied acceleration and trajectory history cannot provide it.")]
        [SerializeField] private bool _useBankAngleWhenRatesUnavailable = true;
        [HideInInspector]
        [SerializeField] private float _gravity = 9.80665f;
        private const int MaximumSamples = 8;
        private const double WindowSeconds = 0.25d;
        private const double MinimumInterval = 0.0001d;
        private const double MinimumSpeedSquared = 1e-8d;

        /// <summary>Gets or sets the duration of cubic packet correction in unscaled seconds.</summary>
        /// <value>A finite, nonnegative duration, defaulting to 0.1 seconds. Zero bypasses packet correction.</value>
        /// <remarks>Assign a new strategy instance to blend changes to other model settings.</remarks>
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

        /// <summary>Gets or sets whether bank may supply a missing horizontal turn rate.</summary>
        /// <value>True by default. Angular motion, supplied normal acceleration and course history take priority.</value>
        /// <remarks>Uses the coordinated level-turn assumption. Declines negligible horizontal speed, vertical heading
        /// and bank magnitudes of 85 degrees or more. A supplied zero angular rate holds the turn rate at zero.</remarks>
        public bool UseBankAngleWhenRatesUnavailable
        {
            get => _useBankAngleWhenRatesUnavailable;
            set => _useBankAngleWhenRatesUnavailable = value;
        }

        /// <summary>Gets or sets gravity used by the bank-derived turn-rate fallback.</summary>
        /// <value>A finite, positive acceleration in source units per second squared; defaults to 9.80665 for metres.</value>
        /// <remarks>Retained through serialization and code; hidden in the Inspector.</remarks>
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

        /// <summary>Integrates horizontal turning and tangential acceleration while extending vertical velocity linearly.</summary>
        /// <param name="context">Time-aligned pose, SDK motion and timestamped history. Vertical speed supplies climb rate.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the position observation, already bounded by the prediction cap.</param>
        /// <returns>Displacement in Cartesian source units or ECEF metres; zero without velocity information.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (seconds == 0 || !TryMotion(context, out Motion motion))
            {
                return default(Double3);
            }

            // Turn and accelerate only in the horizontal plane. The independent climb
            // term cannot be rotated into lateral motion by bank or pitch.
            Double3 horizontal = new Double3(motion.Velocity.X, 0, motion.Velocity.Z);
            Double3 displacement = CoordinatedTurnExtrapolation.IntegrateMotion(horizontal, motion.Acceleration,
                new Double3(0, motion.TurnRate, 0), seconds) + new Double3(0, motion.Velocity.Y * seconds, 0);
            return context.Space == ReferenceSpace.Geographic
                ? motion.Basis.ToEarthCentered(displacement) : context.SourceCoordinates.ToSource(displacement);
        }

        /// <summary>Predicts observed attitude with supplied or estimated body/world rates, or a course-derived yaw fallback.</summary>
        /// <param name="context">Angular motion and pose history. Bank and pitch remain independent of the climb trajectory.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the rotation observation, already bounded by the prediction cap.</param>
        /// <returns>A quaternion offset for the existing body/world pipeline; identity without angular or course motion.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (seconds == 0)
            {
                return Quaternion.identity;
            }

            if (context.AngularVelocity.HasValue)
            {
                return CoordinatedTurnExtrapolation.PredictRotation(context, seconds, _useBankAngleWhenRatesUnavailable, _gravity, false);
            }

            if (!TryMotion(context, out Motion motion) || !motion.HasAttitude)
            {
                return Quaternion.identity;
            }

            // The caller composes missing-rate offsets in body axes. Express the inferred
            // world yaw there so the observed bank and pitch are preserved.
            Double3 bodyRate = SpatialMath.Rotate(Quaternion.Inverse(motion.Attitude), new Double3(0, motion.TurnRate, 0));
            return SpatialMath.AngularDisplacement(bodyRate, seconds);
        }

        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            if (!TryMotion(context, out Motion motion))
            {
                return default(Double3);
            }

            Double3 horizontal = new Double3(motion.Velocity.X, 0, motion.Velocity.Z);
            Double3 velocity = SpatialMath.Rotate(SpatialMath.AngularDisplacement(new Double3(0, motion.TurnRate, 0), seconds),
                horizontal + motion.Acceleration * seconds) + new Double3(0, motion.Velocity.Y, 0);
            return context.Space == ReferenceSpace.Geographic ? motion.Basis.ToEarthCentered(velocity)
                : context.SourceCoordinates.ToSource(velocity);
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            if (context.AngularVelocity.HasValue)
            {
                return context.AngularVelocity.Value;
            }

            return TryMotion(context, out Motion motion) && motion.HasAttitude
                ? SpatialMath.Rotate(Quaternion.Inverse(motion.Attitude), new Double3(0, motion.TurnRate, 0)) : default(Double3);
        }

        private bool TryMotion(in ExtrapolationContext context, out Motion motion)
        {
            int options = _useBankAngleWhenRatesUnavailable ? 1 : 0;
            if (!context.TryPrediction(typeof(ConstantTurnClimbAccelerationExtrapolation), options, _gravity, out motion))
            {
                bool available = ComputeMotion(context, out motion);
                motion.HasVelocity = available;
                context.StorePrediction(typeof(ConstantTurnClimbAccelerationExtrapolation), options, _gravity, motion);
            }

            return motion.HasVelocity;
        }

        private bool ComputeMotion(in ExtrapolationContext context, out Motion motion)
        {
            motion = default(Motion);
            if (context.ObservationPosition.HasValue && (context.Space == ReferenceSpace.Geographic
                || context.RotationSpace == RotationSpace.EarthCentered))
            {
                motion.Basis = context.GeographicBasis;
            }

            Double3? velocity = context.Velocity;
            if (!velocity.HasValue && context.TryGetPositionSample(0, out Timestamp newest, out Double3 after)
                && context.TryGetPositionSample(1, out Timestamp previous, out Double3 before))
            {
                double span = newest.ElapsedSince(previous);
                if (span >= MinimumInterval)
                {
                    velocity = (after - before) * (1d / span);
                }
            }

            if (!velocity.HasValue)
            {
                return false;
            }

            motion.Velocity = CoordinatedTurnExtrapolation.ToLocal(context, motion.Basis, velocity.Value);
            motion.HasAttitude = CoordinatedTurnExtrapolation.LocalAttitude(context, motion.Basis, out motion.Attitude);
            motion.TurnRate = TurnRate(context, motion, out bool inferred);
            double climb = ClimbRate(context, motion.Basis, motion.Velocity.Y);
            Double3 estimatedAcceleration = default(Double3);
            bool suppliedVelocity = context.Velocity.HasValue && !context.VelocityIsEstimated;
            if (suppliedVelocity && context.Acceleration.HasValue)
            {
                AlignVelocity(context, ref motion);
            }

            bool fit = false;
            if (!suppliedVelocity || !context.Acceleration.HasValue)
            {
                fit = TurnAcceleration.TryEstimate(context, motion.Basis, Quaternion.identity,
                    new Double3(0, motion.TurnRate, 0), ref motion.Velocity, out estimatedAcceleration);
            }

            if (!fit && !suppliedVelocity && CoordinatedTurnExtrapolation.EndpointVelocity(context, motion.Basis,
                Quaternion.identity, new Double3(0, motion.TurnRate, 0), out Double3 endpoint))
            {
                motion.Velocity = endpoint;
            }

            if (inferred && fit && !context.Acceleration.HasValue)
            {
                // A course estimate from chords can lag during acceleration. Remove the
                // fitted lateral acceleration by refining the turn rate, leaving only
                // tangential acceleration. Each bounded refinement reuses the exact turn fit.
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    double squaredSpeed = HorizontalSquared(motion.Velocity);
                    if (squaredSpeed < MinimumSpeedSquared)
                    {
                        break;
                    }

                    double correction = NormalAcceleration(motion.Velocity, estimatedAcceleration) / squaredSpeed;
                    if (Math.Abs(correction) < 1e-8d)
                    {
                        break;
                    }

                    double refinedRate = motion.TurnRate + correction;
                    Double3 refinedVelocity = motion.Velocity;
                    if (!TurnAcceleration.TryEstimate(context, motion.Basis, Quaternion.identity,
                        new Double3(0, refinedRate, 0), ref refinedVelocity, out Double3 refinedAcceleration))
                    {
                        break;
                    }

                    motion.TurnRate = refinedRate;
                    motion.Velocity = refinedVelocity;
                    estimatedAcceleration = refinedAcceleration;
                }
            }

            motion.Velocity = new Double3(motion.Velocity.X, climb, motion.Velocity.Z);
            // SDK acceleration is inertial. Only its along-course component changes
            // horizontal speed; normal acceleration is already represented by turning.
            Double3 acceleration = context.Acceleration.HasValue
                ? CoordinatedTurnExtrapolation.ToLocal(context, motion.Basis, context.Acceleration.Value) : estimatedAcceleration;
            Double3 direction = new Double3(motion.Velocity.X, 0, motion.Velocity.Z);
            double squared = HorizontalSquared(direction);
            if (squared < MinimumSpeedSquared)
            {
                direction = new Double3(acceleration.X, 0, acceleration.Z);
                squared = HorizontalSquared(direction);
            }

            motion.Acceleration = squared >= MinimumSpeedSquared
                ? direction * ((direction.X * acceleration.X + direction.Z * acceleration.Z) / squared) : default(Double3);
            return true;
        }

        private static void AlignVelocity(in ExtrapolationContext context, ref Motion motion)
        {
            if (!context.ObservationTime.HasValue || !context.LatestVelocityTime.HasValue
                || context.LatestVelocityTime.Value.CompareTo(context.ObservationTime.Value) >= 0
                || !context.TryGetVelocitySample(0, out Timestamp time, out Double3 raw))
            {
                return;
            }

            // Prepared velocity uses straight inertial acceleration for an older SDK
            // sample. Align its horizontal part through this model's turning trajectory.
            Double3 before = CoordinatedTurnExtrapolation.ToLocal(context, motion.Basis, raw);
            double squared = HorizontalSquared(before);
            double age = context.ObservationTime.Value.ElapsedSince(time);
            Double3 acceleration = CoordinatedTurnExtrapolation.ToLocal(context, motion.Basis, context.Acceleration.Value);
            if (squared < MinimumSpeedSquared)
            {
                // Starting from rest, a_world = a_t*(I + omega*age*J)*direction.
                // Invert that planar relation before integrating the elapsed speed;
                // the normal component must not become sideways velocity at startup.
                double turn = motion.TurnRate * age;
                double inverse = age / (1d + turn * turn);
                motion.Velocity = new Double3((acceleration.X - turn * acceleration.Z) * inverse,
                    motion.Velocity.Y, (acceleration.Z + turn * acceleration.X) * inverse);
                return;
            }

            double angle = motion.TurnRate * age;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            double speed = Math.Sqrt(squared);
            Double3 direction = new Double3((before.X * cosine + before.Z * sine) / speed, 0,
                (before.Z * cosine - before.X * sine) / speed);
            speed += (direction.X * acceleration.X + direction.Z * acceleration.Z) * age;
            motion.Velocity = direction * speed + new Double3(0, motion.Velocity.Y, 0);
        }

        private static double ClimbRate(in ExtrapolationContext context, in GeographicBasis basis, double fallback)
        {
            if (context.Velocity.HasValue && !context.VelocityIsEstimated)
            {
                // Vertical acceleration must not advance a held SDK climb rate, including
                // acceleration that the common motion sampler used for time alignment.
                if (context.ObservationTime.HasValue && context.LatestVelocityTime.HasValue
                    && context.LatestVelocityTime.Value.CompareTo(context.ObservationTime.Value) <= 0
                    && context.TryGetVelocitySample(0, out _, out Double3 velocity))
                {
                    return CoordinatedTurnExtrapolation.ToLocal(context, basis, velocity).Y;
                }

                return fallback;
            }

            if (!context.TryGetPositionSample(0, out Timestamp newest, out Double3 position))
            {
                return fallback;
            }

            double numerator = 0;
            double denominator = 0;
            for (int index = 1; index < MaximumSamples; index++)
            {
                if (!context.TryGetPositionSample(index, out Timestamp time, out Double3 older))
                {
                    break;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && denominator > 0)
                {
                    break;
                }

                if (age < MinimumInterval)
                {
                    continue;
                }

                double height = CoordinatedTurnExtrapolation.ToLocal(context, basis, older - position).Y;
                numerator -= age * height;
                denominator += age * age;
            }

            return denominator > 0 ? numerator / denominator : fallback;
        }

        private double TurnRate(in ExtrapolationContext context, Motion motion, out bool inferred)
        {
            inferred = false;
            if (context.AngularVelocity.HasValue)
            {
                if (context.AngularVelocity.Value == default(Double3))
                {
                    return 0;
                }

                if (motion.HasAttitude)
                {
                    Double3 angular;
                    if (context.AngularVelocityIsWorld)
                    {
                        angular = context.RotationSpace == RotationSpace.EarthCentered
                            ? motion.Basis.ToLocal(context.AngularVelocity.Value) * -1d : context.AngularVelocity.Value;
                    }
                    else
                    {
                        angular = SpatialMath.Rotate(motion.Attitude, context.AngularVelocity.Value);
                    }
                    Double3 forward = SpatialMath.Rotate(motion.Attitude, new Double3(0, 0, 1));
                    double horizontal = HorizontalSquared(forward);
                    if (horizontal >= MinimumSpeedSquared)
                    {
                        // Heading is atan2(forward.x, forward.z). Differentiate it rather
                        // than using angular.y: pure body roll at a pitched attitude must
                        // not become a spurious horizontal turn.
                        return angular.Y - forward.Y * (angular.X * forward.X + angular.Z * forward.Z) / horizontal;
                    }
                }
                else if (context.AngularVelocityIsWorld)
                {
                    Double3 angular = context.RotationSpace == RotationSpace.EarthCentered
                        ? motion.Basis.ToLocal(context.AngularVelocity.Value) * -1d : context.AngularVelocity.Value;
                    return angular.Y;
                }
            }

            double squaredSpeed = HorizontalSquared(motion.Velocity);
            if (context.Acceleration.HasValue && squaredSpeed >= MinimumSpeedSquared)
            {
                Double3 acceleration = CoordinatedTurnExtrapolation.ToLocal(context, motion.Basis, context.Acceleration.Value);
                return NormalAcceleration(motion.Velocity, acceleration) / squaredSpeed;
            }

            if (CourseRate(context, motion.Basis, true, out double rate)
                || CourseRate(context, motion.Basis, false, out rate))
            {
                inferred = true;
                return rate;
            }

            if (motion.HasAttitude)
            {
                Double3 body = CoordinatedTurnExtrapolation.BodyAngularVelocity(context, motion.Basis,
                    motion.Attitude, motion.Velocity, _useBankAngleWhenRatesUnavailable, _gravity);
                return SpatialMath.Rotate(motion.Attitude, body).Y;
            }

            return 0;
        }

        private static bool CourseRate(in ExtrapolationContext context, in GeographicBasis basis, bool velocities, out double rate)
        {
            rate = 0;
            if (!Read(context, velocities, 0, out Timestamp newest, out Double3 after))
            {
                return false;
            }

            Timestamp afterTime = newest;
            Double3 newerChord = default(Double3);
            double newerMidpoint = 0;
            bool hasChord = false;
            double numerator = 0;
            double denominator = 0;
            for (int index = 1; index < MaximumSamples; index++)
            {
                if (!Read(context, velocities, index, out Timestamp time, out Double3 before))
                {
                    break;
                }

                if (newest.ElapsedSince(time) > WindowSeconds && denominator > 0)
                {
                    break;
                }

                double span = afterTime.ElapsedSince(time);
                if (span < MinimumInterval)
                {
                    continue;
                }

                Double3 current;
                Double3 previous;
                double interval;
                if (velocities)
                {
                    current = CoordinatedTurnExtrapolation.ToLocal(context, basis, after);
                    previous = CoordinatedTurnExtrapolation.ToLocal(context, basis, before);
                    interval = span;
                }
                else
                {
                    Double3 chord = CoordinatedTurnExtrapolation.ToLocal(context, basis, (after - before) * (1d / span));
                    double midpoint = afterTime.ElapsedSince(newest) - span * 0.5d;
                    current = newerChord;
                    previous = chord;
                    interval = newerMidpoint - midpoint;
                    newerChord = chord;
                    newerMidpoint = midpoint;
                    if (!hasChord)
                    {
                        hasChord = true;
                        after = before;
                        afterTime = time;
                        continue;
                    }
                }

                if (HorizontalSquared(current) >= MinimumSpeedSquared && HorizontalSquared(previous) >= MinimumSpeedSquared)
                {
                    double angle = Math.Atan2(current.X * previous.Z - current.Z * previous.X,
                        current.X * previous.X + current.Z * previous.Z);
                    numerator += angle * interval;
                    denominator += interval * interval;
                }

                after = before;
                afterTime = time;
            }

            if (denominator == 0)
            {
                return false;
            }

            rate = numerator / denominator;
            return true;
        }

        private static bool Read(in ExtrapolationContext context, bool velocities, int index, out Timestamp time, out Double3 value)
        {
            return velocities ? context.TryGetVelocitySample(index, out time, out value)
                : context.TryGetPositionSample(index, out time, out value);
        }

        private static double HorizontalSquared(Double3 value)
        {
            return value.X * value.X + value.Z * value.Z;
        }

        private static double NormalAcceleration(Double3 velocity, Double3 acceleration)
        {
            return velocity.Z * acceleration.X - velocity.X * acceleration.Z;
        }
    }
}
