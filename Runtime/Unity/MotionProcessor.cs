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
        private ExtrapolationContext _context;
        private bool _hasContext;
        private ExtrapolationContext _alternateContext;
        private bool _hasAlternateContext;
        private long _contextPositionVersion;
        private long _contextRotationVersion;
        private long _contextMotionVersion;
        private readonly PredictionCache _predictions = new PredictionCache();
        private long _predictionContext;

        private readonly Spatial _spatial;

        internal Prediction Trajectory { get; private set; }

        internal double PositionOffset { get; private set; }

        internal double RotationOffset { get; private set; }

        internal PredictionInput Input { get; }

        private MotionData Motion => Input.Motion;

        private Spatial Spatial => _spatial;

        internal MotionProcessor(Spatial spatial)
        {
            _spatial = spatial;
            Input = new PredictionInput(spatial);
        }

        internal void Reset()
        {
            // Keep accepted inputs. Restart the filtered estimation window and per-instance filter state.
            Input.Reset();
            _hasContext = false;
            _hasAlternateContext = false;
            _positionVersion = -1;
            _previousTime = null;
            _hasEstimatedVelocity = false;
            Spatial spatial = Spatial;
            if (spatial != null)
            {
                spatial.History.RestartEstimation();
            }
        }

        internal void Prepare(Spatial spatial, double timestamp, CoordinateSystem sourceAxes)
        {
            // Context values depend on accepted inputs, not the render clock. Keep an
            // unchanged context across frames; CreateContext checks every input version.
            if (Input.Refresh(sourceAxes, timestamp))
            {
                _hasContext = false;
                _hasAlternateContext = false;
                _positionVersion = -1;
                _previousTime = null;
                _hasEstimatedVelocity = false;
            }

            ObservePosition(Input);
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

        internal Prediction Predict(SpatialClock clock, double timestamp, CoordinateSystem sourceAxes,
            PresentationSettings settings, out Double3 observation)
        {
            PredictionInput spatial = Input;
            // Manual timing is anchored to the original SDK creation time. Filter age is
            // added through the effective input time, rather than shifting the Realm target.
            double positionTime = PredictionTimestamp(clock, _spatial.PositionTime, spatial.PositionReceivedTime, timestamp, settings.PredictionTime);
            double rotationTime = PredictionTimestamp(clock, _spatial.RotationTime, spatial.RotationReceivedTime, timestamp, settings.PredictionTime);
            double positionOffset = clock.Age(spatial.PositionTime, spatial.PositionReceivedTime, positionTime);
            double rotationOffset = clock.Age(spatial.RotationTime, spatial.RotationReceivedTime, rotationTime);
            Double3 position = spatial.Position;
            Quaternion rotation = spatial.Rotation;
            bool buffered = settings.InterpolationDelay > 0 || clock.HasManualPrediction;
            if (spatial.History.NewestPositionTime.HasValue && buffered)
            {
                positionOffset = clock.OffsetFrom(spatial.History.NewestPositionTime.Value, positionTime, settings.InterpolationDelay);
                spatial.History.SamplePosition(positionOffset, out position);
            }

            if (spatial.History.NewestRotationTime.HasValue)
            {
                if (buffered)
                {
                    rotationOffset = clock.OffsetFrom(spatial.History.NewestRotationTime.Value, rotationTime, settings.InterpolationDelay);
                }

                spatial.History.SampleRotation(rotationOffset, out rotation);
            }

            // Visibility follows the accepted observation, independently of filtering and prediction.
            observation = _spatial.Position;
            if (buffered && _spatial.PositionTime.HasValue)
            {
                _spatial.History.SamplePosition(clock.OffsetFrom(_spatial.PositionTime.Value, positionTime, settings.InterpolationDelay), out observation);
            }

            double limit = settings.MaximumExtrapolation;
            double positionFilterAge = spatial.PositionTime.HasValue && _spatial.PositionTime.HasValue
                ? Math.Max(0d, _spatial.PositionTime.Value.ElapsedSince(spatial.PositionTime.Value)) : 0;
            double rotationFilterAge = spatial.RotationTime.HasValue && _spatial.RotationTime.HasValue
                ? Math.Max(0d, _spatial.RotationTime.Value.ElapsedSince(spatial.RotationTime.Value)) : 0;
            // The configured cap limits unsupported motion after the latest accepted packet.
            // Returning from a filter's older estimate to that observation time must not spend the gap budget.
            double positionSeconds = spatial.HasPosition ? Math.Max(0d, Math.Min(positionOffset, limit + positionFilterAge)) : 0;
            double rotationSeconds = spatial.HasRotation ? Math.Max(0d, Math.Min(rotationOffset, limit + rotationFilterAge)) : 0;
            ExtrapolationContext positionContext = CreateContext(spatial, sourceAxes, spatial.PositionTime);
            ExtrapolationContext rotationContext = CreateContext(spatial, sourceAxes, spatial.RotationTime);
            PredictionRequest request = new PredictionRequest(positionContext, rotationContext, position, rotation, positionSeconds, rotationSeconds);
            Prediction prediction = new Prediction(position, rotation);
            if (limit > 0 && (spatial.HasPosition || spatial.HasRotation))
            {
                try
                {
                    // Filters have finished. This is the only strategy invocation for this Ghost.
                    prediction = settings.Strategy.Predict(request);
                    SpatialMath.NormalizeRotation(prediction.Rotation, nameof(prediction));
                }
                catch (ArgumentOutOfRangeException)
                {
                    prediction = new Prediction(position, rotation);
                }
            }

            Trajectory = prediction;
            // Live tuning follows accepted packet times, independently of an older filter estimate.
            PositionOffset = clock.Age(_spatial.PositionTime, spatial.PositionReceivedTime, positionTime)
                - (buffered && _spatial.PositionTime.HasValue ? settings.InterpolationDelay : 0);
            RotationOffset = clock.Age(_spatial.RotationTime, spatial.RotationReceivedTime, rotationTime)
                - (buffered && _spatial.RotationTime.HasValue ? settings.InterpolationDelay : 0);
            return prediction;
        }

        internal void ResetRotationFilters()
        {
            Input.Reset();
            _hasContext = false;
            _hasAlternateContext = false;
        }

        private ExtrapolationContext CreateContext(PredictionInput spatial, CoordinateSystem sourceAxes, Timestamp? observationTime)
        {
            // The two pose channels often sample the same SDK time.
            // Reuse motion sampling across projections until an input, history or reference setting changes.
            // Both channel contexts are captured before invoking the strategy.
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

        private Double3? AngularMotion(PredictionInput spatial, CoordinateSystem sourceAxes, Timestamp? observationTime,
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

        private bool HasVelocityAt(PoseHistory history, Timestamp time)
        {
            return Motion.HasVelocity && (!history.OldestVelocityTime.HasValue
                || time.CompareTo(history.OldestVelocityTime.Value) >= 0);
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

        private void ObservePosition(PredictionInput spatial)
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
