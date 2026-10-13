using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that predicts pose using timestamped observations and optional SDK linear and angular motion.</summary>
    /// <remarks>Author beside Spatial; Smoothing is independently optional. ENU vectors use an explicit SDK tangent origin.
    /// Without SDK velocity, consecutive timestamped positions estimate it. No SDK binding is needed on this behavior trait.
    /// Disabling extrapolation retains supplied SDK motion for Smoothing between newly accepted timed observations;
    /// held observations do not advance from that motion while prediction is disabled. With a positive
    /// Realm.InterpolationDelay, pose and SDK motion are sampled on the shared buffered timeline. Prediction
    /// only covers gaps after the newest buffered observation. Orientation uses supplied body angular velocity,
    /// or extends its last timed arc when unavailable, with immediate or buffered playback.
    /// Timed SDK linear motion is sampled at the position observation's time;
    /// changes to its predicted lead are applied without the position smoothing half-life.
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
        private Double3 _angularVelocity;
        private bool _hasVelocity;
        private bool _hasAcceleration;
        private bool _hasAngularVelocity;
        private Timestamp? _velocityTime;
        private Timestamp? _accelerationTime;
        private Timestamp? _angularVelocityTime;
        private long _positionVersion = -1;
        private Double3 _previousPosition;
        private Timestamp? _previousTime;
        private Double3 _estimatedVelocity;
        private bool _hasEstimatedVelocity;
        private double? _projectionTime;
        private double _elapsed;
        private Timestamp? _motionSampleTime;
        private double _motionOffset;
        private Double3 _motionPrediction;

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

        /// <summary>Gets the last supplied body angular velocity after conversion to Unity body quaternion axes, in radians per second.</summary>
        /// <value>X is body right, Y up and Z forward, with signs matching Quaternion.AngleAxis; meaningful while HasAngularVelocity is true.</value>
        /// <remarks>With the default aircraft input axes, this is (-pitch rate, yaw rate, -roll rate). These are true body rates, not Euler-angle derivatives.</remarks>
        public Double3 AngularVelocity => _angularVelocity;

        /// <summary>Gets whether optional SDK body angular velocity is available for orientation prediction.</summary>
        /// <value>True after an angular velocity setter accepts a sample, until the channel is cleared or unbound.</value>
        public bool HasAngularVelocity => _hasAngularVelocity;

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
        /// <remarks>ENU axes are geographic tangent directions, independent of aircraft pitch or roll. Convert body-frame
        /// velocity into those directions using the raw SDK attitude at the motion observation's time before calling this.</remarks>
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
        /// <remarks>ENU axes are geographic tangent directions, independent of aircraft pitch or roll. Convert body-frame
        /// linear acceleration using the raw SDK attitude at the motion observation's time; remove gravity before supplying it.</remarks>
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

        /// <summary>Supplies optional body angular velocity in radians per second for bounded orientation prediction.</summary>
        /// <param name="angularVelocity">Finite true body angular rates in the specified SDK axes; these are not yaw/pitch/roll Euler-angle derivatives.</param>
        /// <param name="bodyAxes">Signed SDK body axes mapped to model right/up/forward. Null uses X forward, Y right, Z down: roll, pitch and yaw rates.</param>
        /// <param name="sampleTime">Original SDK observation time, or null for untimed input; older or duplicate timed angular samples are ignored.</param>
        /// <remarks>Positive rates use the source quaternion rotation convention. Converts and stores body rates independently of pose-reader order.
        /// Uses the rate sampled at the latest rotation's timestamp, before applying the shared prediction horizon. A supplied zero rate overrides
        /// rotation-history estimation. Clearing the channel restores that estimation. Angular velocity never changes linear motion or raw pose.
        /// Rotation smoothing retains its independent half-life; this input supplies prediction, rather than angular filter motion assistance.</remarks>
        /// <exception cref="ArgumentException">The body-axis mapping is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An angular velocity coordinate or its converted value is not finite.</exception>
        public void SetBodyAngularVelocity(Double3 angularVelocity, CoordinateSystem? bodyAxes = null, Timestamp? sampleTime = null)
        {
            ReferenceFrame.ValidatePosition(angularVelocity, nameof(angularVelocity));
            CoordinateSystem axes = bodyAxes ?? CoordinateSystem.NorthEastDown;
            string error = axes.GetConfigurationError();
            if (error != null)
            {
                throw new ArgumentException(error, nameof(bodyAxes));
            }

            Double3 converted = axes.ToUnityAngularVelocity(angularVelocity);
            if (PoseData.Accepts(_angularVelocityTime, sampleTime))
            {
                _angularVelocity = converted;
                _angularVelocityTime = sampleTime;
                _hasAngularVelocity = true;
                PoseHistory history = GetComponent<Spatial>().History;
                if (sampleTime.HasValue)
                {
                    history.AddAngularVelocity(converted, sampleTime.Value);
                }
                else
                {
                    history.ClearAngularVelocity();
                }
            }
        }

        /// <summary>Clears supplied angular velocity, restoring prediction from consecutive timed rotations when available.</summary>
        /// <remarks>Also clears angular timestamp ordering and playback history; linear velocity, acceleration and pose remain unchanged.</remarks>
        public void ClearAngularVelocity()
        {
            _hasAngularVelocity = false;
            _angularVelocity = default(Double3);
            _angularVelocityTime = null;
            Spatial spatial = GetComponent<Spatial>();
            if (spatial != null)
            {
                spatial.History.ClearAngularVelocity();
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
            _motionSampleTime = null;
            _motionOffset = 0;
            _motionPrediction = default(Double3);
        }

        internal void Prepare(Spatial spatial, double timestamp)
        {
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

            double age = clock.Age(spatial.PositionTime, spatial.PositionReceivedTime, timestamp);
            if (spatial.PositionTime.HasValue)
            {
                return ProjectTimestamped(spatial, maximumExtrapolation,
                    spatial.Position, age, preview, out motion);
            }

            if (!preview)
            {
                _motionSampleTime = null;
                _motionPrediction = default(Double3);
            }

            if (maximumExtrapolation <= 0 || (!_hasVelocity && !_hasEstimatedVelocity))
            {
                return spatial.Position;
            }

            double horizon = Math.Min(age, maximumExtrapolation);
            // Untimed motion advances only inside this observation's remaining horizon.
            double step = Math.Min(_elapsed, Math.Max(0d, maximumExtrapolation - Math.Max(0d, age - _elapsed)));
            Double3 velocity = _hasVelocity ? _velocity : _estimatedVelocity;
            Double3 acceleration = _hasAcceleration ? _acceleration : default(Double3);
            try
            {
                Double3 predicted = spatial.Position + velocity * horizon + acceleration * (0.5d * horizon * horizon);
                motion = velocity * step + acceleration * (step * (horizon - 0.5d * step));
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
            _angularVelocityTime = null;
            Reset();
        }

        internal Quaternion ProjectRotation(Spatial spatial, Quaternion rotation, double offset,
            float maximumExtrapolation, CoordinateSystem sourceAxes)
        {
            if (!_hasAngularVelocity || offset <= 0 || maximumExtrapolation <= 0)
            {
                return rotation;
            }

            Double3 angularVelocity = _angularVelocity;
            if (spatial.RotationTime.HasValue && _angularVelocityTime.HasValue
                && !spatial.History.SampleAngularVelocity(spatial.RotationTime.Value, out angularVelocity))
            {
                // A future-only rate cannot describe an older orientation; retain its observed-arc estimate.
                return rotation;
            }

            try
            {
                Quaternion displacement = SpatialMath.AngularDisplacement(angularVelocity, Math.Min(offset, maximumExtrapolation));
                if (spatial.RotationSpace == RotationSpace.Source)
                {
                    displacement = sourceAxes.ToSourceRotation(displacement);
                }
                else if (spatial.RotationSpace == RotationSpace.EarthCentered)
                {
                    displacement = spatial.EarthCenteredBodyAxes.Value.ToSourceRotation(displacement);
                }

                // Body rates compose on the right of the raw attitude. Applying them to the already
                // predicted or projected pose would extrapolate twice or include reference cancellation.
                return SpatialMath.NormalizeRotation(spatial.Rotation * displacement, nameof(angularVelocity));
            }
            catch (ArgumentOutOfRangeException)
            {
                return spatial.Rotation;
            }
        }

        internal Double3 ProjectTimestamped(Spatial spatial, float maximumExtrapolation,
            Double3 position, double offset, bool preview, out Double3 motion)
        {
            motion = default(Double3);
            bool suppliedVelocity = HasVelocityAt(spatial.History, spatial.PositionTime.Value);
            double maximum = suppliedVelocity || _hasEstimatedVelocity ? maximumExtrapolation : 0d;
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
                    GapMotion(spatial.History, spatial.PositionTime.Value, suppliedVelocity,
                        out velocity, out acceleration);
                }

                Double3 predicted = position + velocity * horizon + acceleration * (0.5d * horizon * horizon);
                motion = TimedMotion(spatial, endpoint, velocity, acceleration, preview);
                return predicted;
            }
            catch (ArgumentOutOfRangeException)
            {
                motion = default(Double3);
                return position;
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
            ClearAngularVelocity();
            Reset();
        }

        private Double3 TimedMotion(Spatial spatial, double endpoint,
            Double3 gapVelocity, Double3 gapAcceleration, bool preview)
        {
            Timestamp sampleTime = spatial.PositionTime.Value;
            bool hasPrevious = _motionSampleTime.HasValue;
            double end = Math.Min(endpoint, 0d);
            double start = hasPrevious ? _motionSampleTime.Value.ElapsedSince(sampleTime) + _motionOffset : end;
            Double3 previousPrediction = _motionPrediction;
            double horizon = Math.Max(endpoint, 0d);
            Double3 prediction = gapVelocity * horizon + gapAcceleration * (0.5d * horizon * horizon);
            if (!preview)
            {
                _motionSampleTime = sampleTime;
                _motionOffset = end;
                _motionPrediction = prediction;
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
                end = Math.Max(end, oldest);
            }

            if (end == start && prediction == previousPrediction)
            {
                // A held sample at its cap, or the reference's duplicate preparation, contributes no motion.
                return default(Double3);
            }

            try
            {
                // Integrate SDK motion between the observed playback poses, then replace the old future lead.
                // Filtering a revised velocity/acceleration lead as position noise delays known maneuvers;
                // differencing raw positions here would instead pass measurement noise straight through.
                return IntegrateVelocity(spatial.History, sampleTime, start, end) + prediction - previousPrediction;
            }
            catch (ArgumentOutOfRangeException)
            {
                return default(Double3);
            }
        }

        private void GapMotion(PoseHistory history, Timestamp observationTime,
            bool suppliedVelocity, out Double3 velocity, out Double3 acceleration)
        {
            velocity = suppliedVelocity ? VelocityAt(history, observationTime) : _estimatedVelocity;
            acceleration = AccelerationAt(history, observationTime);
        }

        private bool HasVelocityAt(PoseHistory history, Timestamp time)
        {
            return _hasVelocity && (!history.OldestVelocityTime.HasValue
                || time.CompareTo(history.OldestVelocityTime.Value) >= 0);
        }

        private Double3 IntegrateVelocity(PoseHistory history, Timestamp observationTime, double start, double end)
        {
            if (!history.NewestVelocityTime.HasValue)
            {
                return _velocity * (end - start);
            }

            // Compare SDK timestamp parts directly. Reconstructing the observation through the local
            // clock can place an exactly matching sample just before history and lose its motion.
            double offset = observationTime.ElapsedSince(history.NewestVelocityTime.Value);
            Double3 motion;
            if (!history.IntegrateVelocity(offset + start, offset + end, out motion))
            {
                return default(Double3);
            }

            // Recorded motion uses the supplied velocity curve, including its clamped tails.
            // Acceleration belongs to the gap model; applying a later acceleration retroactively to
            // an old velocity can inject metres of motion while interpolated observations stay still.
            return motion;
        }

        private Double3 VelocityAt(PoseHistory history, Timestamp observationTime)
        {
            if (!history.NewestVelocityTime.HasValue)
            {
                return _velocity;
            }

            double offset = observationTime.ElapsedSince(history.NewestVelocityTime.Value);
            Double3 velocity;
            history.SampleVelocity(offset, out velocity);
            if (offset > 0 && _hasAcceleration)
            {
                velocity += AccelerationAt(history, observationTime) * offset;
            }

            return velocity;
        }

        private Double3 AccelerationAt(PoseHistory history, Timestamp observationTime)
        {
            if (!history.NewestAccelerationTime.HasValue)
            {
                return _hasAcceleration ? _acceleration : default(Double3);
            }

            if (observationTime.CompareTo(history.OldestAccelerationTime.Value) < 0)
            {
                // A separately timed acceleration may describe a later maneuver that playback has not reached.
                return default(Double3);
            }

            Double3 acceleration;
            double offset = observationTime.ElapsedSince(history.NewestAccelerationTime.Value);
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

    }
}
