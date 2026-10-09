using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that predicts position using timestamped observations and optional SDK motion.</summary>
    /// <remarks>Author beside Spatial; Smoothing is independently optional. ENU vectors use an explicit SDK tangent origin.
    /// Without SDK velocity, consecutive timestamped positions estimate it. No SDK binding is needed on this behavior trait.</remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Spatial))]
    [AddComponentMenu("Emas/Prediction")]
    public sealed class Prediction : Trait
    {
        [Tooltip("Maximum sample age to extrapolate, in seconds. 0: no prediction. 0.05-0.1: conservative; 0.1-0.25: bridges short packet gaps (start at 0.15); 0.25-0.5: bridges longer gaps but may overshoot stops or turns. Independent of smoothing half-life.")]
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

        /// <summary>Gets or sets the maximum extrapolated sample age in seconds; defaults to 0.15, independently of smoothing.</summary>
        /// <value>A finite, nonnegative horizon in seconds. Zero disables extrapolation.</value>
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

        /// <summary>Gets the last supplied Cartesian velocity or ECEF metres per second.</summary>
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
        }

        /// <summary>Clears estimated motion history after a teleport while retaining supplied SDK motion and prediction settings.</summary>
        /// <remarks>Use <see cref="Spatial.ResetPresentation"/> when smoothing history must be reset at the same time.</remarks>
        public void Reset()
        {
            _positionVersion = -1;
            _previousTime = null;
            _hasEstimatedVelocity = false;
            _projectionTime = null;
        }

        internal Double3 Motion { get; private set; }

        internal Double3 Prepare(Spatial spatial, SpatialClock clock, double timestamp)
        {
            ObservePosition(spatial);
            Motion = default(Double3);
            double elapsed = _projectionTime.HasValue ? Math.Max(0d, timestamp - _projectionTime.Value) : 0d;
            _projectionTime = timestamp;
            if (!spatial.HasPosition || !_hasVelocity && !_hasEstimatedVelocity
                || !SpatialMath.IsFinite(_maximumExtrapolation) || _maximumExtrapolation <= 0)
            {
                return spatial.Position;
            }

            double age = clock.Age(spatial.PositionTime, spatial.PositionReceivedTime, timestamp);
            double horizon = Math.Min(age, _maximumExtrapolation);
            // Feed smoothing only the portion of this update that remains inside the prediction horizon.
            // Once an unchanged sample reaches the cap, its presentation must stop advancing from motion alone.
            double step = Math.Min(elapsed, Math.Max(0d, _maximumExtrapolation - Math.Max(0d, age - elapsed)));
            Double3 velocity = _hasVelocity ? _velocity : _estimatedVelocity;
            Double3 acceleration = _hasAcceleration ? _acceleration : default(Double3);
            try
            {
                // Reconcile a separately timed velocity with the position observation's time.
                if (_hasVelocity && _hasAcceleration && spatial.PositionTime.HasValue && _velocityTime.HasValue)
                {
                    velocity += acceleration * spatial.PositionTime.Value.ElapsedSince(_velocityTime.Value);
                }

                Double3 predicted = spatial.Position + velocity * horizon + acceleration * (0.5d * horizon * horizon);
                Motion = velocity * step + acceleration * (step * (horizon - 0.5d * step));
                return predicted;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Extreme finite motion must not poison the raw pose or transform.
                Motion = default(Double3);
                return spatial.Position;
            }
        }

        internal void ResetTime()
        {
            _velocityTime = null;
            _accelerationTime = null;
            Reset();
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

        private void OnDisable()
        {
            Reset();
        }
    }
}
