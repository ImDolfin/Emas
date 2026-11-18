using System;
using UnityEngine;

namespace Emas
{
    // Owns source-space presentation, keeping playback and filters separate from raw input and transform projection.
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
            Extrapolation extrapolation = spatial.GetComponent<Extrapolation>();
            Smoothing smoothing = spatial.GetComponent<Smoothing>();
            PresentationSettings settings = new PresentationSettings(extrapolation, smoothing, interpolationDelay);
            if (extrapolation != null)
            {
                extrapolation.Prepare(spatial, timestamp);
            }

            PresentationPose previous = default(PresentationPose);
            if (_tuning.Changed(settings))
            {
                previous = PreparePose(spatial, clock, timestamp, extrapolation, smoothing, sourceAxes, _tuning.Settings, true, out _);
            }

            PresentationPose current = PreparePose(spatial, clock, timestamp, extrapolation, smoothing, sourceAxes, settings, false, out _observation);
            current = _tuning.Apply(previous, current, settings, timestamp);
            _position = current.Position;
            _rotation = current.Rotation;
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
            PresentationPose pose = SamplePose(spatial, clock, timestamp, extrapolation, sourceAxes, settings, preview, out Double3 motion, out observation);
            if (extrapolation != null)
            {
                Double3 velocity = default(Double3);
                Double3 angularVelocity = default(Double3);
                if (settings.PacketSmoothingDuration > 0)
                {
                    // Use a forward difference on the same bounded trajectory, including buffered playback.
                    // A central difference straddles the held past at a fresh packet and halves its tangent;
                    // at the horizon the forward difference correctly reports that presentation has stopped.
                    // Preview calls do not advance motion history.
                    const double step = 0.001d;
                    PresentationPose after = SamplePose(spatial, clock, timestamp + step, extrapolation, sourceAxes, settings, true, out _, out _);
                    try
                    {
                        velocity = (after.Position - pose.Position) * (1d / step);
                        angularVelocity = SpatialMath.RotationRate(pose.Rotation, after.Rotation, step);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Unrepresentable optional tangents must not invalidate the baseline pose.
                    }
                }

                pose = extrapolation.SmoothPacket(spatial, pose, velocity, angularVelocity, timestamp, settings.PacketSmoothingDuration, preview, out Double3 packetMotion);
                if (settings.PacketSmoothingDuration > 0)
                {
                    // SDK lead changes have already been corrected by the spline. Advancing an ordinary
                    // position filter with the uncorrected SDK lead would reintroduce the packet jump.
                    motion = packetMotion;
                }
            }

            return smoothing != null ? smoothing.Prepare(spatial, pose, timestamp, motion, settings, preview) : pose;
        }

        private PresentationPose SamplePose(Spatial spatial, SpatialClock clock, double timestamp, Extrapolation extrapolation,
            CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 motion, out Double3 observation)
        {
            motion = default(Double3);
            observation = spatial.Position;
            PresentationPose pose;
            if (settings.InterpolationDelay > 0)
            {
                pose = BufferedPose(spatial, clock, timestamp, extrapolation, sourceAxes, settings, preview, out motion, out observation);
            }
            else
            {
                Double3 position = extrapolation != null
                    ? extrapolation.Project(spatial, clock, timestamp, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, preview, out motion) : spatial.Position;
                Quaternion rotation = spatial.Rotation;
                if (spatial.History.NewestRotationTime.HasValue)
                {
                    double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, timestamp);
                    spatial.History.SampleRotation(age, out rotation);
                }

                if (extrapolation != null && spatial.HasRotation)
                {
                    double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, timestamp);
                    rotation = extrapolation.ProjectRotation(spatial, rotation, age, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
                }

                pose = new PresentationPose(position, rotation);
            }

            return pose;
        }

        private PresentationPose BufferedPose(Spatial spatial, SpatialClock clock, double timestamp, Extrapolation extrapolation,
            CoordinateSystem sourceAxes, PresentationSettings settings, bool preview, out Double3 motion, out Double3 observation)
        {
            Double3 position = spatial.Position;
            Quaternion rotation = spatial.Rotation;
            observation = spatial.Position;
            motion = default(Double3);
            if (spatial.History.NewestPositionTime.HasValue)
            {
                double offset = clock.OffsetFrom(spatial.History.NewestPositionTime.Value, timestamp, settings.InterpolationDelay);
                spatial.History.SamplePosition(offset, out position);
                observation = position;
                if (extrapolation != null)
                {
                    position = extrapolation.ProjectTimestamped(spatial, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, position, offset, preview, out motion);
                }
            }
            else if (extrapolation != null)
            {
                // An untimed channel has no SDK history to interpolate; preserve its arrival-timed behavior.
                position = extrapolation.Project(spatial, clock, timestamp, settings.MaximumExtrapolation, settings.Strategy, sourceAxes, preview, out motion);
            }

            if (spatial.History.NewestRotationTime.HasValue)
            {
                double offset = clock.OffsetFrom(spatial.History.NewestRotationTime.Value, timestamp, settings.InterpolationDelay);
                spatial.History.SampleRotation(offset, out rotation);
                if (extrapolation != null)
                {
                    rotation = extrapolation.ProjectRotation(spatial, rotation, offset, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
                }
            }
            else if (extrapolation != null && spatial.HasRotation)
            {
                double age = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, timestamp);
                rotation = extrapolation.ProjectRotation(spatial, rotation, age, settings.MaximumExtrapolation, settings.Strategy, sourceAxes);
            }

            return new PresentationPose(position, rotation);
        }
    }
}
