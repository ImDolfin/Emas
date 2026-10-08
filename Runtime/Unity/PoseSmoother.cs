using System;
using UnityEngine;

namespace Emas
{
    // Owns source pose channels and independent smoothing histories before reference projection.
    internal sealed class PoseSmoother
    {
        private Double3 _smoothedPosition;
        private Quaternion _smoothedRotation = Quaternion.identity;
        private bool _hasSmoothedPosition;
        private bool _hasSmoothedRotation;
        private double _positionTimestamp;
        private double _smoothingTimestamp;

        internal Double3 Position { get; private set; }
        internal Quaternion Rotation { get; private set; } = Quaternion.identity;
        internal Double3 Velocity { get; private set; }
        internal Double3 Acceleration { get; private set; }
        internal bool HasPosition { get; private set; }
        internal bool HasRotation { get; private set; }
        internal bool HasVelocity { get; private set; }
        internal bool HasAcceleration { get; private set; }
        internal Double3 PresentationPosition => _hasSmoothedPosition ? _smoothedPosition : Position;
        internal Quaternion PresentationRotation => _hasSmoothedRotation ? _smoothedRotation : Rotation;

        internal void SetPosition(Double3 position, double timestamp)
        {
            ReferenceFrame.ValidatePosition(position, nameof(position));
            if (!HasPosition || Position != position)
            {
                _positionTimestamp = timestamp;
            }

            Position = position;
            HasPosition = true;
        }

        // Spatial normalizes input and resets history when the attitude representation changes.
        internal void SetRotation(Quaternion rotation)
        {
            Rotation = rotation;
            HasRotation = true;
        }

        internal void SetVelocity(Double3 velocity)
        {
            ReferenceFrame.ValidatePosition(velocity, nameof(velocity));
            Velocity = velocity;
            HasVelocity = true;
        }

        internal void ClearVelocity()
        {
            HasVelocity = false;
            Velocity = default(Double3);
        }

        internal void SetAcceleration(Double3 acceleration)
        {
            ReferenceFrame.ValidatePosition(acceleration, nameof(acceleration));
            Acceleration = acceleration;
            HasAcceleration = true;
        }

        internal void ClearAcceleration()
        {
            HasAcceleration = false;
            Acceleration = default(Double3);
        }

        internal void ResetPosition()
        {
            _hasSmoothedPosition = false;
        }

        internal void ResetRotation()
        {
            _hasSmoothedRotation = false;
        }

        internal void Reset()
        {
            ResetPosition();
            ResetRotation();
        }

        internal void Prepare(float positionSmoothingTime, float rotationSmoothingTime, double timestamp)
        {
            double elapsed = Math.Max(0d, timestamp - _smoothingTimestamp);
            if (HasPosition && SpatialMath.IsFinite(positionSmoothingTime) && positionSmoothingTime > 0)
            {
                double weight = 1d - Math.Exp(-elapsed / positionSmoothingTime);
                _smoothedPosition = _hasSmoothedPosition
                    ? SmoothPosition(positionSmoothingTime, weight, elapsed, timestamp) : Position;
                _hasSmoothedPosition = true;
            }
            else
            {
                ResetPosition();
            }

            if (HasRotation && SpatialMath.IsFinite(rotationSmoothingTime) && rotationSmoothingTime > 0)
            {
                double weight = 1d - Math.Exp(-elapsed / rotationSmoothingTime);
                _smoothedRotation = _hasSmoothedRotation
                    ? Quaternion.Slerp(_smoothedRotation, Rotation, (float)weight) : Rotation;
                _hasSmoothedRotation = true;
            }
            else
            {
                ResetRotation();
            }

            _smoothingTimestamp = timestamp;
        }

        private Double3 SmoothPosition(float smoothingTime, double weight, double elapsed, double timestamp)
        {
            // Integrate supplied motion, then blend its error against the latest observation in every direction.
            // Cap prediction age so a stopped SDK cannot drive the presentation indefinitely.
            double age = Math.Max(0d, timestamp - _positionTimestamp);
            double predictionAge = HasVelocity ? Math.Min(age, smoothingTime) : 0d;
            double step = HasVelocity
                ? Math.Min(elapsed, Math.Max(0d, smoothingTime - Math.Max(0d, age - elapsed))) : 0d;
            double accelerationAge = HasAcceleration ? 0.5d * predictionAge * predictionAge : 0d;
            // A fresh packet's velocity is at its observation time; integrate acceleration backward
            // over the preceding part of this step, then forward over any packet age.
            double accelerationStep = HasAcceleration
                ? step * (predictionAge - 0.5d * step) : 0d;
            double x = SmoothCoordinate(Position.X, _smoothedPosition.X, Velocity.X, Acceleration.X,
                predictionAge, step, accelerationAge, accelerationStep, weight);
            double y = SmoothCoordinate(Position.Y, _smoothedPosition.Y, Velocity.Y, Acceleration.Y,
                predictionAge, step, accelerationAge, accelerationStep, weight);
            double z = SmoothCoordinate(Position.Z, _smoothedPosition.Z, Velocity.Z, Acceleration.Z,
                predictionAge, step, accelerationAge, accelerationStep, weight);
            return SpatialMath.IsFinite(x) && SpatialMath.IsFinite(y) && SpatialMath.IsFinite(z)
                ? new Double3(x, y, z) : Position;
        }

        private static double SmoothCoordinate(double position, double smoothed, double velocity, double acceleration,
            double age, double step, double accelerationAge, double accelerationStep, double weight)
        {
            double predicted = smoothed + velocity * step + acceleration * accelerationStep;
            double target = position + velocity * age + acceleration * accelerationAge;
            double correction = target - predicted;
            if (!SpatialMath.IsFinite(correction))
            {
                return double.NaN;
            }

            return predicted + correction * weight;
        }
    }
}
