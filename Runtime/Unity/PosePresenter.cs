using System;
using UnityEngine;

namespace Emas
{
    // Builds the displayed source pose without feeding filtered values back into observations.
    internal sealed class PosePresenter
    {
        private readonly PresentationTuning _tuning = new PresentationTuning();
        private Double3 _position;
        private Double3 _observation;
        private Quaternion _rotation = Quaternion.identity;
        private bool _hasPose;
        private PresentationPose _filtered;
        private PresentationPose _target;
        private Double3 _velocity;
        private Double3 _angularVelocity;
        private double? _timestamp;
        private Timestamp? _positionTime;
        private Timestamp? _rotationTime;
        private double _positionReceivedTime;
        private double _rotationReceivedTime;
        private double _positionOffset;
        private double _rotationOffset;
        private bool _estimatedRotationMotion;

        internal Double3 Position => _position;
        internal Double3 Observation => _observation;
        internal Quaternion Rotation => _rotation;
        internal bool HasPose => _hasPose;

        internal void Prepare(Spatial spatial, SpatialClock clock, double timestamp, float interpolationDelay, CoordinateSystem sourceAxes)
        {
            Extrapolation extrapolation = spatial.GetComponent<Extrapolation>();
            Smoothing smoothing = spatial.GetComponent<Smoothing>();
            PresentationSettings settings = new PresentationSettings(extrapolation, smoothing, interpolationDelay);
            MotionProcessor processor = extrapolation != null || (smoothing != null && smoothing.enabled && spatial.HasMotionData)
                ? spatial.Processor : null;
            Double3 motion = default(Double3);
            Quaternion rotationMotion = Quaternion.identity;
            bool worldRotationMotion = true;
            bool estimatedRotationMotion = false;
            bool assistAngular = smoothing != null && settings.RotationHalfLife > 0
                && settings.PacketSmoothingDuration <= 0 && (settings.EstimateAngularMotion || spatial.HasAngularVelocity);
            Prediction prediction;
            if (processor != null)
            {
                processor.Prepare(spatial, timestamp);
                prediction = processor.Predict(spatial, clock, timestamp, sourceAxes, settings, assistAngular,
                    out motion, out rotationMotion, out worldRotationMotion, out estimatedRotationMotion, out _observation);
            }
            else
            {
                // Buffered playback also works without any motion-processing components.
                Double3 position = spatial.Position;
                Quaternion rotation = spatial.Rotation;
                double target = clock.PredictionTimestamp(timestamp);
                if (spatial.PositionTime.HasValue && (interpolationDelay > 0 || clock.HasManualPrediction))
                {
                    spatial.History.SamplePosition(clock.OffsetFrom(spatial.PositionTime.Value, target, interpolationDelay), out position);
                }

                if (spatial.RotationTime.HasValue)
                {
                    double offset = interpolationDelay > 0 || clock.HasManualPrediction
                        ? clock.OffsetFrom(spatial.RotationTime.Value, target, interpolationDelay)
                        : clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, target);
                    spatial.History.SampleRotation(offset, out rotation);
                }

                _observation = position;
                prediction = new Prediction(position, rotation);
            }

            PresentationPose pose = new PresentationPose(prediction.Position, prediction.Rotation);
            if (processor != null)
            {
                // Packet correction and half-life smoothing consume the same full prediction.
                pose = processor.SmoothPacket(spatial, pose, prediction.Velocity, prediction.AngularVelocity,
                    timestamp, settings.PacketSmoothingDuration, false, out Double3 packetMotion, out Quaternion packetRotationMotion);
                if (settings.PacketSmoothingDuration > 0)
                {
                    motion = packetMotion;
                    rotationMotion = packetRotationMotion;
                    worldRotationMotion = true;
                }
            }

            PresentationPose previous = _filtered;
            double elapsed = _timestamp.HasValue ? Math.Max(0d, timestamp - _timestamp.Value) : 0;
            if (_tuning.Changed(settings))
            {
                // Advance the cached target along its previous timeline. New packets advance
                // a capped timeline too, while a held packet at its limit contributes no motion.
                try
                {
                    double positionStep = TargetStep(spatial.PositionTime, _positionTime, spatial.PositionReceivedTime,
                        _positionReceivedTime, _positionOffset, elapsed, _tuning.Settings.MaximumExtrapolation);
                    double rotationStep = TargetStep(spatial.RotationTime, _rotationTime, spatial.RotationReceivedTime,
                        _rotationReceivedTime, _rotationOffset, elapsed, _tuning.Settings.MaximumExtrapolation);
                    Double3 previousMotion = _velocity * positionStep;
                    Quaternion previousRotationMotion = SpatialMath.AngularDisplacement(_angularVelocity, rotationStep);
                    previous = new PresentationPose(_target.Position + previousMotion, previousRotationMotion * _target.Rotation);
                    // Preview only the old filter on a copy. No model, history or packet-correction evaluation is repeated.
                    if (smoothing != null)
                    {
                        previous = smoothing.Prepare(spatial, previous, timestamp, previousMotion, previousRotationMotion,
                            true, _estimatedRotationMotion, _tuning.Settings, true);
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    previous = _filtered;
                }
            }

            PresentationPose current = smoothing != null ? smoothing.Prepare(spatial, pose, timestamp, motion, rotationMotion,
                worldRotationMotion, estimatedRotationMotion, settings, false) : pose;
            _target = pose;
            _filtered = current;
            _velocity = processor != null ? processor.Trajectory.Velocity : prediction.Velocity;
            _angularVelocity = processor != null ? processor.Trajectory.AngularVelocity : prediction.AngularVelocity;
            _positionOffset = processor != null ? processor.PositionOffset : 0;
            _rotationOffset = processor != null ? processor.RotationOffset : 0;
            _positionTime = spatial.PositionTime;
            _rotationTime = spatial.RotationTime;
            _positionReceivedTime = spatial.PositionReceivedTime;
            _rotationReceivedTime = spatial.RotationReceivedTime;
            _estimatedRotationMotion = estimatedRotationMotion;
            _timestamp = timestamp;
            current = _tuning.Apply(previous, current, settings, timestamp);
            _position = current.Position;
            _rotation = current.Rotation;
            _hasPose = true;
        }

        internal void Observe(Spatial spatial)
        {
            _position = spatial.Position;
            _observation = spatial.Position;
            _rotation = spatial.Rotation;
            _hasPose = true;
        }

        internal void Reset()
        {
            _hasPose = false;
            ResetTuning();
        }

        internal void ResetTuning()
        {
            _tuning.Reset();
            _timestamp = null;
            _velocity = default(Double3);
            _angularVelocity = default(Double3);
        }

        internal void ResetRotation()
        {
            _tuning.ResetRotation();
            _angularVelocity = default(Double3);
        }

        private static double TargetStep(Timestamp? sampleTime, Timestamp? previousSampleTime,
            double receivedTime, double previousReceivedTime, double offset, double elapsed, double limit)
        {
            double sourceAdvance = sampleTime.HasValue && previousSampleTime.HasValue
                ? sampleTime.Value.ElapsedSince(previousSampleTime.Value) : receivedTime - previousReceivedTime;
            return sourceAdvance + Math.Min(offset + elapsed - sourceAdvance, limit) - Math.Min(offset, limit);
        }
    }
}
