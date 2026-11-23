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
            Extrapolation extrapolation = spatial.GetComponent<Extrapolation>();
            Smoothing smoothing = spatial.GetComponent<Smoothing>();
            PresentationSettings settings = new PresentationSettings(extrapolation, smoothing, interpolationDelay);
            if (extrapolation != null)
            {
                extrapolation.Prepare(spatial, timestamp);
            }

            PresentationPose previous = default(PresentationPose);
            // Try the old settings at the same time so the blend covers only the settings change.
            if (_tuning.Changed(settings))
            {
                previous = PreparePose(spatial, clock, timestamp, extrapolation, smoothing, sourceAxes, _tuning.Settings, true, out _);
            }

            // Save the new filter state once. Keep the final settings-change offset out of that state.
            PresentationPose current = PreparePose(spatial, clock, timestamp, extrapolation, smoothing, sourceAxes, settings, false, out _observation);
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

        private PresentationPose PreparePose(Spatial spatial, SpatialClock clock, double timestamp, Extrapolation extrapolation,
            Smoothing smoothing, CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 observation)
        {
            // Calculate the source pose within its prediction limit before applying packet correction or Smoothing.
            PresentationPose pose = SamplePose(spatial, clock, timestamp, extrapolation, sourceAxes, settings, preview, out Double3 motion, out observation);
            if (extrapolation != null)
            {
                Double3 velocity = default(Double3);
                Double3 angularVelocity = default(Double3);
                if (settings.PacketSmoothingDuration > 0)
                {
                    // Estimate velocity by comparing this pose with one slightly later on the same path.
                    // Comparing poses before and after a new packet would halve its starting velocity.
                    // At the prediction limit, both poses are held still, so this correctly gives zero velocity.
                    // Preview calls leave stored motion unchanged.
                    const double step = 0.001d;
                    PresentationPose after = SamplePose(spatial, clock, timestamp + step, extrapolation, sourceAxes, settings, true, out _, out _);
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
                pose = extrapolation.SmoothPacket(spatial, pose, velocity, angularVelocity, timestamp, settings.PacketSmoothingDuration, preview, out Double3 packetMotion);
                if (settings.PacketSmoothingDuration > 0)
                {
                    // Packet correction has already handled changes in predicted movement. Using the original
                    // prediction again in Smoothing would bring back the packet jump.
                    motion = packetMotion;
                }
            }

            // Use known movement to help Smoothing follow the pose. Smooth only the remaining position error.
            return smoothing != null ? smoothing.Prepare(spatial, pose, timestamp, motion, settings, preview) : pose;
        }

        private PresentationPose SamplePose(Spatial spatial, SpatialClock clock, double timestamp, Extrapolation extrapolation,
            CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 motion, out Double3 observation)
        {
            motion = default(Double3);
            observation = spatial.Position;
            // Each channel starts its requested forward interval at receipt, counting known sample age once.
            // Filters and tuning still use real presentation time, and extrapolation still applies its own cap.
            double positionTime = extrapolation != null
                ? extrapolation.PredictionTimestamp(clock, spatial.PositionTime, spatial.PositionReceivedTime, timestamp, settings.PredictionTime) : timestamp;
            double rotationTime = extrapolation != null
                ? extrapolation.PredictionTimestamp(clock, spatial.RotationTime, spatial.RotationReceivedTime, timestamp, settings.PredictionTime) : timestamp;
            PresentationPose pose;
            // With a playback delay, read stored samples. Otherwise, start at the latest position and rotation.
            if (settings.InterpolationDelay > 0)
            {
                pose = BufferedPose(spatial, clock, positionTime, rotationTime, extrapolation, sourceAxes, settings, preview, out motion, out observation);
            }
            else
            {
                Double3 position = extrapolation != null
                    ? extrapolation.Project(spatial, clock, positionTime, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, preview, out motion) : spatial.Position;
                Quaternion rotation = spatial.Rotation;
                if (spatial.History.NewestRotationTime.HasValue)
                {
                    double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                    spatial.History.SampleRotation(age, out rotation);
                }

                if (extrapolation != null && spatial.HasRotation)
                {
                    double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                    rotation = extrapolation.ProjectRotation(spatial, rotation, age, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
                }

                pose = new PresentationPose(position, rotation);
            }

            return pose;
        }

        private PresentationPose BufferedPose(Spatial spatial, SpatialClock clock, double positionTime, double rotationTime, Extrapolation extrapolation,
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
                if (extrapolation != null)
                {
                    position = extrapolation.ProjectTimestamped(spatial, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, position, offset, preview, out motion);
                }
            }
            else if (extrapolation != null)
            {
                // With no timestamp, use time since arrival because there are no SDK times to interpolate.
                position = extrapolation.Project(spatial, clock, positionTime, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, preview, out motion);
            }

            if (spatial.History.NewestRotationTime.HasValue)
            {
                double offset = clock.OffsetFrom(spatial.History.NewestRotationTime.Value, rotationTime, settings.InterpolationDelay);
                spatial.History.SampleRotation(offset, out rotation);
                if (extrapolation != null)
                {
                    rotation = extrapolation.ProjectRotation(spatial, rotation, offset, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
                }
            }
            else if (extrapolation != null && spatial.HasRotation)
            {
                double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
                rotation = extrapolation.ProjectRotation(spatial, rotation, age, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
            }

            return new PresentationPose(position, rotation);
        }
    }
}
