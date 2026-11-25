using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Observation filter that smooths source pose before extrapolation.</summary>
    /// <remarks>
    /// Place beside Spatial; no reader binding is required. Position and rotation filter independently, using
    /// buffered poses when configured. Timed SDK velocity supplied through Spatial compensates linear motion
    /// between observations even when gap extrapolation is disabled.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Spatial))]
    [AddComponentMenu("Emas/Smoothing")]
    public sealed class Smoothing : Filter
    {
        [Tooltip("Seconds to halve position correction error. Zero applies immediately; start at 0.08 s. Larger values remove more jitter but increase lag without SDK motion.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _positionHalfLife = 0.08f;
        [Tooltip("Seconds to halve angular correction error. Zero applies immediately (default). Larger values smooth more but delay turns, including scene movement when following this Ghost's orientation.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _rotationHalfLife;
        private PoseSmoother _smoother;
        private FilterState _previous;
        private FilterState _input;
        private double _positionChangedAt;
        private double _rotationChangedAt;
        private Double3 _estimatedWorldRate;
        private bool _hasEstimatedWorldRate;

        /// <summary>Gets or sets seconds to halve position error; zero applies the incoming presentation position directly.</summary>
        /// <value>A finite, nonnegative half-life in seconds; the default is 0.08 seconds.</value>
        /// <remarks>
        /// Timed SDK motion compensates movement while measurement error remains filtered; without it, moving inputs
        /// trail. Live edits, including zero and enabled changes, retain history and blend their presentation difference
        /// over 0.25 unscaled seconds. The Inspector offers 0-0.5 seconds; code also accepts larger finite values.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The assigned half-life is negative, NaN or infinite.</exception>
        public float PositionHalfLife
        {
            get => _positionHalfLife;
            set
            {
                ValidateTime(value, nameof(value));
                _positionHalfLife = value;
            }
        }

        /// <summary>Gets or sets seconds to halve angular error; zero applies orientation immediately (the default).</summary>
        /// <value>A finite, nonnegative rotation half-life in seconds.</value>
        /// <remarks>Controls reference orientation and scene repositioning when this Ghost is followed with FollowRotation enabled.
        /// Supplied angular motion advances the observation estimate. Without supplied motion, the effective
        /// filtered timestamp records its age so prediction can compensate using filtered pose history.
        /// Live edits retain history and blend the setting-induced rotation over 0.25 seconds of real time,
        /// independently of position. Zero applies orientation directly after this transition.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The assigned half-life is negative, NaN or infinite.</exception>
        public float RotationHalfLife
        {
            get => _rotationHalfLife;
            set
            {
                ValidateTime(value, nameof(value));
                _rotationHalfLife = value;
            }
        }

        /// <summary>Gets the current half-life configuration for live filter transitions.</summary>
        /// <value>A hash of the two channel half-lives.</value>
        protected override int ConfigurationVersion => unchecked(_positionHalfLife.GetHashCode() * 397 ^ _rotationHalfLife.GetHashCode());

        internal void ResetRotation()
        {
            _smoother.ResetRotation();
            _previous.Rotation = null;
            _previous.RotationTime = null;
            _hasEstimatedWorldRate = false;
        }

        internal static void ValidateTime(float value, string parameter)
        {
            if (!SpatialMath.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameter, "The time must be finite and nonnegative.");
            }
        }

        /// <summary>Filters observation error before prediction, preserving each estimate's effective SDK time.</summary>
        /// <param name="input">Accepted source data or the preceding filter's estimate.</param>
        /// <param name="context">Processing time and source conventions.</param>
        /// <returns>Smoothed pose observations with original motion inputs and coherent sample times.</returns>
        protected override FilterState Process(FilterState input, FilterContext context)
        {
            _smoother.Prepare(context.Time);
            FilterState output = input;
            if (input.Position.HasValue)
            {
                bool changed = !Nullable.Equals(input.Position, _input.Position) || !Nullable.Equals(input.PositionTime, _input.PositionTime);
                bool motionAvailable = Eligible(input.Velocity, input.VelocityTime, input.PositionTime);
                double step = Interval(input.PositionTime, _previous.PositionTime, changed, context.Time - _positionChangedAt);
                Double3 velocity = input.Velocity.GetValueOrDefault();
                if (motionAvailable && Eligible(_input.Velocity, _input.VelocityTime, _input.PositionTime))
                {
                    // A supplied change of velocity describes a maneuver, not measurement error.
                    // Trapezoidal integration is exact for constant inertial acceleration.
                    velocity = (velocity + _input.Velocity.Value) * 0.5d;
                }
                else if (motionAvailable && Eligible(input.Acceleration, input.AccelerationTime, input.PositionTime))
                {
                    velocity -= input.Acceleration.Value * (0.5d * step);
                }

                Double3 motion = motionAvailable && _previous.Position.HasValue ? velocity * step : default(Double3);
                output.Position = _smoother.Position(input.Position.Value, _positionHalfLife, motion);
                output.PositionTime = motionAvailable ? input.PositionTime : EstimateTime(_previous.PositionTime,
                    input.PositionTime, Weight(context.DeltaTime, _positionHalfLife), _previous.Position.HasValue);
                if (changed)
                {
                    _positionChangedAt = context.Time;
                }
            }

            if (input.Rotation.HasValue)
            {
                bool changed = !Nullable.Equals(input.Rotation, _input.Rotation) || !Nullable.Equals(input.RotationTime, _input.RotationTime);
                bool motionAvailable = Eligible(input.AngularVelocity, input.AngularVelocityTime, input.RotationTime);
                if (motionAvailable || _rotationHalfLife <= 0)
                {
                    _hasEstimatedWorldRate = false;
                }

                bool estimatedMotion = _rotationHalfLife > 0 && !motionAvailable && input.RotationTime.HasValue && _input.RotationTime.HasValue
                    && _input.Rotation.HasValue && input.RotationTime.Value.CompareTo(_input.RotationTime.Value) > 0;
                if (estimatedMotion)
                {
                    double sampleStep = input.RotationTime.Value.ElapsedSince(_input.RotationTime.Value);
                    Double3 rate = SpatialMath.RotationRate(_input.Rotation.Value, input.Rotation.Value, sampleStep);
                    double weight = Weight(sampleStep, _rotationHalfLife);
                    // Estimate and filter angular motion together with attitude. Backdating a
                    // low-pass attitude alone would amplify a measurement step into turn acceleration.
                    _estimatedWorldRate = _hasEstimatedWorldRate
                        ? _estimatedWorldRate * (1d - weight) + rate * weight : rate;
                    _hasEstimatedWorldRate = true;
                }

                bool useEstimate = _rotationHalfLife > 0 && !motionAvailable && _hasEstimatedWorldRate;
                double step = Interval(input.RotationTime, _previous.RotationTime, changed, context.Time - _rotationChangedAt);
                FilterState angular = input;
                angular.Rotation = _previous.Rotation ?? input.Rotation;
                Quaternion motion = (motionAvailable || useEstimate) && _previous.Rotation.HasValue
                    ? SpatialMath.AngularDisplacement(useEstimate ? _estimatedWorldRate : context.WorldAngularVelocity(angular), step) : Quaternion.identity;
                output.Rotation = _smoother.Rotation(input.Rotation.Value, _rotationHalfLife, motion);
                output.RotationTime = motionAvailable || useEstimate ? input.RotationTime : EstimateTime(_previous.RotationTime,
                    input.RotationTime, Weight(context.DeltaTime, _rotationHalfLife), _previous.Rotation.HasValue);
                if (useEstimate)
                {
                    output.AngularVelocity = context.BodyAngularVelocity(output.Rotation.Value, _estimatedWorldRate);
                    output.AngularVelocityIsWorld = false;
                    output.AngularVelocityTime = output.RotationTime;
                }

                if (changed)
                {
                    _rotationChangedAt = context.Time;
                }
            }

            // A low-pass pose without measured motion represents an older point on the path.
            // Keep that effective time so prediction includes the filter's age automatically.
            _input = input;
            _previous = output;
            return output;
        }

        /// <summary>Clears both pose estimates and their timestamp histories.</summary>
        protected override void OnReset()
        {
            _smoother.Reset();
            _previous = default(FilterState);
            _input = default(FilterState);
            _positionChangedAt = 0;
            _rotationChangedAt = 0;
            _hasEstimatedWorldRate = false;
            _estimatedWorldRate = default(Double3);
        }

        private static bool Eligible(Double3? motion, Timestamp? motionTime, Timestamp? poseTime)
        {
            return motion.HasValue && (!motionTime.HasValue || !poseTime.HasValue || motionTime.Value.CompareTo(poseTime.Value) <= 0);
        }

        private static double Interval(Timestamp? current, Timestamp? previous, bool changed, double elapsed)
        {
            return current.HasValue && previous.HasValue ? Math.Max(0d, current.Value.ElapsedSince(previous.Value))
                : changed ? Math.Max(0d, elapsed) : 0;
        }

        private static double Weight(double elapsed, double halfLife)
        {
            return halfLife > 0 ? 1d - Math.Exp(-0.6931471805599453d * elapsed / halfLife) : 1d;
        }

        private static Timestamp? EstimateTime(Timestamp? previous, Timestamp? current, double weight, bool initialized)
        {
            return initialized && previous.HasValue && current.HasValue
                ? previous.Value.OffsetBy(current.Value.ElapsedSince(previous.Value) * weight) : current;
        }
    }
}
