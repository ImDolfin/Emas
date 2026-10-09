using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that predicts position using timestamped observations and optional SDK motion.</summary>
    /// <remarks>Author beside Spatial; Smoothing is independently optional. ENU vectors use an explicit SDK tangent origin.
    /// Without SDK velocity, consecutive timestamped positions estimate it. No SDK binding is needed on this behavior trait.
    /// Disabling extrapolation retains supplied SDK motion for Smoothing between newly accepted timed observations;
    /// held observations do not advance from that motion while prediction is disabled. With a positive
    /// Realm.InterpolationDelay, pose and SDK motion are sampled on the shared buffered timeline. Prediction
    /// only covers gaps after the newest buffered observation, including orientation from its last timed arc.
    /// The horizon limits future extrapolation; newly accepted timed poses still contribute SDK motion to
    /// Smoothing after earlier packets have reached that limit.</remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Spatial))]
    [AddComponentMenu("Emas/Prediction")]
    public sealed class Prediction : Trait
    {
        [Tooltip("Maximum extrapolation beyond the newest observation, in seconds. With buffered playback, only fills gaps after recorded poses. 0: no prediction. 0.05-0.1: conservative; 0.1-0.25: bridges short packet gaps; 0.25-0.5: may overshoot stops or turns. Independent of smoothing half-life.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _maximumExtrapolation = 0.15f;
        private Double3 _velocity;
        private Double3 _acceleration;
        private bool _hasVelocity;
        private bool _hasAcceleration;
        private Timestamp? _velocityTime;
        private Timestamp? _accelerationTime;
        private long _positionVersion = -1;
        private Double3 _previousPosition;
        private Timestamp? _previousTime;
        private Double3 _estimatedVelocity;
        private bool _hasEstimatedVelocity;
        private double? _projectionTime;
        private double _elapsed;
        private Double3 _observationMotion;
        private Timestamp? _motionSampleTime;
        private double _motionOffset;

        /// <summary>Gets or sets the maximum extrapolated sample age in seconds; defaults to 0.15, independently of smoothing.</summary>
        /// <value>A finite, nonnegative horizon in seconds. Zero disables extrapolation, retaining SDK motion assistance for smoothing new observations.</value>
        /// <remarks>Live edits blend the setting-induced displacement over 0.25 seconds of real time, including zero
        /// and enabled changes, with or without Smoothing. Filter history, raw input, timestamps and motion remain intact.
        /// During that transition presentation can retain some of the previous limit's lead. Reaching the limit
        /// stops motion from held observations, while new timed observations retain supplied SDK motion assistance.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The assigned horizon is negative, NaN or infinite.</exception>
        public float MaximumExtrapolation
        {
            get => _maximumExtrapolation;
            set
            {
                Smoothing.ValidateTime(value, nameof(value));
                _maximumExtrapolation = value;
            }
        }

        /// <summary>Gets the last supplied Cartesian velocity or ECEF metres per second, also available to Smoothing with extrapolation disabled.</summary>
        /// <value>The accepted SDK velocity, meaningful while <see cref="HasVelocity"/> is true; estimated velocity is not exposed here.</value>
        public Double3 Velocity => _velocity;

        /// <summary>Gets whether SDK velocity is available; otherwise timestamped positions may estimate it.</summary>
        /// <value>True after a velocity setter accepts a sample, until supplied velocity is cleared.</value>
        public bool HasVelocity => _hasVelocity;

        /// <summary>Gets the last supplied Cartesian acceleration or ECEF metres per second squared.</summary>
        /// <value>The accepted SDK linear acceleration, meaningful while <see cref="HasAcceleration"/> is true.</value>
        public Double3 Acceleration => _acceleration;

        /// <summary>Gets whether optional SDK linear acceleration is available.</summary>
        /// <value>True after an acceleration setter accepts a sample, until acceleration is cleared.</value>
        public bool HasAcceleration => _hasAcceleration;

        /// <summary>Supplies velocity in shared Cartesian source units per second; older or duplicate timed observations are ignored.</summary>
        /// <param name="velocity">Finite velocity in the same source axes and distance units as the Cartesian position.</param>
        /// <param name="sampleTime">The original SDK observation time, or null for untimed input.</param>
        /// <exception cref="ArgumentOutOfRangeException">A velocity coordinate is not finite.</exception>
        public void SetCartesianVelocity(Double3 velocity, Timestamp? sampleTime = null)
        {
            StoreVelocity(velocity, sampleTime);
        }

        /// <summary>Supplies ECEF XYZ velocity in metres per second; older or duplicate timed observations are ignored.</summary>
        /// <param name="velocity">Finite Earth-centered, Earth-fixed XYZ velocity in metres per second.</param>
        /// <param name="sampleTime">The original SDK observation time, or null for untimed input.</param>
        /// <exception cref="ArgumentOutOfRangeException">A velocity coordinate is not finite.</exception>
        public void SetEarthCenteredVelocity(Double3 velocity, Timestamp? sampleTime = null)
        {
            StoreVelocity(velocity, sampleTime);
        }

        /// <summary>Converts ENU metres per second (X east, Y north, Z up) at the SDK tangent origin into ECEF velocity.</summary>
        /// <param name="velocity">The finite local east/north/up velocity in metres per second.</param>
        /// <param name="origin">The SDK tangent origin defining the velocity axes; its height does not affect the conversion.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed velocity samples are ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException">The origin or velocity is invalid, or conversion produces a nonfinite coordinate.</exception>
        public void SetGeographicVelocity(Double3 velocity, GeoPosition origin, Timestamp? sampleTime = null)
        {
            StoreVelocity(origin.ToEarthCenteredVector(velocity), sampleTime);
        }

        /// <summary>Clears supplied velocity; timestamp-based estimation remains available.</summary>
        /// <remarks>Also clears the velocity timestamp so the next supplied sample starts a new timed channel.</remarks>
        public void ClearVelocity()
        {
            _hasVelocity = false;
            _velocity = default(Double3);
            _velocityTime = null;
            Spatial spatial = GetComponent<Spatial>();
            if (spatial != null)
            {
                spatial.History.ClearVelocity();
            }
        }

        /// <summary>Supplies linear acceleration in Cartesian source units per second squared.</summary>
        /// <param name="acceleration">Finite linear acceleration in the position's source axes, with gravity removed.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed acceleration samples are ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException">An acceleration coordinate is not finite.</exception>
        public void SetCartesianAcceleration(Double3 acceleration, Timestamp? sampleTime = null)
        {
            StoreAcceleration(acceleration, sampleTime);
        }

        /// <summary>Supplies ECEF linear acceleration in metres per second squared, with gravity removed.</summary>
        /// <param name="acceleration">Finite Earth-centered, Earth-fixed XYZ linear acceleration in metres per second squared.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed acceleration samples are ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException">An acceleration coordinate is not finite.</exception>
        public void SetEarthCenteredAcceleration(Double3 acceleration, Timestamp? sampleTime = null)
        {
            StoreAcceleration(acceleration, sampleTime);
        }

        /// <summary>Converts ENU linear acceleration (X east, Y north, Z up) at the SDK origin; gravity must already be removed.</summary>
        /// <param name="acceleration">Finite east/north/up linear acceleration in metres per second squared.</param>
        /// <param name="origin">The SDK tangent origin defining the acceleration axes; its height does not affect the conversion.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed acceleration samples are ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException">The origin or acceleration is invalid, or conversion produces a nonfinite coordinate.</exception>
        public void SetGeographicAcceleration(Double3 acceleration, GeoPosition origin, Timestamp? sampleTime = null)
        {
            StoreAcceleration(origin.ToEarthCenteredVector(acceleration), sampleTime);
        }

        /// <summary>Clears acceleration while retaining supplied or estimated velocity.</summary>
        /// <remarks>Also clears the acceleration timestamp; acceleration alone never starts position prediction.</remarks>
        public void ClearAcceleration()
        {
            _hasAcceleration = false;
            _acceleration = default(Double3);
            _accelerationTime = null;
            Spatial spatial = GetComponent<Spatial>();
            if (spatial != null)
            {
                spatial.History.ClearAcceleration();
            }
        }

        /// <summary>Clears estimated motion history after a teleport while retaining supplied SDK motion and prediction settings.</summary>
        /// <remarks>Use <see cref="Spatial.ResetPresentation"/> when smoothing history must be reset at the same time.</remarks>
        public void Reset()
        {
            _positionVersion = -1;
            _previousTime = null;
            _hasEstimatedVelocity = false;
            _projectionTime = null;
            _observationMotion = default(Double3);
            _motionSampleTime = null;
            _motionOffset = 0;
        }

        internal void Prepare(Spatial spatial, double timestamp)
        {
            _observationMotion = default(Double3);
            ObservePosition(spatial);
            _elapsed = _projectionTime.HasValue ? Math.Max(0d, timestamp - _projectionTime.Value) : 0d;
            _projectionTime = timestamp;
        }

        internal Double3 Project(Spatial spatial, SpatialClock clock, double timestamp,
            float maximumExtrapolation, bool preview, out Double3 motion)
        {
            // Old and new tuning settings use identical observations and elapsed time without advancing history twice.
            motion = default(Double3);
            if (!spatial.HasPosition)
            {
                return spatial.Position;
            }

            if (!spatial.PositionTime.HasValue && !preview)
            {
                _motionSampleTime = null;
            }

            if (maximumExtrapolation <= 0 || (!_hasVelocity && !_hasEstimatedVelocity))
            {
                motion = DirectMotion(spatial, clock, timestamp, preview);
                return spatial.Position;
            }

            double age = clock.Age(spatial.PositionTime, spatial.PositionReceivedTime, timestamp);
            double horizon = Math.Min(age, maximumExtrapolation);
            // Estimated or untimed motion advances only inside this observation's remaining horizon.
            // Timed SDK motion below instead tracks actual source endpoints across newly accepted packets.
            double step = Math.Min(_elapsed, Math.Max(0d, maximumExtrapolation - Math.Max(0d, age - _elapsed)));
            Double3 velocity = _hasVelocity ? _velocity : _estimatedVelocity;
            Double3 acceleration = _hasAcceleration ? _acceleration : default(Double3);
            try
            {
                // Reconcile a separately timed velocity with the position observation's time.
                if (_hasVelocity)
                {
                    velocity = VelocityAtObservation(spatial, acceleration);
                }

                Double3 predicted = spatial.Position + velocity * horizon + acceleration * (0.5d * horizon * horizon);
                motion = velocity * step + acceleration * (step * (horizon - 0.5d * step));
                if (spatial.PositionTime.HasValue)
                {
                    Double3 suppliedMotion = TimedMotion(spatial, clock, timestamp, 0, horizon, velocity, acceleration, preview);
                    if (_hasVelocity)
                    {
                        motion = suppliedMotion;
                    }
                }

                return predicted;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Extreme finite motion must not poison the raw pose or transform.
                motion = default(Double3);
                return spatial.Position;
            }
        }

        internal void ResetTime()
        {
            _velocityTime = null;
            _accelerationTime = null;
            Reset();
        }

        internal Double3 ProjectBuffered(Spatial spatial, SpatialClock clock, double timestamp,
            PresentationSettings settings, Double3 position, double offset, bool preview, out Double3 motion)
        {
            motion = default(Double3);
            bool suppliedVelocity = HasBufferedVelocityAt(spatial.History, spatial.History.NewestPositionTime.Value);
            double maximum = suppliedVelocity || _hasEstimatedVelocity ? settings.MaximumExtrapolation : 0d;
            double oldest = spatial.History.OldestPositionTime.Value.ElapsedSince(spatial.PositionTime.Value);
            double endpoint = Math.Max(oldest, Math.Min(offset, maximum));

            try
            {
                // Extrapolation begins at the newest POSITION observation, never at a future motion sample.
                double horizon = Math.Max(0d, endpoint);
                Double3 velocity = default(Double3);
                Double3 acceleration = default(Double3);
                if (suppliedVelocity || (_hasEstimatedVelocity && horizon > 0))
                {
                    GapMotion(spatial.History, clock, timestamp - offset, settings.InterpolationDelay, suppliedVelocity,
                        out velocity, out acceleration);
                }

                motion = TimedMotion(spatial, clock, timestamp, settings.InterpolationDelay,
                    endpoint, velocity, acceleration, preview);
                return position + velocity * horizon + acceleration * (0.5d * horizon * horizon);
            }
            catch (ArgumentOutOfRangeException)
            {
                motion = default(Double3);
                return position;
            }
        }

        private Double3 DirectMotion(Spatial spatial, SpatialClock clock, double timestamp, bool preview)
        {
            if (!spatial.PositionTime.HasValue)
            {
                return _observationMotion;
            }

            try
            {
                Double3 acceleration = _hasAcceleration ? _acceleration : default(Double3);
                Double3 velocity = _hasVelocity ? VelocityAtObservation(spatial, acceleration) : default(Double3);
                return TimedMotion(spatial, clock, timestamp, 0, 0, velocity, acceleration, preview);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default(Double3);
            }
        }

        private Double3 TimedMotion(Spatial spatial, SpatialClock clock, double timestamp,
            double delay, double endpoint, Double3 gapVelocity, Double3 gapAcceleration, bool preview)
        {
            Timestamp sampleTime = spatial.PositionTime.Value;
            bool hasPrevious = _motionSampleTime.HasValue;
            double start = hasPrevious ? _motionSampleTime.Value.ElapsedSince(sampleTime) + _motionOffset : endpoint;
            if (!preview)
            {
                _motionSampleTime = sampleTime;
                _motionOffset = endpoint;
            }

            if (!_hasVelocity || !hasPrevious)
            {
                return default(Double3);
            }

            if (spatial.History.OldestVelocityTime.HasValue)
            {
                // Before the first supplied velocity there is no known SDK motion to assist this filter.
                double oldest = spatial.History.OldestVelocityTime.Value.ElapsedSince(sampleTime);
                start = Math.Max(start, oldest);
                endpoint = Math.Max(endpoint, oldest);
            }

            if (endpoint == start)
            {
                // A held sample at its cap, or the reference's duplicate preparation, contributes no motion.
                return default(Double3);
            }

            try
            {
                // Retain the PREVIOUS bounded source instant. Clamping both endpoints against a new packet
                // discards its legitimate motion once age exceeds the cap, producing speed-dependent filter lag.
                double offset = clock.OffsetFrom(sampleTime, timestamp, delay);
                return BufferedDisplacement(spatial.History, clock, timestamp - offset,
                    delay, start, endpoint, gapVelocity, gapAcceleration);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default(Double3);
            }
        }

        private Double3 BufferedDisplacement(PoseHistory history, SpatialClock clock,
            double observationTime, double delay, double start, double end, Double3 gapVelocity, Double3 gapAcceleration)
        {
            if (start > end)
            {
                // Live delay/horizon changes can move the target time backwards. Preview and commit
                // use the same previous endpoint, and the presentation transition blends this change.
                return BufferedDisplacement(history, clock, observationTime, delay,
                    end, start, gapVelocity, gapAcceleration) * -1d;
            }

            Double3 motion = default(Double3);
            if (start < 0)
            {
                double recordedEnd = Math.Min(0d, end);
                motion = IntegrateBufferedVelocity(history, clock, observationTime + start,
                    observationTime + recordedEnd, delay);
            }

            if (end > 0)
            {
                // Freeze the gap model at the newest position, exactly as target prediction does.
                // Later motion observations must not push the filter along a different trajectory.
                motion += GapDisplacement(gapVelocity, gapAcceleration, Math.Max(0d, start), end);
            }

            return motion;
        }

        private static Double3 GapDisplacement(Double3 velocity, Double3 acceleration, double start, double end)
        {
            return velocity * (end - start) + acceleration * (0.5d * (end * end - start * start));
        }

        private void GapMotion(PoseHistory history, SpatialClock clock, double observationTime,
            double delay, bool suppliedVelocity, out Double3 velocity, out Double3 acceleration)
        {
            velocity = suppliedVelocity ? BufferedVelocity(history, clock, observationTime, delay) : _estimatedVelocity;
            acceleration = BufferedAcceleration(history, clock, observationTime, delay);
        }

        private bool HasBufferedVelocityAt(PoseHistory history, Timestamp time)
        {
            return _hasVelocity && (!history.OldestVelocityTime.HasValue
                || time.CompareTo(history.OldestVelocityTime.Value) >= 0);
        }

        private Double3 IntegrateBufferedVelocity(PoseHistory history, SpatialClock clock,
            double startTime, double endTime, double delay)
        {
            if (!history.NewestVelocityTime.HasValue)
            {
                return _velocity * (endTime - startTime);
            }

            double start = clock.OffsetFrom(history.NewestVelocityTime.Value, startTime, delay);
            double end = clock.OffsetFrom(history.NewestVelocityTime.Value, endTime, delay);
            Double3 motion;
            if (!history.IntegrateVelocity(start, end, out motion))
            {
                return default(Double3);
            }

            // Recorded motion uses the supplied velocity curve, including its clamped tails.
            // Acceleration belongs to the gap model; applying a later acceleration retroactively to
            // an old velocity can inject metres of motion while interpolated observations stay still.
            return motion;
        }

        private Double3 BufferedVelocity(PoseHistory history, SpatialClock clock, double timestamp, double delay)
        {
            if (!history.NewestVelocityTime.HasValue)
            {
                return _velocity;
            }

            double offset = clock.OffsetFrom(history.NewestVelocityTime.Value, timestamp, delay);
            Double3 velocity;
            history.SampleVelocity(offset, out velocity);
            if (offset > 0 && _hasAcceleration)
            {
                velocity += BufferedAcceleration(history, clock, timestamp, delay) * offset;
            }

            return velocity;
        }

        private Double3 BufferedAcceleration(PoseHistory history, SpatialClock clock, double timestamp, double delay)
        {
            if (!history.NewestAccelerationTime.HasValue)
            {
                return _hasAcceleration ? _acceleration : default(Double3);
            }

            if (clock.OffsetFrom(history.OldestAccelerationTime.Value, timestamp, delay) < 0)
            {
                // A separately timed acceleration may describe a later maneuver that playback has not reached.
                return default(Double3);
            }

            Double3 acceleration;
            double offset = clock.OffsetFrom(history.NewestAccelerationTime.Value, timestamp, delay);
            history.SampleAcceleration(offset, out acceleration);
            return acceleration;
        }

        private void ObservePosition(Spatial spatial)
        {
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
                    _observationMotion = ObservationMotion(spatial, elapsed);
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

        private Double3 ObservationMotion(Spatial spatial, double elapsed)
        {
            if (!_hasVelocity)
            {
                // Position-derived velocity would copy observation noise straight into the filter's motion step.
                return default(Double3);
            }

            try
            {
                Double3 acceleration = _hasAcceleration ? _acceleration : default(Double3);
                Double3 velocity = VelocityAtObservation(spatial, acceleration);
                // The supplied velocity belongs to the NEW sample. Integrate backwards over the interval
                // between observations, rather than projecting either observation into the future.
                return velocity * elapsed - acceleration * (0.5d * elapsed * elapsed);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default(Double3);
            }
        }

        private Double3 VelocityAtObservation(Spatial spatial, Double3 acceleration)
        {
            return _hasAcceleration && spatial.PositionTime.HasValue && _velocityTime.HasValue
                ? _velocity + acceleration * spatial.PositionTime.Value.ElapsedSince(_velocityTime.Value) : _velocity;
        }

        private void StoreVelocity(Double3 velocity, Timestamp? sampleTime)
        {
            ReferenceFrame.ValidatePosition(velocity, nameof(velocity));
            if (PoseData.Accepts(_velocityTime, sampleTime))
            {
                _velocity = velocity;
                _velocityTime = sampleTime;
                _hasVelocity = true;
                PoseHistory history = GetComponent<Spatial>().History;
                if (sampleTime.HasValue)
                {
                    history.AddVelocity(velocity, sampleTime.Value);
                }
                else
                {
                    history.ClearVelocity();
                }
            }
        }

        private void StoreAcceleration(Double3 acceleration, Timestamp? sampleTime)
        {
            ReferenceFrame.ValidatePosition(acceleration, nameof(acceleration));
            if (PoseData.Accepts(_accelerationTime, sampleTime))
            {
                _acceleration = acceleration;
                _accelerationTime = sampleTime;
                _hasAcceleration = true;
                PoseHistory history = GetComponent<Spatial>().History;
                if (sampleTime.HasValue)
                {
                    history.AddAcceleration(acceleration, sampleTime.Value);
                }
                else
                {
                    history.ClearAcceleration();
                }
            }
        }

        internal override void Refresh()
        {
            // Read every input trait before predicting during the spatial phase.
        }

        internal override void ClearBinding()
        {
            ClearVelocity();
            ClearAcceleration();
            Reset();
        }

    }
}
