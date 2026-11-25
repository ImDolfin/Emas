using System;
using UnityEngine;

namespace Emas
{
    // Builds the displayed source pose. Keeps playback and filters separate from raw input and Unity transforms.
    internal sealed class PosePresenter
    {
        private readonly PresentationTuning _tuning = new PresentationTuning();
        private Double3 _position;
        private Double3 _observation;
        private Quaternion _rotation = Quaternion.identity;
        private bool _hasPose;

        internal Double3 Position => _position;

        internal Double3 Observation => _observation;

        internal Quaternion Rotation => _rotation;

        internal bool HasPose => _hasPose;

        internal void Prepare(Spatial spatial, SpatialClock clock, double timestamp, float interpolationDelay, CoordinateSystem sourceAxes)
        {
            // Read the settings and input once before trying any settings changes.
            Extrapolation prediction = spatial.GetComponent<Extrapolation>();
            Smoothing smoothing = spatial.GetComponent<Smoothing>();
            PresentationSettings settings = new PresentationSettings(prediction, smoothing, interpolationDelay);
            MotionProcessor processor = prediction != null || (smoothing != null && smoothing.enabled && spatial.HasMotionData)
                ? spatial.Processor : null;
            if (processor != null)
            {
                processor.Prepare(spatial, timestamp);
            }

            PresentationPose previous = default(PresentationPose);
            // Try the old settings at the same time so the blend covers only the settings change.
            if (_tuning.Changed(settings))
            {
                previous = PreparePose(spatial, clock, timestamp, processor, smoothing, sourceAxes, _tuning.Settings, true, out _);
            }

            // Save the new filter state once. Keep the final settings-change offset out of that state.
            PresentationPose current = PreparePose(spatial, clock, timestamp, processor, smoothing, sourceAxes, settings, false, out _observation);
            current = _tuning.Apply(previous, current, settings, timestamp);
            _position = current.Position;
            _rotation = current.Rotation;
            _hasPose = true;
        }

        internal void Observe(Spatial spatial)
        {
            // Distant roots use accepted input immediately, including when the Realm has buffered playback.
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
        }

        internal void ResetRotation()
        {
            _tuning.ResetRotation();
        }

        private PresentationPose PreparePose(Spatial spatial, SpatialClock clock, double timestamp, MotionProcessor processor,
            Smoothing smoothing, CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 observation)
        {
            // Calculate the source pose within its prediction limit before applying packet correction or Smoothing.
            PresentationPose pose = SamplePose(spatial, clock, timestamp, processor, sourceAxes, settings, preview, out Double3 motion, out observation);
            Quaternion rotationMotion = Quaternion.identity;
            bool worldRotationMotion = false;
            bool estimatedRotationMotion = false;
            if (processor != null)
            {
                if (spatial.HasRotation)
                {
                    double rotationTime = processor.PredictionTimestamp(clock, spatial.RotationTime, spatial.RotationReceivedTime, timestamp, settings.PredictionTime);
                    double offset = settings.InterpolationDelay > 0 && spatial.RotationTime.HasValue
                        ? clock.OffsetFrom(spatial.RotationTime.Value, rotationTime, settings.InterpolationDelay)
                        : clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                    // Track the endpoint even while the filter is off so live edits cannot
                    // apply a large accumulated turn when angular smoothing resumes.
                    bool assistAngular = settings.EstimateAngularMotion || spatial.HasAngularVelocity;
                    rotationMotion = processor.RotationMotion(spatial, offset, settings.MaximumExtrapolation,
                        settings.Strategy, sourceAxes, preview, smoothing != null && settings.RotationHalfLife > 0
                            && settings.PacketSmoothingDuration <= 0 && assistAngular, out worldRotationMotion);
                    if (smoothing != null && settings.RotationHalfLife > 0 && assistAngular)
                    {
                        estimatedRotationMotion = processor.EstimatesAngularMotion(spatial, sourceAxes);
                    }
                }

                Double3 velocity = default(Double3);
                Double3 angularVelocity = default(Double3);
                if (settings.PacketSmoothingDuration > 0)
                {
                    // Estimate velocity by comparing this pose with one slightly later on the same path.
                    // Comparing poses before and after a new packet would halve its starting velocity.
                    // At the prediction limit, both poses are held still, so this correctly gives zero velocity.
                    // Preview calls leave stored motion unchanged.
                    const double step = 0.001d;
                    PresentationPose after = SamplePose(spatial, clock, timestamp + step, processor, sourceAxes, settings, true, out _, out _);
                    try
                    {
                        velocity = (after.Position - pose.Position) * (1d / step);
                        angularVelocity = SpatialMath.RotationRate(pose.Rotation, after.Rotation, step);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // If velocity is too large to represent, keep the valid pose.
                    }
                }

                // Apply packet correction using its own stored arrivals, then apply the optional Smoothing trait.
                pose = processor.SmoothPacket(spatial, pose, velocity, angularVelocity, timestamp, settings.PacketSmoothingDuration, preview,
                    out Double3 packetMotion, out Quaternion packetRotationMotion);
                if (settings.PacketSmoothingDuration > 0)
                {
                    // Keep packet correction and half-life filtering separate. Advance the
                    // filters by the strategy's motion, leaving correction error to decay.
                    motion = packetMotion;
                    rotationMotion = packetRotationMotion;
                    worldRotationMotion = true;
                }
            }

            // Use known movement to help Smoothing follow the pose. Smooth only the remaining position error.
            return smoothing != null ? smoothing.Prepare(spatial, pose, timestamp, motion, rotationMotion,
                worldRotationMotion, estimatedRotationMotion, settings, preview) : pose;
        }

        private PresentationPose SamplePose(Spatial spatial, SpatialClock clock, double timestamp, MotionProcessor processor,
            CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 motion, out Double3 observation)
        {
            motion = default(Double3);
            observation = spatial.Position;
            // Each channel starts its requested forward interval at receipt, counting known sample age once.
            // Filters and tuning use real presentation time; prediction retains its configured cap.
            double positionTime = processor != null
                ? processor.PredictionTimestamp(clock, spatial.PositionTime, spatial.PositionReceivedTime, timestamp, settings.PredictionTime) : clock.PredictionTimestamp(timestamp);
            double rotationTime = processor != null
                ? processor.PredictionTimestamp(clock, spatial.RotationTime, spatial.RotationReceivedTime, timestamp, settings.PredictionTime) : clock.PredictionTimestamp(timestamp);
            PresentationPose pose;
            // Manual targets can precede a peer's newest packet. Sample its history,
            // as buffered playback does, so every entity can use the same target time.
            if (settings.InterpolationDelay > 0 || clock.HasManualPrediction)
            {
                pose = BufferedPose(spatial, clock, positionTime, rotationTime, processor, sourceAxes, settings, preview, out motion, out observation);
            }
            else
            {
                Double3 position = processor != null
                    ? processor.Project(spatial, clock, positionTime, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, preview, out motion) : spatial.Position;
                Quaternion rotation = spatial.Rotation;
                if (spatial.History.NewestRotationTime.HasValue)
                {
                    double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                    spatial.History.SampleRotation(age, out rotation);
                }

                if (processor != null && spatial.HasRotation)
                {
                    double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                    rotation = processor.ProjectRotation(spatial, rotation, age, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
                }

                pose = new PresentationPose(position, rotation);
            }

            return pose;
        }

        private PresentationPose BufferedPose(Spatial spatial, SpatialClock clock, double positionTime, double rotationTime, MotionProcessor processor,
            CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 motion, out Double3 observation)
        {
            Double3 position = spatial.Position;
            Quaternion rotation = spatial.Rotation;
            observation = spatial.Position;
            motion = default(Double3);
            // Position and rotation share the playback clock, but each keeps its own sample times.
            if (spatial.History.NewestPositionTime.HasValue)
            {
                double offset = clock.OffsetFrom(spatial.History.NewestPositionTime.Value, positionTime, settings.InterpolationDelay);
                spatial.History.SamplePosition(offset, out position);
                observation = position;
                if (processor != null)
                {
                    position = processor.ProjectTimestamped(spatial, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, position, offset, preview, out motion);
                }
            }
            else if (processor != null)
            {
                // With no timestamp, use time since arrival because there are no SDK times to interpolate.
                position = processor.Project(spatial, clock, positionTime, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, preview, out motion);
            }

            if (spatial.History.NewestRotationTime.HasValue)
            {
                double offset = clock.OffsetFrom(spatial.History.NewestRotationTime.Value, rotationTime, settings.InterpolationDelay);
                spatial.History.SampleRotation(offset, out rotation);
                if (processor != null)
                {
                    rotation = processor.ProjectRotation(spatial, rotation, offset, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
                }
            }
            else if (processor != null && spatial.HasRotation)
            {
                double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                rotation = processor.ProjectRotation(spatial, rotation, age, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
            }

            return new PresentationPose(position, rotation);
        }
    }
}
