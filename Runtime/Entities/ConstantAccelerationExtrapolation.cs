using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Predicts constant linear and angular acceleration, estimating missing motion from timestamped history.</summary>
    /// <remarks>Supplied linear acceleration takes priority, including zero. Missing acceleration uses recent SDK
    /// velocities or a quadratic position fit. Angular acceleration uses SDK rates or rotation arcs. Supplied zero
    /// angular velocity holds attitude. Fits use at most eight rates or nine orientations from a short window,
    /// retaining the minimum samples needed at low source rates. Instances have no mutable entity state and may be shared.</remarks>
    [Serializable]
    public sealed class ConstantAccelerationExtrapolation : IExtrapolationStrategy, IPredictionModel
    {
        private enum Samples
        {
            Position,
            Velocity,
            AngularVelocity
        }

        private struct LinearMotion
        {
            internal Double3? Velocity;
            internal Double3? Acceleration;
        }

        private struct AngularMotion
        {
            internal Double3 Velocity;
            internal Double3 Acceleration;
        }

        private struct Fit
        {
            internal double Count;
            internal double T1;
            internal double T2;
            internal double T3;
            internal double T4;
            internal Double3 Y0;
            internal Double3 Y1;
            internal Double3 Y2;

            internal void Add(double time, Double3 value)
            {
                double squared = time * time;
                Count++;
                T1 += time;
                T2 += squared;
                Y0 += value;
                Y1 += value * time;
            }

            internal void AddQuadratic(double time, Double3 value)
            {
                Add(time, value);
                double squared = time * time;
                T3 += squared * time;
                T4 += squared * squared;
                Y2 += value * squared;
            }

            internal bool Line(out Double3 value, out Double3 slope)
            {
                value = default(Double3);
                slope = default(Double3);
                double denominator = T2 - T1 * T1 / Count;
                if (Count < 2 || denominator <= 1e-12)
                {
                    return false;
                }

                slope = (Y1 - Y0 * (T1 / Count)) * (1d / denominator);
                value = (Y0 - slope * T1) * (1d / Count);
                return true;
            }

            internal bool Quadratic(out Double3 velocity, out Double3 acceleration)
            {
                velocity = default(Double3);
                acceleration = default(Double3);
                double a = T2 - T1 * T1 / Count;
                double b = T3 - T1 * T2 / Count;
                double c = T4 - T2 * T2 / Count;
                double determinant = a * c - b * b;
                if (Count < 3 || determinant <= 1e-12)
                {
                    return false;
                }

                // Eliminate the fitted intercept, then solve for the linear and quadratic terms.
                Double3 linear = Y1 - Y0 * (T1 / Count);
                Double3 quadratic = Y2 - Y0 * (T2 / Count);
                velocity = (linear * c - quadratic * b) * (1d / determinant);
                acceleration = (quadratic * a - linear * b) * (2d / determinant);
                return true;
            }
        }

        private const int MaximumSamples = 8;
        private const double WindowSeconds = 0.25d;
        private const double MinimumSpan = 0.0001d;

        /// <summary>Predicts the complete pose and motion using this model.</summary>
        /// <param name="request">Independently timed observations and bounded prediction intervals.</param>
        /// <returns>The absolute source pose and its linear and angular velocity before smoothing.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Extends linear velocity with one half acceleration times squared duration.</summary>
        /// <param name="context">Time-aligned SDK motion and retained timestamped observations. SDK acceleration overrides estimation.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>Source-space displacement from constant linear acceleration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (seconds == 0)
            {
                return default(Double3);
            }

            LinearMotion motion = LinearAt(context);

            // Acceleration alone does not tell us the starting velocity.
            return motion.Velocity.HasValue ? motion.Velocity.Value * seconds
                + motion.Acceleration.GetValueOrDefault() * (0.5d * seconds * seconds) : default(Double3);
        }

        /// <summary>Integrates angular velocity and history-derived angular acceleration in the supplied body or world frame.</summary>
        /// <param name="context">Time-aligned angular motion and retained SDK rate or orientation observations.</param>
        /// <param name="seconds">Finite, nonnegative extrapolation duration in seconds.</param>
        /// <returns>A quaternion angular offset in the supplied vector's basis.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (seconds == 0 || (context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated
                && context.AngularVelocity.Value == default(Double3)))
            {
                return Quaternion.identity;
            }

            AngularMotion motion = AngularAt(context);

            return IntegrateAngular(motion.Velocity, motion.Acceleration, seconds, context.AngularVelocityIsWorld);
        }

        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            LinearMotion motion = LinearAt(context);
            return motion.Velocity.HasValue ? motion.Velocity.Value + motion.Acceleration.GetValueOrDefault() * seconds : default(Double3);
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            AngularMotion motion = AngularAt(context);
            return motion.Velocity + motion.Acceleration * seconds;
        }

        private static LinearMotion LinearAt(ExtrapolationContext context)
        {
            if (!context.TryPrediction(typeof(ConstantAccelerationExtrapolation), 0, 0, out LinearMotion motion))
            {
                motion = new LinearMotion { Velocity = context.Velocity, Acceleration = context.Acceleration };
                if (context.ObservationTime.HasValue)
                {
                    try
                    {
                        EstimateLinear(context, ref motion.Velocity, ref motion.Acceleration);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Unrepresentable fits cannot invalidate otherwise usable SDK motion.
                        motion.Velocity = context.Velocity;
                        motion.Acceleration = context.Acceleration;
                    }
                }

                context.StorePrediction(typeof(ConstantAccelerationExtrapolation), 0, 0, motion);
            }

            return motion;
        }

        private static AngularMotion AngularAt(ExtrapolationContext context)
        {
            if (context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated
                && context.AngularVelocity.Value == default(Double3))
            {
                return default(AngularMotion);
            }

            if (!context.TryPrediction(typeof(ConstantAccelerationExtrapolation), 1, 0, out AngularMotion motion))
            {
                motion = new AngularMotion { Velocity = context.AngularVelocity.GetValueOrDefault() };
                if (context.ObservationTime.HasValue)
                {
                    try
                    {
                        EstimateAngular(context, ref motion.Velocity, ref motion.Acceleration);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        motion.Velocity = context.AngularVelocity.GetValueOrDefault();
                        motion.Acceleration = default(Double3);
                    }
                }

                context.StorePrediction(typeof(ConstantAccelerationExtrapolation), 1, 0, motion);
            }

            return motion;
        }

        private static void EstimateLinear(in ExtrapolationContext context, ref Double3? velocity, ref Double3? acceleration)
        {
            if (!velocity.HasValue || context.VelocityIsEstimated)
            {
                if (FitPosition(context, out Double3 fittedVelocity, out Double3 fittedAcceleration))
                {
                    // Position differences give interval-average velocity. The quadratic derivative
                    // supplies velocity at the actual observation endpoint instead.
                    velocity = fittedVelocity;
                    acceleration = acceleration ?? fittedAcceleration;
                }
                else if (!velocity.HasValue && context.TryGetPositionSample(0, out Timestamp newest, out Double3 after)
                    && context.TryGetPositionSample(1, out Timestamp previous, out Double3 before))
                {
                    double elapsed = newest.ElapsedSince(previous);
                    if (elapsed >= MinimumSpan)
                    {
                        velocity = (after - before) * (1d / elapsed);
                    }
                }
            }
            else if (!acceleration.HasValue)
            {
                if (FitRates(context, Samples.Velocity, out _, out Double3 slope, out Timestamp newest))
                {
                    acceleration = slope;
                    if (context.LatestVelocityTime.HasValue && context.LatestVelocityTime.Value.CompareTo(context.ObservationTime.Value) <= 0)
                    {
                        velocity += slope * context.ObservationTime.Value.ElapsedSince(newest);
                    }
                }
                else if (FitPosition(context, out _, out Double3 fittedAcceleration))
                {
                    acceleration = fittedAcceleration;
                }
            }
        }

        private static void EstimateAngular(in ExtrapolationContext context, ref Double3 velocity, ref Double3 acceleration)
        {
            if (context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated)
            {
                if (FitRates(context, Samples.AngularVelocity, out _, out Double3 slope, out Timestamp newest))
                {
                    if (context.AngularVelocityIsWorld && context.RotationSpace != RotationSpace.EarthCentered)
                    {
                        if (!context.ObservationPosition.HasValue)
                        {
                            return;
                        }

                        GeographicBasis basis = context.GeographicBasis;
                        slope = basis.ToLocal(slope) * -1d;
                    }

                    acceleration = slope;
                    if (context.LatestAngularVelocityTime.HasValue && context.LatestAngularVelocityTime.Value.CompareTo(context.ObservationTime.Value) <= 0)
                    {
                        velocity += slope * context.ObservationTime.Value.ElapsedSince(newest);
                    }
                }
            }
            else if (FitRotations(context, out Double3 fittedVelocity, out Double3 fittedAcceleration))
            {
                velocity = fittedVelocity;
                acceleration = fittedAcceleration;
            }
        }

        private static bool Window(in ExtrapolationContext context, Samples kind, int minimum,
            out Timestamp newest, out Double3 anchor, out int count, out double span, out bool constant)
        {
            count = 0;
            span = 0;
            constant = true;
            if (!Read(context, kind, 0, out newest, out anchor))
            {
                return false;
            }

            count = 1;
            for (int index = 1; index < MaximumSamples; index++)
            {
                if (!Read(context, kind, index, out Timestamp time, out Double3 sample))
                {
                    break;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && count >= minimum)
                {
                    break;
                }

                count++;
                span = age;
                constant &= sample == anchor;
            }

            return count >= minimum && span >= MinimumSpan;
        }

        private static bool Read(in ExtrapolationContext context, Samples kind, int lookback, out Timestamp time, out Double3 value)
        {
            switch (kind)
            {
                case Samples.Position:
                    return context.TryGetPositionSample(lookback, out time, out value);
                case Samples.Velocity:
                    return context.TryGetVelocitySample(lookback, out time, out value);
                default:
                    return context.TryGetAngularVelocitySample(lookback, out time, out value);
            }
        }

        private static bool FitRates(in ExtrapolationContext context, Samples kind, out Double3 value, out Double3 acceleration, out Timestamp newest)
        {
            value = default(Double3);
            acceleration = default(Double3);
            if (!Window(context, kind, 2, out newest, out Double3 anchor, out int count, out double span, out bool constant))
            {
                return false;
            }

            if (constant)
            {
                value = anchor;
                return true;
            }

            // Normalize time and subtract a nearby value before summing. This preserves
            // precision for large epochs and avoids cancellation for constant rates.
            Fit fit = default(Fit);
            for (int index = 0; index < count; index++)
            {
                Read(context, kind, index, out Timestamp time, out Double3 sample);
                fit.Add(time.ElapsedSince(newest) / span, sample - anchor);
            }

            if (!fit.Line(out Double3 intercept, out Double3 slope))
            {
                return false;
            }

            value = anchor + intercept;
            acceleration = slope * (1d / span);
            return true;
        }

        private static bool FitPosition(in ExtrapolationContext context, out Double3 velocity, out Double3 acceleration)
        {
            velocity = default(Double3);
            acceleration = default(Double3);
            if (!Window(context, Samples.Position, 3, out Timestamp newest, out Double3 anchor, out int count, out double span, out bool constant))
            {
                return false;
            }

            if (constant)
            {
                return true;
            }

            Fit fit = default(Fit);
            for (int index = 0; index < count; index++)
            {
                context.TryGetPositionSample(index, out Timestamp time, out Double3 position);
                fit.AddQuadratic(time.ElapsedSince(newest) / span, position - anchor);
            }

            if (!fit.Quadratic(out Double3 linear, out Double3 quadratic))
            {
                return false;
            }

            acceleration = quadratic * (1d / (span * span));
            velocity = linear * (1d / span) + acceleration * context.ObservationTime.Value.ElapsedSince(newest);
            return true;
        }

        private static bool FitRotations(in ExtrapolationContext context, out Double3 velocity, out Double3 acceleration)
        {
            velocity = default(Double3);
            acceleration = default(Double3);
            if (!context.TryGetRotationSample(0, out Timestamp newest, out _))
            {
                return false;
            }

            int count = 1;
            double span = 0;
            for (int index = 1; index <= MaximumSamples; index++)
            {
                if (!context.TryGetRotationSample(index, out Timestamp time, out _))
                {
                    break;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && count >= 3)
                {
                    break;
                }

                count++;
                span = age;
            }

            if (count < 3 || span < MinimumSpan)
            {
                return false;
            }

            Fit fit = default(Fit);
            Double3 anchor = default(Double3);
            for (int index = 0; index < count - 1; index++)
            {
                context.TryGetRotationSample(index, out Timestamp afterTime, out Quaternion after);
                context.TryGetRotationSample(index + 1, out Timestamp beforeTime, out Quaternion before);
                double elapsed = afterTime.ElapsedSince(beforeTime);
                if (elapsed < MinimumSpan)
                {
                    continue;
                }

                // Rotation arcs measure average BODY rate at the interval midpoint. Fitting
                // those midpoints recovers endpoint rate and acceleration without Euler-angle wrapping.
                Double3 rate = SpatialMath.RotationRate(Quaternion.identity, Quaternion.Inverse(before) * after, elapsed);
                if (context.RotationSpace == RotationSpace.Source)
                {
                    rate = context.SourceCoordinates.ToUnityAngularVelocity(rate);
                }
                else if (context.RotationSpace == RotationSpace.EarthCentered)
                {
                    rate = context.EarthCenteredBodyAxes.Value.ToUnityAngularVelocity(rate);
                }

                if (fit.Count == 0)
                {
                    anchor = rate;
                }

                fit.Add((afterTime.ElapsedSince(newest) - elapsed * 0.5d) / span, rate - anchor);
            }

            if (!fit.Line(out Double3 intercept, out Double3 slope))
            {
                return false;
            }

            acceleration = slope * (1d / span);
            velocity = anchor + intercept + acceleration * context.ObservationTime.Value.ElapsedSince(newest);
            return true;
        }

        private static Quaternion IntegrateAngular(Double3 velocity, Double3 acceleration, double seconds, bool world)
        {
            // Collinear angular vectors commute, giving an exact single exponential for
            // ordinary accelerating bank/pitch/yaw. Constant-rate motion keeps the same fast path.
            double velocityScale = Math.Max(Math.Abs(velocity.X), Math.Max(Math.Abs(velocity.Y), Math.Abs(velocity.Z)));
            double accelerationScale = Math.Max(Math.Abs(acceleration.X), Math.Max(Math.Abs(acceleration.Y), Math.Abs(acceleration.Z)));
            if (velocityScale == 0 || accelerationScale == 0 || Parallel(velocity * (1d / velocityScale), acceleration * (1d / accelerationScale)))
            {
                return SpatialMath.AngularDisplacement(velocity * seconds + acceleration * (0.5d * seconds * seconds), 1d);
            }

            // Changing axes do not commute. Bounded midpoint steps retain body/world composition
            // without creating a shared per-entity integrator or mutating preview state.
            int steps = (int)Math.Min(64d, Math.Max(1d, Math.Ceiling(seconds * 120d)));
            double step = seconds / steps;
            Quaternion rotation = Quaternion.identity;
            for (int index = 0; index < steps; index++)
            {
                Double3 rate = velocity + acceleration * (seconds * ((index + 0.5d) / steps));
                Quaternion increment = SpatialMath.AngularDisplacement(rate, step);
                rotation = world ? increment * rotation : rotation * increment;
            }

            return SpatialMath.NormalizeRotation(rotation, nameof(seconds));
        }

        private static bool Parallel(Double3 left, Double3 right)
        {
            double x = left.Y * right.Z - left.Z * right.Y;
            double y = left.Z * right.X - left.X * right.Z;
            double z = left.X * right.Y - left.Y * right.X;
            return x * x + y * y + z * z <= 1e-12;
        }
    }
}
