using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Keeps filtered observations and their history separate from accepted SDK inputs.
    internal sealed class PredictionInput
    {
        private readonly struct Observation
        {
            internal readonly int Channel;
            internal readonly Timestamp Time;
            internal readonly Double3 Vector;
            internal readonly Quaternion Rotation;

            internal Observation(int channel, Timestamp time, Double3 vector, Quaternion rotation)
            {
                Channel = channel;
                Time = time;
                Vector = vector;
                Rotation = rotation;
            }
        }

        private static readonly Comparison<Observation> _compare = Compare;
        private readonly List<Observation> _pending = new List<Observation>();
        private readonly Timestamp?[] _seen = new Timestamp?[5];
        private FilterState _source;
        private bool _initialized;
        private readonly Spatial _spatial;
        private readonly List<Filter> _filters = new List<Filter>();
        private PoseHistory _history;
        private MotionData _motion;
        private FilterState _state;
        private bool _filtering;
        private int _signature;
        private long _positionVersion;
        private long _rotationVersion;
        private long _rawPositionVersion = -1;
        private long _rawRotationVersion = -1;

        internal Spatial Spatial => _spatial;
        internal Ghost Ghost => _spatial.Ghost;
        internal RotationSpace RotationSpace => _spatial.RotationSpace;
        internal CoordinateSystem? EarthCenteredBodyAxes => _spatial.EarthCenteredBodyAxes;
        internal bool HasPosition => _filtering ? _state.Position.HasValue : _spatial.HasPosition;
        internal bool HasRotation => _filtering ? _state.Rotation.HasValue : _spatial.HasRotation;
        internal Double3 Position => _filtering ? _state.Position.GetValueOrDefault() : _spatial.Position;
        internal Quaternion Rotation => _filtering ? _state.Rotation ?? Quaternion.identity : _spatial.Rotation;
        internal Timestamp? PositionTime => _filtering ? _state.PositionTime : _spatial.PositionTime;
        internal Timestamp? RotationTime => _filtering ? _state.RotationTime : _spatial.RotationTime;
        internal double PositionReceivedTime => _spatial.PositionReceivedTime;
        internal double RotationReceivedTime => _spatial.RotationReceivedTime;
        internal long PositionVersion => _filtering ? _positionVersion : _spatial.PositionVersion;
        internal long RotationVersion => _filtering ? _rotationVersion : _spatial.RotationVersion;
        internal PoseHistory History => _filtering ? _history : _spatial.History;
        internal MotionData Motion => _filtering ? _motion : _spatial.Motion;
        internal int Signature => _signature;

        internal PredictionInput(Spatial spatial)
        {
            _spatial = spatial;
        }

        internal bool Refresh(CoordinateSystem axes, double timestamp)
        {
            _spatial.GetComponents(_filters);
            int signature = 0;
            bool filtering = false;
            for (int index = 0; index < _filters.Count; index++)
            {
                Filter filter = _filters[index];
                if (Enabled(filter))
                {
                    filtering = true;
                    signature = unchecked((signature * 397 ^ filter.GetInstanceID()) * 397 ^ filter.SettingsVersion);
                }
            }

            bool changed = filtering != _filtering || signature != _signature;
            if (changed && _history != null)
            {
                // Do not fit across a change of estimator, order or configuration.
                _history.Clear();
                _motion = new MotionData(_history);
            }

            _signature = signature;
            _filtering = filtering;
            if (!filtering)
            {
                for (int index = 0; index < _filters.Count; index++)
                {
                    _filters[index].Deactivate();
                }

                _initialized = false;
                _source = default(FilterState);
                Array.Clear(_seen, 0, _seen.Length);
                return changed;
            }

            if (_history == null)
            {
                _history = new PoseHistory();
                _motion = new MotionData(_history);
            }

            if (!_initialized)
            {
                // Replaying accepted batches preserves interpolation and derivative history.
                // Samples with the same SDK timestamp enter the filters as one coherent state.
                for (int index = 0; index < _filters.Count; index++)
                {
                    _filters[index].ClearState();
                }

                _initialized = true;
            }

            MotionData rawMotion = _spatial.HasMotionData ? _spatial.Motion : null;
            bool angularWorld = rawMotion != null && rawMotion.AngularVelocityIsWorld;
            if (_source.AngularVelocityIsWorld != angularWorld)
            {
                // A newly selected angular frame cannot relabel an older value while
                // replaying pose samples that precede the new rate's creation time.
                _source.AngularVelocity = null;
                _source.AngularVelocityTime = null;
                _seen[4] = null;
            }

            _source.AngularVelocityIsWorld = angularWorld;
            _pending.Clear();
            CollectPosition(_spatial.HasPosition, _spatial.PositionTime, _spatial.HasPosition ? _spatial.Position : default(Double3));
            CollectRotation(_spatial.HasRotation, _spatial.RotationTime, _spatial.HasRotation ? _spatial.Rotation : default(Quaternion));
            CollectVelocity(rawMotion != null && rawMotion.HasVelocity, rawMotion?.VelocityTime, rawMotion != null && rawMotion.HasVelocity ? rawMotion.Velocity : default(Double3));
            CollectAcceleration(rawMotion != null && rawMotion.HasAcceleration, rawMotion?.AccelerationTime, rawMotion != null && rawMotion.HasAcceleration ? rawMotion.Acceleration : default(Double3));
            CollectAngularVelocity(rawMotion != null && rawMotion.HasAngularVelocity, rawMotion?.AngularVelocityTime, rawMotion != null && rawMotion.HasAngularVelocity ? rawMotion.AngularVelocity : default(Double3));

            _pending.Sort(_compare);
            int cursor = 0;
            Timestamp? newest = _pending.Count > 0 ? _pending[_pending.Count - 1].Time : (Timestamp?)null;
            while (cursor < _pending.Count)
            {
                Timestamp time = _pending[cursor].Time;
                do
                {
                    Observation sample = _pending[cursor++];
                    switch (sample.Channel)
                    {
                        case 0:
                            _source.Position = sample.Vector;
                            _source.PositionTime = time;
                            break;
                        case 1:
                            _source.Rotation = sample.Rotation;
                            _source.RotationTime = time;
                            break;
                        case 2:
                            _source.Velocity = sample.Vector;
                            _source.VelocityTime = time;
                            break;
                        case 3:
                            _source.Acceleration = sample.Vector;
                            _source.AccelerationTime = time;
                            break;
                        case 4:
                            _source.AngularVelocity = sample.Vector;
                            _source.AngularVelocityTime = time;
                            break;
                    }

                    _seen[sample.Channel] = time;
                }
                while (cursor < _pending.Count && _pending[cursor].Time.Equals(time));

                Apply(_source, axes, timestamp + time.ElapsedSince(newest.Value));
            }

            if (!newest.HasValue)
            {
                // Held packets still allow correction error to decay, without adding model motion.
                Apply(_source, axes, timestamp);
            }

            return changed;
        }

        internal GeographicBasis BasisAt(Double3 position)
        {
            return _spatial.BasisAt(position);
        }

        internal void Reset()
        {
            for (int index = 0; index < _filters.Count; index++)
            {
                if (_filters[index] != null)
                {
                    _filters[index].ClearState();
                }
            }

            _history?.Clear();
            if (_history != null)
            {
                _motion = new MotionData(_history);
            }

            _state = default(FilterState);
            _source = default(FilterState);
            _initialized = false;
            Array.Clear(_seen, 0, _seen.Length);
            _rawPositionVersion = -1;
            _rawRotationVersion = -1;
        }
        private void Apply(FilterState state, CoordinateSystem axes, double timestamp)
        {
            for (int index = 0; index < _filters.Count; index++)
            {
                Filter filter = _filters[index];
                if (Enabled(filter))
                {
                    try
                    {
                        state = filter.Apply(state, _spatial, axes, timestamp);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        filter.ClearState();
                    }
                }
                else
                {
                    filter.Deactivate();
                }
            }

            bool positionChanged = _rawPositionVersion != _spatial.PositionVersion
                || (state.Position.HasValue && state.PositionTime.HasValue && !_history.NewestPositionTime.HasValue)
                || !Nullable.Equals(_state.Position, state.Position) || !Nullable.Equals(_state.PositionTime, state.PositionTime);
            bool rotationChanged = _rawRotationVersion != _spatial.RotationVersion
                || (state.Rotation.HasValue && state.RotationTime.HasValue && !_history.NewestRotationTime.HasValue)
                || !Nullable.Equals(_state.Rotation, state.Rotation) || !Nullable.Equals(_state.RotationTime, state.RotationTime);
            if (positionChanged)
            {
                _positionVersion++;
                if (state.Position.HasValue && state.PositionTime.HasValue)
                {
                    if (_history.NewestPositionTime.HasValue && state.PositionTime.Value.CompareTo(_history.NewestPositionTime.Value) < 0)
                    {
                        // A retimed estimate restarts its channel instead of mixing future history into it.
                        _history.ClearPosition();
                    }

                    _history.UpdatePosition(state.Position.Value, state.PositionTime.Value);
                }
                else
                {
                    _history.ClearPosition();
                }
            }

            if (rotationChanged)
            {
                _rotationVersion++;
                if (state.Rotation.HasValue && state.RotationTime.HasValue)
                {
                    if (_history.NewestRotationTime.HasValue && state.RotationTime.Value.CompareTo(_history.NewestRotationTime.Value) < 0)
                    {
                        _history.ClearRotation();
                    }

                    _history.UpdateRotation(state.Rotation.Value, state.RotationTime.Value);
                }
                else
                {
                    _history.ClearRotation();
                }
            }

            _motion.ApplyFilter(state);
            _state = state;
            _rawPositionVersion = _spatial.PositionVersion;
            _rawRotationVersion = _spatial.RotationVersion;
        }

        private void CollectPosition(bool available, Timestamp? time, Double3 value)
        {
            if (!available || !time.HasValue)
            {
                _source.Position = available ? value : (Double3?)null;
                _source.PositionTime = null;
                _seen[0] = null;
                return;
            }

            bool found = false;
            for (int lookback = 0; _spatial.History.ReadPosition(lookback, null, out Timestamp sampleTime, out Double3 sample); lookback++)
            {
                if (_seen[0].HasValue && sampleTime.CompareTo(_seen[0].Value) <= 0)
                {
                    break;
                }

                found = true;
                _pending.Add(new Observation(0, sampleTime, sample, Quaternion.identity));
            }

            if (!found && (!_seen[0].HasValue || time.Value.CompareTo(_seen[0].Value) > 0))
            {
                _pending.Add(new Observation(0, time.Value, value, Quaternion.identity));
            }
        }

        private void CollectRotation(bool available, Timestamp? time, Quaternion value)
        {
            if (!available || !time.HasValue)
            {
                _source.Rotation = available ? value : (Quaternion?)null;
                _source.RotationTime = null;
                _seen[1] = null;
                return;
            }

            bool found = false;
            for (int lookback = 0; _spatial.History.ReadRotation(lookback, null, out Timestamp sampleTime, out Quaternion sample); lookback++)
            {
                if (_seen[1].HasValue && sampleTime.CompareTo(_seen[1].Value) <= 0)
                {
                    break;
                }

                found = true;
                _pending.Add(new Observation(1, sampleTime, default(Double3), sample));
            }

            if (!found && (!_seen[1].HasValue || time.Value.CompareTo(_seen[1].Value) > 0))
            {
                _pending.Add(new Observation(1, time.Value, default(Double3), value));
            }
        }

        private void CollectVelocity(bool available, Timestamp? time, Double3 value)
        {
            if (!available || !time.HasValue)
            {
                _source.Velocity = available ? value : (Double3?)null;
                _source.VelocityTime = null;
                _seen[2] = null;
                return;
            }

            bool found = false;
            for (int lookback = 0; _spatial.History.ReadVelocity(lookback, null, out Timestamp sampleTime, out Double3 sample); lookback++)
            {
                if (_seen[2].HasValue && sampleTime.CompareTo(_seen[2].Value) <= 0)
                {
                    break;
                }

                found = true;
                _pending.Add(new Observation(2, sampleTime, sample, Quaternion.identity));
            }

            if (!found && (!_seen[2].HasValue || time.Value.CompareTo(_seen[2].Value) > 0))
            {
                _pending.Add(new Observation(2, time.Value, value, Quaternion.identity));
            }
        }

        private void CollectAcceleration(bool available, Timestamp? time, Double3 value)
        {
            if (!available || !time.HasValue)
            {
                _source.Acceleration = available ? value : (Double3?)null;
                _source.AccelerationTime = null;
                _seen[3] = null;
                return;
            }

            bool found = false;
            for (int lookback = 0; _spatial.History.ReadAcceleration(lookback, null, out Timestamp sampleTime, out Double3 sample); lookback++)
            {
                if (_seen[3].HasValue && sampleTime.CompareTo(_seen[3].Value) <= 0)
                {
                    break;
                }

                found = true;
                _pending.Add(new Observation(3, sampleTime, sample, Quaternion.identity));
            }

            if (!found && (!_seen[3].HasValue || time.Value.CompareTo(_seen[3].Value) > 0))
            {
                _pending.Add(new Observation(3, time.Value, value, Quaternion.identity));
            }
        }

        private void CollectAngularVelocity(bool available, Timestamp? time, Double3 value)
        {
            if (!available || !time.HasValue)
            {
                _source.AngularVelocity = available ? value : (Double3?)null;
                _source.AngularVelocityTime = null;
                _seen[4] = null;
                return;
            }

            bool found = false;
            for (int lookback = 0; _spatial.History.ReadAngularVelocity(lookback, null, out Timestamp sampleTime, out Double3 sample); lookback++)
            {
                if (_seen[4].HasValue && sampleTime.CompareTo(_seen[4].Value) <= 0)
                {
                    break;
                }

                found = true;
                _pending.Add(new Observation(4, sampleTime, sample, Quaternion.identity));
            }

            if (!found && (!_seen[4].HasValue || time.Value.CompareTo(_seen[4].Value) > 0))
            {
                _pending.Add(new Observation(4, time.Value, value, Quaternion.identity));
            }
        }

        private static int Compare(Observation first, Observation second)
        {
            int time = first.Time.CompareTo(second.Time);
            return time != 0 ? time : first.Channel.CompareTo(second.Channel);
        }

        private static bool Enabled(Filter filter)
        {
            if (!filter.enabled)
            {
                return false;
            }

            Smoothing smoothing = filter as Smoothing;
            if (smoothing != null)
            {
                return smoothing.PositionHalfLife > 0 || smoothing.RotationHalfLife > 0;
            }

            PacketCorrection correction = filter as PacketCorrection;
            return correction == null || correction.Duration > 0;
        }
    }
}
