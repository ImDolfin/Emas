using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Continues a timestamp-fitted cubic Bézier curve beyond the newest position and orientation.</summary>
    /// <remarks>Fits up to eight observations in a 0.25-second window, retaining the minimum needed at slower source rates.
    /// Fewer observations reduce the curve to quadratic or linear motion. Supplied velocities use their own history
    /// to fit curve derivatives; SDK acceleration constrains the starting curvature. Supplied zero angular velocity
    /// holds orientation. Pose-derived rotation uses quaternion log space
    /// in the selected body or world frame. The caller owns prediction timing, caps and smoothing. This strategy adds
    /// no packet correction and has no mutable per-entity state. Instances may be shared.</remarks>
    [Serializable]
    public sealed class SplineExtrapolation : IExtrapolationStrategy, IPredictionModel
    {
        private struct Curve
        {
            internal double Span;
            internal Double3 Linear;
            internal Double3 Quadratic;
            internal Double3 Cubic;

            internal Double3 Evaluate(double seconds)
            {
                // Store the Bézier curve in power form to avoid subtracting large control
                // points when evaluating a small offset from the current observation.
                double parameter = seconds / Span;
                return ((Cubic * parameter + Quadratic) * parameter + Linear) * parameter;
            }

            internal Double3 Velocity(double seconds)
            {
                double parameter = seconds / Span;
                return ((Cubic * (3d * parameter) + Quadratic * 2d) * parameter + Linear) * (1d / Span);
            }
        }

        private struct LinearMotion
        {
            internal bool HasVelocity;
            internal Curve Curve;
        }

        private struct Fit
        {
            internal int Count;
            private double _t2;
            private double _t3;
            private double _t4;
            private double _t5;
            private double _t6;
            private Double3 _y1;
            private Double3 _y2;
            private Double3 _y3;

            internal void Add(double seconds, Double3 offset)
            {
                double squared = seconds * seconds;
                double cubed = squared * seconds;
                Count++;
                _t2 += squared;
                _t3 += cubed;
                _t4 += squared * squared;
                _t5 += cubed * squared;
                _t6 += cubed * cubed;
                _y1 += offset * seconds;
                _y2 += offset * squared;
                _y3 += offset * cubed;
            }

            internal Curve Solve(double span, bool cubic)
            {
                // Fit offsets from the newest observation, forcing the curve through that
                // observation. Normalize time before solving to handle irregular intervals.
                double scale = 1d / span;
                double scale2 = scale * scale;
                double scale3 = scale2 * scale;
                double a = _t2 * scale2;
                double b = _t3 * scale3;
                double c = _t4 * scale2 * scale2;
                double d = _t5 * scale3 * scale2;
                double e = _t6 * scale3 * scale3;
                Double3 first = _y1 * scale;
                Double3 second = _y2 * scale2;
                Double3 third = _y3 * scale3;
                Curve curve = new Curve { Span = span, Linear = first * (1d / a) };
                double quadraticPivot = c - b * b / a;
                if (Count < 2 || quadraticPivot <= 1e-10d * c)
                {
                    return curve;
                }

                // Eliminate the linear coefficient. Drop the cubic term if the retained
                // times do not contain enough independent information to determine it.
                Double3 quadraticRight = second - first * (b / a);
                double cross = d - b * c / a;
                double cubicPivot = e - c * c / a - cross * cross / quadraticPivot;
                if (cubic && Count >= 3 && cubicPivot > 1e-10d * e)
                {
                    curve.Cubic = (third - first * (c / a)
                        - quadraticRight * (cross / quadraticPivot)) * (1d / cubicPivot);
                }

                curve.Quadratic = (quadraticRight - curve.Cubic * cross) * (1d / quadraticPivot);
                curve.Linear = (first - curve.Quadratic * b - curve.Cubic * c) * (1d / a);
                return curve;
            }
        }

        private const int MaximumSamples = 8;
        private const double WindowSeconds = 0.25d;
        private const double MinimumInterval = 0.0001d;

        /// <summary>Predicts the complete pose and motion using this model.</summary>
        /// <param name="request">Independently timed observations and bounded prediction intervals.</param>
        /// <returns>The absolute source pose and its linear and angular velocity after observation filtering.</returns>
        public Prediction Predict(PredictionRequest request)
        {
            return PredictionEvaluator.Evaluate(request, this);
        }

        /// <summary>Continues a cubic position curve fitted to retained timestamped observations.</summary>
        /// <param name="context">Position history and time-aligned SDK derivatives in source axes or ECEF metres.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the observation, already bounded by the prediction cap.</param>
        /// <returns>Source-space displacement. Sparse history uses lower-order curves or supplied motion; no velocity information holds position.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting displacement is invalid.</exception>
        Double3 IPredictionModel.Displacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (context.TryPrediction(typeof(SplineExtrapolation), 0, 0, out LinearMotion prepared))
            {
                return prepared.HasVelocity ? prepared.Curve.Evaluate(seconds) : default(Double3);
            }

            Curve curve = new Curve { Span = 1d, Linear = context.Velocity.GetValueOrDefault() };
            bool hasVelocity = context.Velocity.HasValue;
            try
            {
                bool supplied = context.Velocity.HasValue && !context.VelocityIsEstimated;
                bool fitted = supplied ? TryRateCurve(context, false, out Curve prediction)
                    : TryCurve(context, false, out prediction);
                if (fitted)
                {
                    curve = prediction;
                    hasVelocity = true;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Extreme history cannot invalidate an otherwise usable SDK derivative.
            }

            if (!hasVelocity)
            {
                context.StorePrediction(typeof(SplineExtrapolation), 0, 0, new LinearMotion());
                return default(Double3);
            }

            // Supplied acceleration constrains curvature even when velocity came from poses.
            if (context.Acceleration.HasValue)
            {
                curve.Quadratic = context.Acceleration.Value * (0.5d * curve.Span * curve.Span);
            }

            context.StorePrediction(typeof(SplineExtrapolation), 0, 0, new LinearMotion { HasVelocity = true, Curve = curve });
            return curve.Evaluate(seconds);
        }

        /// <summary>Continues a cubic quaternion-log curve without interpolating Euler angles.</summary>
        /// <param name="context">Retained orientations, their axis mapping and time-aligned body or world angular velocity.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the orientation, already bounded by the prediction cap.</param>
        /// <returns>A quaternion offset for the existing body/world composition pipeline; identity without motion or with supplied zero rate.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or resulting angular displacement is invalid.</exception>
        Quaternion IPredictionModel.Rotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated
                && context.AngularVelocity.Value == default(Double3))
            {
                return Quaternion.identity;
            }

            bool supplied = context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated;
            if (context.TryPrediction(typeof(SplineExtrapolation), 1, 0, out Curve prepared))
            {
                return supplied ? IntegrateAngular(prepared, seconds, context.AngularVelocityIsWorld)
                    : SpatialMath.AngularDisplacement(prepared.Evaluate(seconds), 1d);
            }

            Curve curve = new Curve { Span = 1d, Linear = context.AngularVelocity.GetValueOrDefault() };
            try
            {
                bool fitted = supplied ? TryRateCurve(context, true, out Curve prediction)
                    : TryCurve(context, true, out prediction);
                if (fitted)
                {
                    curve = prediction;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Preserve constant-rate prediction when a history fit cannot be represented.
            }

            context.StorePrediction(typeof(SplineExtrapolation), 1, 0, curve);
            return supplied ? IntegrateAngular(curve, seconds, context.AngularVelocityIsWorld)
                : SpatialMath.AngularDisplacement(curve.Evaluate(seconds), 1d);
        }

        Double3 IPredictionModel.Velocity(ExtrapolationContext context, double seconds)
        {
            if (!context.TryPrediction(typeof(SplineExtrapolation), 0, 0, out LinearMotion motion))
            {
                ((IPredictionModel)this).Displacement(context, 0);
                context.TryPrediction(typeof(SplineExtrapolation), 0, 0, out motion);
            }

            return motion.HasVelocity ? motion.Curve.Velocity(seconds) : default(Double3);
        }

        Double3 IPredictionModel.AngularVelocity(ExtrapolationContext context, double seconds)
        {
            if (context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated
                && context.AngularVelocity.Value == default(Double3))
            {
                return default(Double3);
            }

            if (!context.TryPrediction(typeof(SplineExtrapolation), 1, 0, out Curve curve))
            {
                ((IPredictionModel)this).Rotation(context, 0);
                context.TryPrediction(typeof(SplineExtrapolation), 1, 0, out curve);
            }

            Double3 derivative = curve.Velocity(seconds);
            if (context.AngularVelocity.HasValue && !context.AngularVelocityIsEstimated)
            {
                return derivative;
            }

            // The derivative of a quaternion-log curve needs the exponential's Jacobian.
            // Body composition uses the right Jacobian; world composition uses the left.
            Double3 arc = curve.Evaluate(seconds);
            double angle = Math.Sqrt(arc.X * arc.X + arc.Y * arc.Y + arc.Z * arc.Z);
            double squared = angle * angle;
            double first = angle < 0.01d ? 0.5d - squared / 24d : (1d - Math.Cos(angle)) / squared;
            double second = angle < 0.01d ? 1d / 6d - squared / 120d : (angle - Math.Sin(angle)) / (squared * angle);
            Double3 cross = Cross(arc, derivative);
            return derivative + cross * (context.AngularVelocityIsWorld ? first : -first) + Cross(arc, cross) * second;
        }

        private static Double3 Cross(Double3 a, Double3 b)
        {
            return new Double3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        private static bool TryCurve(in ExtrapolationContext context, bool angular, out Curve curve)
        {
            curve = default(Curve);
            if (!context.ObservationTime.HasValue)
            {
                return false;
            }

            Timestamp newest;
            Double3 anchor = default(Double3);
            Quaternion rotation = Quaternion.identity;
            bool available = angular ? context.TryGetRotationSample(0, out newest, out rotation)
                : context.TryGetPositionSample(0, out newest, out anchor);
            if (!available || !newest.Equals(context.ObservationTime.Value))
            {
                return false;
            }

            Fit fit = default(Fit);
            Double3 previous = default(Double3);
            double span = 0;
            for (int index = 1; index < MaximumSamples; index++)
            {
                Timestamp time;
                Double3 offset;
                if (angular)
                {
                    if (!context.TryGetRotationSample(index, out time, out Quaternion sample))
                    {
                        break;
                    }

                    offset = RotationVector(context, rotation, sample);
                    if (Double3.Distance(previous, offset) > Math.PI)
                    {
                        // Do not fit across a log-map branch jump. The shorter retained
                        // history still supplies a lower-order curve without a false turn.
                        break;
                    }
                }
                else
                {
                    if (!context.TryGetPositionSample(index, out time, out Double3 sample))
                    {
                        break;
                    }

                    offset = sample - anchor;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && fit.Count >= 3)
                {
                    break;
                }

                if (age - span < MinimumInterval)
                {
                    continue;
                }

                fit.Add(-age, offset);
                span = age;
                previous = offset;
            }

            if (fit.Count == 0)
            {
                return false;
            }

            curve = fit.Solve(span, true);
            return true;
        }

        private static bool TryRateCurve(in ExtrapolationContext context, bool angular, out Curve curve)
        {
            curve = default(Curve);
            if (!context.ObservationTime.HasValue || !ReadRate(context, angular, 0, out Timestamp newest, out Double3 anchor))
            {
                return false;
            }

            Fit fit = default(Fit);
            double span = 0;
            for (int index = 1; index < MaximumSamples; index++)
            {
                if (!ReadRate(context, angular, index, out Timestamp time, out Double3 sample))
                {
                    break;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && fit.Count >= 2)
                {
                    break;
                }

                if (age - span < MinimumInterval)
                {
                    continue;
                }

                fit.Add(-age, sample - anchor);
                span = age;
            }

            if (fit.Count == 0)
            {
                return false;
            }

            // A quadratic velocity curve integrates to cubic displacement. Prefer SDK
            // rate history over pose curvature so measurement noise cannot invent motion
            // that contradicts a steady supplied velocity.
            Curve rates = fit.Solve(span, false);
            if (angular && context.AngularVelocityIsWorld && context.RotationSpace != RotationSpace.EarthCentered)
            {
                GeographicBasis basis = context.GeographicBasis;
                rates.Linear = basis.ToLocal(rates.Linear) * -1d;
                rates.Quadratic = basis.ToLocal(rates.Quadratic) * -1d;
            }

            double offset = context.ObservationTime.Value.ElapsedSince(newest) / span;
            Double3 derivative = angular ? context.AngularVelocity.Value : context.Velocity.Value;
            Timestamp? latest = angular ? context.LatestAngularVelocityTime : context.LatestVelocityTime;
            if (offset > 0 && latest.HasValue && latest.Value.CompareTo(context.ObservationTime.Value) <= 0
                && (angular || !context.Acceleration.HasValue))
            {
                // A held rate may predate this pose. Move it to the pose's timestamp using
                // the fitted curve. Prepared linear velocity already includes supplied
                // acceleration, and an interpolated SDK value must not be advanced twice.
                derivative += rates.Evaluate(context.ObservationTime.Value.ElapsedSince(newest));
            }

            curve = new Curve
            {
                Span = span,
                Linear = derivative * span,
                Quadratic = (rates.Linear + rates.Quadratic * (2d * offset)) * (0.5d * span),
                Cubic = rates.Quadratic * (span / 3d)
            };
            return true;
        }

        private static bool ReadRate(in ExtrapolationContext context, bool angular, int lookback, out Timestamp time, out Double3 value)
        {
            return angular ? context.TryGetAngularVelocitySample(lookback, out time, out value)
                : context.TryGetVelocitySample(lookback, out time, out value);
        }

        private static Quaternion IntegrateAngular(Curve curve, double seconds, bool world)
        {
            // Collinear angular vectors commute. Integrate ordinary bank/pitch/yaw
            // curves exactly, using bounded midpoint steps only for changing axes.
            if (Parallel(curve.Linear, curve.Quadratic) && Parallel(curve.Linear, curve.Cubic)
                && Parallel(curve.Quadratic, curve.Cubic))
            {
                return SpatialMath.AngularDisplacement(curve.Evaluate(seconds), 1d);
            }

            int steps = (int)Math.Min(64d, Math.Max(1d, Math.Ceiling(seconds * 120d)));
            double step = seconds / steps;
            Quaternion rotation = Quaternion.identity;
            for (int index = 0; index < steps; index++)
            {
                Quaternion increment = SpatialMath.AngularDisplacement(curve.Velocity((index + 0.5d) * step), step);
                rotation = world ? increment * rotation : rotation * increment;
            }

            return SpatialMath.NormalizeRotation(rotation, nameof(curve));
        }

        private static Double3 RotationVector(in ExtrapolationContext context, Quaternion newest, Quaternion sample)
        {
            Quaternion relative = context.AngularVelocityIsWorld
                ? sample * Quaternion.Inverse(newest) : Quaternion.Inverse(newest) * sample;
            Double3 vector = SpatialMath.RotationRate(Quaternion.identity, relative, 1d);
            if (context.RotationSpace == RotationSpace.Source)
            {
                return context.SourceCoordinates.ToUnityAngularVelocity(vector);
            }

            return context.RotationSpace == RotationSpace.EarthCentered && !context.AngularVelocityIsWorld
                ? context.EarthCenteredBodyAxes.Value.ToUnityAngularVelocity(vector) : vector;
        }

        private static bool Parallel(Double3 left, Double3 right)
        {
            double leftScale = Math.Max(Math.Abs(left.X), Math.Max(Math.Abs(left.Y), Math.Abs(left.Z)));
            double rightScale = Math.Max(Math.Abs(right.X), Math.Max(Math.Abs(right.Y), Math.Abs(right.Z)));
            if (leftScale == 0 || rightScale == 0)
            {
                return true;
            }

            // Normalize before comparing directions so coefficient size cannot overflow
            // the cross product or turn harmless fitting roundoff into many integration steps.
            left *= 1d / leftScale;
            right *= 1d / rightScale;
            double x = left.Y * right.Z - left.Z * right.Y;
            double y = left.Z * right.X - left.X * right.Z;
            double z = left.X * right.Y - left.Y * right.X;
            return x * x + y * y + z * z <= 1e-12d;
        }
    }
}
