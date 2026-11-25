using System;
using UnityEngine;

namespace Emas
{
    // Spatial owns prediction history and motion assistance. Optional components only
    // configure prediction/filtering; supplying data never requires those components.
    internal sealed class MotionProcessor
    {
        private long _positionVersion = -1;
        private Double3 _previousPosition;
        private Timestamp? _previousTime;
        private Double3 _estimatedVelocity;
        private bool _hasEstimatedVelocity;
        private double? _projectionTime;
        private double _elapsed;
        private Timestamp? _motionSampleTime;
        private double _motionOffset;
        private Double3 _motionExtrapolation;
        private PacketSmoother _packetSmoother;
        private ExtrapolationContext _context;
        private bool _hasContext;
        private ExtrapolationContext _alternateContext;
        private bool _hasAlternateContext;
        private long _contextPositionVersion;
        private long _contextRotationVersion;
        private long _contextMotionVersion;
        private bool _hasRotationMotion;
        private Timestamp? _rotationMotionSampleTime;
        private double _rotationMotionOffset;
        private readonly PredictionCache _predictions = new PredictionCache();
        private long _predictionContext;

        private readonly Spatial _spatial;

        private MotionData Motion => _spatial.Motion;

        private Spatial Spatial => _spatial;

        internal MotionProcessor(Spatial spatial)
        {
            _spatial = spatial;
        }

        internal void Reset()
        {
            // Keep motion supplied by the application. Clear estimated motion and this root's filter state.
            _hasContext = false;
            _hasAlternateContext = false;
            _hasRotationMotion = false;
            _positionVersion = -1;
            _previousTime = null;
            _hasEstimatedVelocity = false;
            _projectionTime = null;
            _motionSampleTime = null;
            _motionOffset = 0;
            _motionExtrapolation = default(Double3);
            _packetSmoother = default(PacketSmoother);
            Spatial spatial = Spatial;
            if (spatial != null)
            {
                spatial.History.RestartEstimation();
            }
        }

        internal void Prepare(Spatial spatial, double timestamp)
        {
            // Context values depend on accepted inputs, not the render clock. Keep an
            // unchanged context across frames; CreateContext checks every input version.
            ObservePosition(spatial);
            _elapsed = _projectionTime.HasValue ? Math.Max(0d, timestamp - _projectionTime.Value) : 0d;
            _projectionTime = timestamp;
        }

        internal double PredictionTimestamp(SpatialClock clock, Timestamp? sampleTime, double receivedTime,
            double timestamp, float predictionTime)
        {
            if (clock.HasReference)
            {
                return clock.PredictionTimestamp(timestamp);
            }

            if (predictionTime <= 0)
            {
                return timestamp;
            }

            // Without a followed clock, the override is individual. Replace the clock's
            // inferred age with the requested interval plus elapsed time since receipt.
            double automatic = sampleTime.HasValue ? clock.OffsetFrom(sampleTime.Value, timestamp, 0) : timestamp - receivedTime;
            return timestamp + predictionTime + Math.Max(0d, timestamp - receivedTime) - automatic;
        }

        internal Double3 Project(Spatial spatial, SpatialClock clock, double timestamp,
            float maximumExtrapolation, IExtrapolationStrategy strategy, CoordinateSystem sourceAxes, bool preview, out Double3 motion)
        {
            // Try old and new settings with the same input and time. Save the state only once.
            motion = default(Double3);
            if (!spatial.HasPosition)
            {
                return spatial.Position;
            }

            // Use the shared SDK clock for timestamped input. Otherwise, use time since this input arrived.
            double age = clock.Age(spatial.PositionTime, spatial.PositionReceivedTime, timestamp);
            if (spatial.PositionTime.HasValue)
            {
                return ProjectTimestamped(spatial, maximumExtrapolation,
                    strategy, sourceAxes, spatial.Position, age, preview, out motion);
            }

            if (!preview)
            {
                _motionSampleTime = null;
                _motionExtrapolation = default(Double3);
            }

            if (maximumExtrapolation <= 0)
            {
                return spatial.Position;
            }

            // Predict only as far as the observation's age, up to the allowed time limit.
            double horizon = Math.Min(age, maximumExtrapolation);
            // Motion without a timestamp can advance only within this observation's remaining prediction time.
            double step = Math.Min(_elapsed, Math.Max(0d, maximumExtrapolation - Math.Max(0d, age - _elapsed)));
            try
            {
                ExtrapolationContext context = CreateContext(spatial, sourceAxes, null);
                Double3 lead = strategy.ExtrapolateDisplacement(context, horizon);
                Double3 predicted = spatial.Position + lead;
                motion = lead - strategy.ExtrapolateDisplacement(context, Math.Max(0d, horizon - step));
                return predicted;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Very large motion values must not corrupt the raw pose or transform.
                motion = default(Double3);
                return spatial.Position;
            }
        }

        internal PresentationPose SmoothPacket(Spatial spatial, PresentationPose pose, Double3 velocity,
            Double3 angularVelocity, double timestamp, float duration, bool preview, out Double3 motion, out Quaternion rotationMotion)
        {
            // Try settings on a copy so previews do not change the saved correction state.
            PacketSmoother smoother = _packetSmoother;
            pose = smoother.Apply(spatial, pose, velocity, angularVelocity, Motion.Version, timestamp, duration, out motion, out rotationMotion);
            if (!preview)
            {
                _packetSmoother = smoother;
            }

            return pose;
        }

        internal Quaternion RotationMotion(Spatial spatial, double offset, float maximumExtrapolation,
            IExtrapolationStrategy strategy, CoordinateSystem sourceAxes, bool preview, bool enabled, out bool world)
        {
            world = false;
            bool timed = spatial.RotationTime.HasValue;
            double endpoint = timed ? Math.Min(offset, maximumExtrapolation) : 0;
            if (timed && spatial.History.OldestRotationTime.HasValue)
            {
                endpoint = Math.Max(endpoint, spatial.History.OldestRotationTime.Value.ElapsedSince(spatial.RotationTime.Value));
            }

            double step = 0;
            if (_hasRotationMotion && timed == _rotationMotionSampleTime.HasValue)
            {
                step = timed ? Math.Max(0d, endpoint - _rotationMotionSampleTime.Value.ElapsedSince(spatial.RotationTime.Value) - _rotationMotionOffset)
                    : Math.Min(_elapsed, Math.Max(0d, maximumExtrapolation - Math.Max(0d, offset - _elapsed)));
                // A new packet or clock update can move the sampled timeline much farther than
                // one frame. Treat the excess as a correction instead of bypassing its filter.
                step = Math.Min(step, _elapsed);
            }

            if (!preview)
            {
                _hasRotationMotion = true;
                _rotationMotionSampleTime = spatial.RotationTime;
                _rotationMotionOffset = endpoint;
            }

            if (step <= 0 || !enabled)
            {
                return Quaternion.identity;
            }

            try
            {
                // Advance the filter by the model's angular motion, rather than by the noisy
                // difference between two packets. A capped target contributes no further motion.
                ExtrapolationContext context = CreateContext(spatial, sourceAxes, spatial.RotationTime);
                world = context.AngularVelocityIsWorld;
                return RotationDisplacement(spatial, context, strategy, sourceAxes, step);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Quaternion.identity;
            }
        }

        internal bool EstimatesAngularMotion(Spatial spatial, CoordinateSystem sourceAxes)
        {
            return CreateContext(spatial, sourceAxes, spatial.RotationTime).AngularVelocityIsEstimated;
        }

        internal void ResetPacketRotation()
        {
            _packetSmoother.ResetRotation();
        }

        internal Quaternion ProjectRotation(Spatial spatial, Quaternion rotation, double offset,
            float maximumExtrapolation, IExtrapolationStrategy strategy, CoordinateSystem sourceAxes)
        {
            if (offset <= 0 || maximumExtrapolation <= 0)
            {
                return rotation;
            }

            try
            {
                // Read motion at the rotation sample time, then predict up to the time limit.
                ExtrapolationContext context = CreateContext(spatial, sourceAxes, spatial.RotationTime);
                Quaternion displacement = RotationDisplacement(spatial, context, strategy, sourceAxes, Math.Min(offset, maximumExtrapolation));

                // Multiply body rotation changes on the right and world rotation changes on the left. Start from raw
                // rotation each time so repeated updates and reference changes do not add prediction twice.
                return SpatialMath.NormalizeRotation(context.AngularVelocityIsWorld ? displacement * spatial.Rotation
                    : spatial.Rotation * displacement, nameof(strategy));
            }
            catch (ArgumentOutOfRangeException)
            {
                return spatial.Rotation;
            }
        }

        internal Double3 ProjectTimestamped(Spatial spatial, float maximumExtrapolation,
            IExtrapolationStrategy strategy, CoordinateSystem sourceAxes, Double3 position, double offset, bool preview, out Double3 motion)
        {
            motion = default(Double3);
            double oldest = spatial.History.OldestPositionTime.Value.ElapsedSince(spatial.PositionTime.Value);
            double endpoint = Math.Max(oldest, Math.Min(offset, maximumExtrapolation));

            try
            {
                // Predict from the newest position sample, never from a later motion sample.
                double horizon = Math.Max(0d, endpoint);
                Double3 lead = horizon > 0
                    ? strategy.ExtrapolateDisplacement(CreateContext(spatial, sourceAxes, spatial.PositionTime), horizon)
                    : default(Double3);
                Double3 predicted = position + lead;
                motion = TimedMotion(spatial, endpoint, lead, preview);
                return predicted;
            }
            catch (ArgumentOutOfRangeException)
            {
                motion = default(Double3);
                return position;
            }
        }

        private static Quaternion RotationDisplacement(Spatial spatial, in ExtrapolationContext context,
            IExtrapolationStrategy strategy, CoordinateSystem sourceAxes, double seconds)
        {
            Quaternion displacement = SpatialMath.NormalizeRotation(strategy.ExtrapolateRotation(context, seconds), nameof(strategy));
            if (spatial.RotationSpace == RotationSpace.Source)
            {
                displacement = sourceAxes.ToSourceRotation(displacement);
            }
            else if (!context.AngularVelocityIsWorld && spatial.RotationSpace == RotationSpace.EarthCentered)
            {
                displacement = spatial.EarthCenteredBodyAxes.Value.ToSourceRotation(displacement);
            }

            return displacement;
        }

        private ExtrapolationContext CreateContext(Spatial spatial, CoordinateSystem sourceAxes, Timestamp? observationTime)
        {
            // Position, rotation and packet-correction previews often sample the same SDK time.
            // Reuse that motion sample within this preparation, while allowing a custom strategy
            // to change input or reference settings before another channel is evaluated.
            Ghost ghost = spatial.Ghost;
            ReferenceFrame frame = ghost != null && ghost.Anchor != null ? ghost.Anchor.Realm.ReferenceFrame : null;
            ReferenceSpace space = frame != null ? frame.Space : ReferenceSpace.Cartesian;
            bool sameInputs = _hasContext && _contextPositionVersion == spatial.PositionVersion
                && _contextRotationVersion == spatial.RotationVersion && _contextMotionVersion == Motion.Version
                && _context.Space == space
                && _context.SourceCoordinates.Right == sourceAxes.Right
                && _context.SourceCoordinates.Up == sourceAxes.Up
                && _context.SourceCoordinates.Forward == sourceAxes.Forward;
            if (sameInputs && Nullable.Equals(_context.ObservationTime, observationTime))
            {
                return _context;
            }

            if (sameInputs && _hasAlternateContext && Nullable.Equals(_alternateContext.ObservationTime, observationTime))
            {
                return _alternateContext;
            }

            // Position and orientation may have different SDK times. Retain both samples
            // so packet previews do not repeatedly rebuild them as channels alternate.
            if (sameInputs)
            {
                _alternateContext = _context;
                _hasAlternateContext = true;
            }
            else
            {
                _hasAlternateContext = false;
            }

            // Use supplied velocity at the requested time when available. Otherwise, estimate from known poses.
            PoseHistory history = spatial.History;
            bool suppliedVelocity = Motion.HasVelocity && (!observationTime.HasValue || HasVelocityAt(history, observationTime.Value));
            bool estimatedVelocity = !suppliedVelocity && _hasEstimatedVelocity
                && (!observationTime.HasValue || !spatial.PositionTime.HasValue
                    || spatial.PositionTime.Value.CompareTo(observationTime.Value) <= 0);
            Double3? velocity = null;
            Double3? acceleration = null;
            try
            {
                if (suppliedVelocity)
                {
                    velocity = observationTime.HasValue ? VelocityAt(history, observationTime.Value) : Motion.Velocity;
                }
                else if (estimatedVelocity)
                {
                    velocity = _estimatedVelocity;
                }

                if (Motion.HasAcceleration && (!observationTime.HasValue || !history.OldestAccelerationTime.HasValue
                    || observationTime.Value.CompareTo(history.OldestAccelerationTime.Value) >= 0))
                {
                    acceleration = observationTime.HasValue ? AccelerationAt(history, observationTime.Value) : Motion.Acceleration;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // If position calculations overflow, keep predicting rotation when its input is still valid.
                velocity = null;
                acceleration = null;
                estimatedVelocity = false;
            }

            // Tell each strategy where the angular rate came from and whether it uses body or world axes.
            Double3? angularVelocity = AngularMotion(spatial, sourceAxes, observationTime,
                out bool estimatedAngularVelocity, out bool worldAngularVelocity);
            _context = new ExtrapolationContext(spatial, sourceAxes, observationTime, velocity, acceleration,
                angularVelocity, estimatedVelocity, estimatedAngularVelocity, worldAngularVelocity,
                Motion.VelocityTime, Motion.AccelerationTime, Motion.AngularVelocityTime, _predictions, ++_predictionContext);
            _contextPositionVersion = spatial.PositionVersion;
            _contextRotationVersion = spatial.RotationVersion;
            _contextMotionVersion = Motion.Version;
            _hasContext = true;
            return _context;
        }

        private Double3? AngularMotion(Spatial spatial, CoordinateSystem sourceAxes, Timestamp? observationTime,
            out bool estimated, out bool world)
        {
            estimated = false;
            world = false;
            Double3 rate = Motion.AngularVelocity;
            // A supplied zero rate still counts as input. Estimate only if no supplied rate covers this time.
            bool supplied = Motion.HasAngularVelocity && (!observationTime.HasValue || !Motion.AngularVelocityTime.HasValue
                || spatial.History.SampleAngularVelocity(observationTime.Value, out rate));
            try
            {
                if (!supplied)
                {
                    if (!spatial.History.EstimateAngularVelocity(observationTime, out rate))
                    {
                        return null;
                    }

                    // The observed rotation change uses the stored body axes. Convert it
                    // back to model axes so supplied and estimated rates use the same axes.
                    if (spatial.RotationSpace == RotationSpace.Source)
                    {
                        rate = sourceAxes.ToUnityAngularVelocity(rate);
                    }
                    else if (spatial.RotationSpace == RotationSpace.EarthCentered)
                    {
                        rate = spatial.EarthCenteredBodyAxes.Value.ToUnityAngularVelocity(rate);
                    }

                    estimated = true;
                }
                else if (Motion.AngularVelocityIsWorld)
                {
                    world = true;
                    if (spatial.RotationSpace != RotationSpace.EarthCentered)
                    {
                        if (!spatial.HasPosition)
                        {
                            world = false;
                            return null;
                        }

                        // Angular vectors reverse sign when ECEF's right-handed basis becomes
                        // the left-handed local east/up/north quaternion basis.
                        Double3? position = ExtrapolationContext.SamplePosition(spatial, observationTime);
                        if (!position.HasValue)
                        {
                            world = false;
                            return null;
                        }

                        GeographicBasis basis = spatial.BasisAt(position.Value);
                        rate = basis.ToLocal(rate) * -1d;
                    }
                }

                return rate;
            }
            catch (ArgumentOutOfRangeException)
            {
                // An invalid angular rate must not stop prediction from valid position data.
                estimated = false;
                world = false;
                return null;
            }
        }

        private Double3 TimedMotion(Spatial spatial, double endpoint, Double3 extrapolation, bool preview)
        {
            Timestamp sampleTime = spatial.PositionTime.Value;
            bool hasPrevious = _motionSampleTime.HasValue;
            double end = Math.Min(endpoint, 0d);
            double start = hasPrevious ? _motionSampleTime.Value.ElapsedSince(sampleTime) + _motionOffset : end;
            Double3 previousExtrapolation = _motionExtrapolation;
            if (!preview)
            {
                _motionSampleTime = sampleTime;
                _motionOffset = end;
                _motionExtrapolation = extrapolation;
            }

            if (!Motion.HasVelocity || !hasPrevious)
            {
                return default(Double3);
            }

            if (spatial.History.OldestVelocityTime.HasValue)
            {
                // Before the first supplied velocity, the filter has no SDK motion to use.
                double oldest = spatial.History.OldestVelocityTime.Value.ElapsedSince(sampleTime);
                start = Math.Max(start, oldest);
                end = Math.Max(end, oldest);
            }

            if (end == start && extrapolation == previousExtrapolation)
            {
                // Add no movement after the prediction limit or when evaluating the same playback time again.
                return default(Double3);
            }

            try
            {
                // Sum SDK motion between playback poses, then replace the old predicted movement.
                // Treating a changed motion prediction as position noise would delay known turns and acceleration.
                // Using raw position differences instead would pass measurement noise straight through.
                return IntegrateVelocity(spatial.History, sampleTime, start, end) + extrapolation - previousExtrapolation;
            }
            catch (ArgumentOutOfRangeException)
            {
                return default(Double3);
            }
        }

        private bool HasVelocityAt(PoseHistory history, Timestamp time)
        {
            return Motion.HasVelocity && (!history.OldestVelocityTime.HasValue
                || time.CompareTo(history.OldestVelocityTime.Value) >= 0);
        }

        private Double3 IntegrateVelocity(PoseHistory history, Timestamp observationTime, double start, double end)
        {
            if (!history.NewestVelocityTime.HasValue)
            {
                return Motion.Velocity * (end - start);
            }

            // Compare SDK timestamp parts directly. Reconstructing the observation through the local
            // clock can place an exact match just before the first stored sample and lose its motion.
            double offset = observationTime.ElapsedSince(history.NewestVelocityTime.Value);
            Double3 motion;
            if (!history.IntegrateVelocity(offset + start, offset + end, out motion))
            {
                return default(Double3);
            }

            // Recorded motion follows supplied velocity and holds the first or last value outside stored times.
            // Use acceleration only when predicting past the stored pose. Applying later acceleration to
            // an older velocity could add metres of movement while the recorded position stays still.
            return motion;
        }

        private Double3 VelocityAt(PoseHistory history, Timestamp observationTime)
        {
            if (!history.NewestVelocityTime.HasValue)
            {
                return Motion.Velocity;
            }

            double offset = observationTime.ElapsedSince(history.NewestVelocityTime.Value);
            Double3 velocity;
            history.SampleVelocity(offset, out velocity);
            if (offset > 0 && Motion.HasAcceleration)
            {
                velocity += AccelerationAt(history, observationTime) * offset;
            }

            return velocity;
        }

        private Double3 AccelerationAt(PoseHistory history, Timestamp observationTime)
        {
            if (!history.NewestAccelerationTime.HasValue)
            {
                return Motion.HasAcceleration ? Motion.Acceleration : default(Double3);
            }

            if (observationTime.CompareTo(history.OldestAccelerationTime.Value) < 0)
            {
                // A later acceleration sample may describe movement that playback has not reached yet.
                return default(Double3);
            }

            Double3 acceleration;
            double offset = observationTime.ElapsedSince(history.NewestAccelerationTime.Value);
            history.SampleAcceleration(offset, out acceleration);
            return acceleration;
        }

        private void ObservePosition(Spatial spatial)
        {
            // Estimate once per new pose. Repeated updates must not create samples with no time between them.
            if (!spatial.HasPosition || _positionVersion == spatial.PositionVersion)
            {
                return;
            }

            _hasEstimatedVelocity = false;
            if (_previousTime.HasValue && spatial.PositionTime.HasValue)
            {
                double elapsed = spatial.PositionTime.Value.ElapsedSince(_previousTime.Value);
                if (elapsed > 0)
                {
                    try
                    {
                        _estimatedVelocity = (spatial.Position - _previousPosition) * (1d / elapsed);
                        _hasEstimatedVelocity = true;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        _hasEstimatedVelocity = false;
                    }
                }
            }

            _positionVersion = spatial.PositionVersion;
            _previousPosition = spatial.Position;
            _previousTime = spatial.PositionTime;
        }

    }
}
