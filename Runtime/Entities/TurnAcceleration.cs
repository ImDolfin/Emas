using System;
using UnityEngine;

namespace Emas
{
    // Fit acceleration in rotating axes. World acceleration already contains centripetal
    // motion, so adding a world-space second difference to a turn would count it twice.
    internal static class TurnAcceleration
    {
        private struct PositionFit
        {
            internal int Count;
            internal Double3 Axis;
            internal double Magnitude;
            private double _axialAA;
            private double _axialAB;
            private double _axialBB;
            private double _planeAA;
            private double _planeRealAB;
            private double _planeImaginaryAB;
            private double _planeBB;
            private Double3 _rightA;
            private Double3 _rightB;

            internal void Add(double seconds, Double3 displacement)
            {
                // A and B integrate exp(omega*t) and t*exp(omega*t). Along the turn
                // axis they are real scalars; in its perpendicular plane they act
                // like complex numbers. This avoids allocating a general 6x6 solver.
                CoordinatedTurnExtrapolation.Factors(Magnitude * seconds, out double f1, out double f2, out double f3, out double f4);
                double a0 = seconds;
                double b0 = 0.5d * seconds * seconds;
                double ar = seconds * (1d - f2);
                double ai = seconds * f1;
                double br = seconds * seconds * (0.5d - f4);
                double bi = seconds * seconds * f3;
                Double3 parallel = Project(Axis, displacement);
                Double3 plane = displacement - parallel;
                Double3 rotated = Cross(Axis, plane);
                _axialAA += a0 * a0;
                _axialAB += a0 * b0;
                _axialBB += b0 * b0;
                _planeAA += ar * ar + ai * ai;
                _planeRealAB += ar * br + ai * bi;
                _planeImaginaryAB += ar * bi - ai * br;
                _planeBB += br * br + bi * bi;
                _rightA += parallel * a0 + plane * ar - rotated * ai;
                _rightB += parallel * b0 + plane * br - rotated * bi;
                Count++;
            }

            internal bool Solve(Double3? knownVelocity, out Double3 velocity, out Double3 acceleration)
            {
                velocity = knownVelocity.GetValueOrDefault();
                acceleration = default(Double3);
                Double3 axialA = Project(Axis, _rightA);
                Double3 axialB = Project(Axis, _rightB);
                Double3 planeA = _rightA - axialA;
                Double3 planeB = _rightB - axialB;
                if (knownVelocity.HasValue)
                {
                    if (Count < 1 || _axialBB == 0 || _planeBB == 0)
                    {
                        return false;
                    }

                    // An SDK velocity at the pose time is fixed. Only the unknown body
                    // acceleration is fitted, preserving supplied motion including zero.
                    Double3 axialVelocity = Project(Axis, velocity);
                    Double3 planeVelocity = velocity - axialVelocity;
                    acceleration = (axialB - axialVelocity * _axialAB) * (1d / _axialBB)
                        + (planeB - planeVelocity * _planeRealAB + Cross(Axis, planeVelocity) * _planeImaginaryAB) * (1d / _planeBB);
                    return true;
                }

                double axialDeterminant = _axialAA * _axialBB - _axialAB * _axialAB;
                double planeDeterminant = _planeAA * _planeBB
                    - _planeRealAB * _planeRealAB - _planeImaginaryAB * _planeImaginaryAB;
                if (Count < 2 || axialDeterminant <= 1e-10d * _axialAA * _axialBB
                    || planeDeterminant <= 1e-10d * _planeAA * _planeBB)
                {
                    return false;
                }

                // Solve the real and Hermitian two-coefficient normal equations. A
                // nearly singular history cannot determine both velocity and acceleration.
                Double3 axialAcceleration = (axialB * _axialAA - axialA * _axialAB) * (1d / axialDeterminant);
                Double3 planeAcceleration = (planeB * _planeAA - planeA * _planeRealAB
                    + Cross(Axis, planeA) * _planeImaginaryAB) * (1d / planeDeterminant);
                velocity = (axialA - axialAcceleration * _axialAB) * (1d / _axialAA)
                    + (planeA - planeAcceleration * _planeRealAB - Cross(Axis, planeAcceleration) * _planeImaginaryAB) * (1d / _planeAA);
                acceleration = axialAcceleration + planeAcceleration;
                return true;
            }
        }

        private const int MaximumSamples = 8;
        private const double WindowSeconds = 0.25d;
        private const double MinimumInterval = 0.0001d;

        internal static bool TryEstimate(in ExtrapolationContext context, in GeographicBasis basis,
            Quaternion attitude, Double3 rate, ref Double3 velocity, out Double3 acceleration)
        {
            acceleration = default(Double3);
            if (!context.ObservationTime.HasValue)
            {
                return false;
            }

            try
            {
                bool supplied = context.Velocity.HasValue && !context.VelocityIsEstimated;
                if (supplied && FitVelocities(context, basis, attitude, rate, velocity, out Double3 fittedVelocity, out Double3 fittedAcceleration))
                {
                    // Keep a current/interpolated SDK velocity fixed. Advance an older
                    // held velocity to the pose time through the fitted body motion.
                    if (context.LatestVelocityTime.HasValue && context.LatestVelocityTime.Value.CompareTo(context.ObservationTime.Value) < 0)
                    {
                        velocity = fittedVelocity;
                    }

                    acceleration = fittedAcceleration;
                    return true;
                }

                bool currentVelocity = supplied && (!context.LatestVelocityTime.HasValue
                    || context.LatestVelocityTime.Value.CompareTo(context.ObservationTime.Value) >= 0);
                if (FitPositions(context, basis, attitude, rate, currentVelocity ? velocity : (Double3?)null,
                    out fittedVelocity, out fittedAcceleration))
                {
                    velocity = fittedVelocity;
                    acceleration = fittedAcceleration;
                    return true;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // An unrepresentable fit must retain valid prepared SDK motion and the
                // ordinary turn fallback instead of contaminating the presented pose.
            }

            return false;
        }

        private static bool FitVelocities(in ExtrapolationContext context, in GeographicBasis basis,
            Quaternion attitude, Double3 rate, Double3 anchor, out Double3 velocity, out Double3 acceleration)
        {
            velocity = default(Double3);
            acceleration = default(Double3);
            if (!context.TryGetVelocitySample(0, out Timestamp newest, out _))
            {
                return false;
            }

            Quaternion inverse = Quaternion.Inverse(attitude);
            double magnitude = Length(rate);
            Double3 axis = magnitude == 0 ? new Double3(0, 0, 1) : rate * (1d / magnitude);
            int count = 0;
            double sumTime = 0;
            double sumSquaredTime = 0;
            Double3 sum = default(Double3);
            Double3 sumTimeValue = default(Double3);
            double previousAge = -MinimumInterval;
            for (int index = 0; index < MaximumSamples; index++)
            {
                if (!context.TryGetVelocitySample(index, out Timestamp time, out Double3 sample))
                {
                    break;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && count >= 2)
                {
                    break;
                }

                if (age - previousAge < MinimumInterval)
                {
                    continue;
                }

                double seconds = time.ElapsedSince(context.ObservationTime.Value);
                Double3 local = CoordinatedTurnExtrapolation.ToLocal(context, basis, sample);
                Double3 body = SpatialMath.Rotate(inverse, local);
                // Bring each sampled velocity into the body's rotating coordinates at
                // its own timestamp. A steady turn then has zero body acceleration.
                Double3 value = Rotate(axis, -magnitude * seconds, body) - anchor;
                sum += value;
                sumTimeValue += value * seconds;
                sumTime += seconds;
                sumSquaredTime += seconds * seconds;
                previousAge = age;
                count++;
            }

            double denominator = count > 0 ? sumSquaredTime - sumTime * sumTime / count : 0;
            if (count < 2 || denominator <= 1e-10d * sumSquaredTime)
            {
                return false;
            }

            acceleration = (sumTimeValue - sum * (sumTime / count)) * (1d / denominator);
            velocity = anchor + (sum - acceleration * sumTime) * (1d / count);
            return true;
        }

        private static bool FitPositions(in ExtrapolationContext context, in GeographicBasis basis,
            Quaternion attitude, Double3 rate, Double3? knownVelocity, out Double3 velocity, out Double3 acceleration)
        {
            velocity = default(Double3);
            acceleration = default(Double3);
            if (!context.TryGetPositionSample(0, out Timestamp newest, out Double3 position)
                || !newest.Equals(context.ObservationTime.Value))
            {
                return false;
            }

            double magnitude = Length(rate);
            PositionFit fit = new PositionFit
            {
                Axis = magnitude == 0 ? new Double3(0, 0, 1) : rate * (1d / magnitude), Magnitude = magnitude
            };
            Quaternion inverse = Quaternion.Inverse(attitude);
            double previousAge = 0;
            for (int index = 1; index < MaximumSamples; index++)
            {
                if (!context.TryGetPositionSample(index, out Timestamp time, out Double3 older))
                {
                    break;
                }

                double age = newest.ElapsedSince(time);
                if (age > WindowSeconds && fit.Count >= (knownVelocity.HasValue ? 1 : 2))
                {
                    break;
                }

                if (age - previousAge < MinimumInterval)
                {
                    continue;
                }

                Double3 delta = CoordinatedTurnExtrapolation.ToLocal(context, basis, older - position);
                fit.Add(-age, SpatialMath.Rotate(inverse, delta));
                previousAge = age;
            }

            return fit.Solve(knownVelocity, out velocity, out acceleration);
        }

        private static Double3 Project(Double3 axis, Double3 vector)
        {
            return axis * (axis.X * vector.X + axis.Y * vector.Y + axis.Z * vector.Z);
        }

        private static Double3 Rotate(Double3 axis, double angle, Double3 vector)
        {
            Double3 cross = Cross(axis, vector);
            return vector + cross * Math.Sin(angle) + Cross(axis, cross) * (1d - Math.Cos(angle));
        }

        private static double Length(Double3 vector)
        {
            return Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
        }

        private static Double3 Cross(Double3 left, Double3 right)
        {
            return new Double3(left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);
        }
    }
}
