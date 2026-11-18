using System;
using UnityEngine;

namespace Emas
{
    // Keep packet corrections separate from the prediction strategy and raw SDK samples.
    // Use a Hermite curve for position error and a spherical Bezier curve for rotation error.
    internal struct PacketSmoother
    {
        private Double3 _position;
        private Double3 _targetPosition;
        private Double3 _targetVelocity;
        private Quaternion _targetRotation;
        private Double3 _targetAngularVelocity;
        private Double3 _positionOffset;
        private Double3 _positionTangent;
        private Quaternion _rotationOffset;
        private Quaternion _rotationControl;
        private Double3 _rotationTangent;
        private double _positionStarted;
        private double _rotationStarted;
        private double _timestamp;
        private long _positionVersion;
        private long _rotationVersion;
        private long _motionVersion;
        private bool _hasPosition;
        private bool _hasRotation;

        internal PresentationPose Apply(Spatial spatial, PresentationPose target, Double3 velocity,
            Double3 angularVelocity, long motionVersion, double timestamp, float duration, out Double3 motion)
        {
            motion = default(Double3);
            if (duration <= 0)
            {
                this = default(PacketSmoother);
                return target;
            }

            // Keep the valid pose in case the correction calculation overflows.
            PresentationPose baseline = target;
            try
            {
                double elapsed = Math.Max(0d, timestamp - _timestamp);
                bool motionChanged = motionVersion != _motionVersion;
                // Give Smoothing the movement after packet correction so it does not add movement twice.
                if (spatial.HasPosition)
                {
                    bool hadPosition = _hasPosition;
                    Double3 previous = _position;
                    target.Position = Position(target.Position, velocity, spatial.PositionVersion, motionChanged, elapsed, timestamp, duration);
                    if (hadPosition)
                    {
                        motion = target.Position - previous;
                    }
                }

                if (spatial.HasRotation)
                {
                    target.Rotation = Rotation(target.Rotation, angularVelocity, spatial.RotationVersion, motionChanged, elapsed, timestamp, duration);
                }

                // Save the packet versions only after position and rotation have both been handled.
                _timestamp = timestamp;
                _positionVersion = spatial.PositionVersion;
                _rotationVersion = spatial.RotationVersion;
                _motionVersion = motionVersion;
                return target;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Very large SDK values can overflow correction calculations. Fall back to the pose without correction.
                this = default(PacketSmoother);
                motion = default(Double3);
                return baseline;
            }
        }

        internal void ResetRotation()
        {
            _hasRotation = false;
        }

        private Double3 Position(Double3 target, Double3 velocity, long version, bool motionChanged,
            double elapsed, double timestamp, double duration)
        {
            if (!_hasPosition)
            {
                _position = target;
                _targetPosition = target;
                _targetVelocity = velocity;
                _positionOffset = default(Double3);
                _positionTangent = default(Double3);
                _positionStarted = timestamp;
                _hasPosition = true;
                return target;
            }

            Double3 offset = PositionCorrection(timestamp, duration, out Double3 tangent);
            if (version != _positionVersion || motionChanged)
            {
                // Update the old correction curve to the current time before starting the new correction.
                // Otherwise, frequent packets could keep restarting the curve and preserve movement that should have stopped.
                _positionOffset = _targetPosition + _targetVelocity * elapsed + offset - target;
                _positionTangent = _targetVelocity + tangent - velocity;
                _positionStarted = timestamp;
                offset = _positionOffset;
            }

            _targetPosition = target;
            _targetVelocity = velocity;
            _position = target + offset;
            return _position;
        }

        private Double3 PositionCorrection(double timestamp, double duration, out Double3 tangent)
        {
            // The Hermite curve ends with zero position correction and zero correction velocity.
            double u = Math.Min(1d, Math.Max(0d, (timestamp - _positionStarted) / duration));
            double squared = u * u;
            double h00 = 2d * squared * u - 3d * squared + 1d;
            double h10 = squared * u - 2d * squared + u;
            tangent = _positionOffset * ((6d * squared - 6d * u) / duration)
                + _positionTangent * (3d * squared - 4d * u + 1d);
            return _positionOffset * h00 + _positionTangent * (duration * h10);
        }

        private Quaternion Rotation(Quaternion target, Double3 angularVelocity, long version, bool motionChanged,
            double elapsed, double timestamp, double duration)
        {
            if (!_hasRotation)
            {
                _targetRotation = target;
                _targetAngularVelocity = angularVelocity;
                _rotationOffset = Quaternion.identity;
                _rotationControl = Quaternion.identity;
                _rotationTangent = default(Double3);
                _rotationStarted = timestamp;
                _hasRotation = true;
                return target;
            }

            double u = Math.Min(1d, Math.Max(0d, (timestamp - _rotationStarted) / duration));
            Quaternion correction = Curve(u);
            if (version != _rotationVersion || motionChanged)
            {
                Quaternion continuedTarget = SpatialMath.AngularDisplacement(_targetAngularVelocity, elapsed) * _targetRotation;
                Quaternion continued = correction * continuedTarget;
                Double3 continuedRate = RotationCorrectionRate(u, duration) + SpatialMath.Rotate(correction, _targetAngularVelocity);
                _rotationOffset = SpatialMath.NormalizeRotation(continued * Quaternion.Inverse(target), nameof(target));
                // For Q = E*T, world angular velocity is omega_E + E*omega_T. Evaluate the old
                // correction's current rotation rate so frequent packets still let its error fade away.
                _rotationTangent = continuedRate - SpatialMath.Rotate(_rotationOffset, angularVelocity);
                _rotationControl = SpatialMath.AngularDisplacement(_rotationTangent, duration / 3d) * _rotationOffset;
                _rotationStarted = timestamp;
                correction = _rotationOffset;
            }

            _targetRotation = target;
            _targetAngularVelocity = angularVelocity;
            return SpatialMath.NormalizeRotation(correction * target, nameof(target));
        }

        private Double3 RotationCorrectionRate(double u, double duration)
        {
            // Use the exact rotation rate at each end. Estimate the rate only inside the curve.
            if (u <= 0)
            {
                return _rotationTangent;
            }
            else if (u < 1)
            {
                double before = Math.Max(0d, u - 0.001d);
                double after = Math.Min(1d, u + 0.001d);
                return SpatialMath.RotationRate(Curve(before), Curve(after), (after - before) * duration);
            }

            return default(Double3);
        }

        private Quaternion Curve(double u)
        {
            // Spherical de Casteljau blends rotations on the sphere and handles q and -q as the same rotation.
            // The last two control rotations are identity, so rotation error and its rate end at zero.
            float amount = (float)u;
            Quaternion first = Quaternion.Slerp(_rotationOffset, _rotationControl, amount);
            Quaternion second = Quaternion.Slerp(_rotationControl, Quaternion.identity, amount);
            Quaternion left = Quaternion.Slerp(first, second, amount);
            Quaternion right = Quaternion.Slerp(second, Quaternion.identity, amount);
            return Quaternion.Slerp(left, right, amount);
        }
    }
}
