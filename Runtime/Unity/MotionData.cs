namespace Emas
{
    // Spatial owns accepted SDK motion and history even without a prediction component.
    internal sealed class MotionData
    {
        private Double3 _velocity;
        private Double3 _acceleration;
        private Double3 _angularVelocity;
        private bool _angularVelocityIsWorld;
        private bool _hasVelocity;
        private bool _hasAcceleration;
        private bool _hasAngularVelocity;
        private Timestamp? _velocityTime;
        private Timestamp? _accelerationTime;
        private Timestamp? _angularVelocityTime;
        private long _motionVersion;
        private readonly PoseHistory _history;

        internal Double3 Velocity => _velocity;

        internal Double3 Acceleration => _acceleration;

        internal Double3 AngularVelocity => _angularVelocity;

        internal bool AngularVelocityIsWorld => _angularVelocityIsWorld;

        internal bool HasVelocity => _hasVelocity;

        internal bool HasAcceleration => _hasAcceleration;

        internal bool HasAngularVelocity => _hasAngularVelocity;

        internal Timestamp? VelocityTime => _velocityTime;

        internal Timestamp? AccelerationTime => _accelerationTime;

        internal Timestamp? AngularVelocityTime => _angularVelocityTime;

        internal long Version => _motionVersion;

        internal MotionData(PoseHistory history)
        {
            _history = history;
        }

        internal void ClearVelocity()
        {
            if (_hasVelocity)
            {
                _motionVersion++;
            }

            _hasVelocity = false;
            _velocity = default(Double3);
            _velocityTime = null;
            _history.ClearVelocity();
        }

        internal void ClearAcceleration()
        {
            if (_hasAcceleration)
            {
                _motionVersion++;
            }

            _hasAcceleration = false;
            _acceleration = default(Double3);
            _accelerationTime = null;
            _history.ClearAcceleration();
        }

        internal void ClearAngularVelocity()
        {
            if (_hasAngularVelocity)
            {
                _motionVersion++;
            }

            _hasAngularVelocity = false;
            _angularVelocity = default(Double3);
            _angularVelocityIsWorld = false;
            _angularVelocityTime = null;
            _history.ClearAngularVelocity();
        }

        internal void StoreVelocity(Double3 velocity, Timestamp? sampleTime)
        {
            // Store timestamped motion for playback. Clear that history when input has no timestamp.
            ReferenceFrame.ValidatePosition(velocity, nameof(velocity));
            if (_hasVelocity && !sampleTime.HasValue && !_velocityTime.HasValue && _velocity == velocity)
            {
                return;
            }

            if (PoseData.Accepts(_velocityTime, sampleTime))
            {
                if (!_hasVelocity || _velocity != velocity || sampleTime.HasValue || _velocityTime.HasValue)
                {
                    _motionVersion++;
                }

                _velocity = velocity;
                _velocityTime = sampleTime;
                _hasVelocity = true;
                PoseHistory history = _history;
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

        internal void StoreAcceleration(Double3 acceleration, Timestamp? sampleTime)
        {
            // Store acceleration separately so later acceleration cannot change predictions from older poses.
            ReferenceFrame.ValidatePosition(acceleration, nameof(acceleration));
            if (_hasAcceleration && !sampleTime.HasValue && !_accelerationTime.HasValue && _acceleration == acceleration)
            {
                return;
            }

            if (PoseData.Accepts(_accelerationTime, sampleTime))
            {
                if (!_hasAcceleration || _acceleration != acceleration || sampleTime.HasValue || _accelerationTime.HasValue)
                {
                    _motionVersion++;
                }

                _acceleration = acceleration;
                _accelerationTime = sampleTime;
                _hasAcceleration = true;
                PoseHistory history = _history;
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

        internal void StoreAngularVelocity(Double3 angularVelocity, bool world, Timestamp? sampleTime)
        {
            // Check angular-rate times separately so an old rate cannot replace a newer one.
            if (!PoseData.Accepts(_angularVelocityTime, sampleTime))
            {
                return;
            }

            if (_hasAngularVelocity && !sampleTime.HasValue && !_angularVelocityTime.HasValue
                && _angularVelocity == angularVelocity && _angularVelocityIsWorld == world)
            {
                return;
            }

            PoseHistory history = _history;
            if (!_hasAngularVelocity || _angularVelocity != angularVelocity || _angularVelocityIsWorld != world
                || sampleTime.HasValue || _angularVelocityTime.HasValue)
            {
                _motionVersion++;
            }

            // Clear stored samples when switching between body and world axes or when a timestamp is missing.
            if (_angularVelocityIsWorld != world || !sampleTime.HasValue)
            {
                history.ClearAngularVelocity();
            }

            _angularVelocity = angularVelocity;
            _angularVelocityIsWorld = world;
            _angularVelocityTime = sampleTime;
            _hasAngularVelocity = true;
            if (sampleTime.HasValue)
            {
                history.AddAngularVelocity(angularVelocity, sampleTime.Value);
            }
        }

        internal void ResetTime()
        {
            // Keep supplied values across a clock restart, but accept fresh timestamps.
            _velocityTime = null;
            _accelerationTime = null;
            _angularVelocityTime = null;
            _motionVersion++;
        }
    }
}
