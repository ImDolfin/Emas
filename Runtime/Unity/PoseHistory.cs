using System;
using UnityEngine;

namespace Emas
{
    // Store each SDK input separately. Keep full timestamp precision.
    // Each circular buffer holds 128 samples. Reading and adding samples need no new memory.
    internal sealed class PoseHistory
    {
        private readonly struct RotationSample
        {
            internal readonly Quaternion Value;
            internal readonly Timestamp Time;

            internal RotationSample(Quaternion value, Timestamp time)
            {
                Value = value;
                Time = time;
            }
        }

        private readonly struct VectorSample
        {
            internal readonly Double3 Value;
            internal readonly Timestamp Time;

            internal VectorSample(Double3 value, Timestamp time)
            {
                Value = value;
                Time = time;
            }
        }

        private sealed class VectorHistory
        {
            private readonly VectorSample[] _samples = new VectorSample[Capacity];
            private int _start;
            private int _count;

            internal Timestamp? NewestTime => _count == 0 ? (Timestamp?)null : At(_count - 1).Time;
            internal Timestamp? OldestTime => _count == 0 ? (Timestamp?)null : At(0).Time;

            internal void Add(Double3 value, Timestamp time)
            {
                // Accept only newer samples. If the buffer is full, replace its oldest sample.
                if (_count > 0 && time.CompareTo(At(_count - 1).Time) <= 0)
                {
                    return;
                }

                int index = (_start + _count) % Capacity;
                _samples[index] = new VectorSample(value, time);
                if (_count == Capacity)
                {
                    _start = (_start + 1) % Capacity;
                }
                else
                {
                    _count++;
                }
            }

            internal void Update(Double3 value, Timestamp time)
            {
                if (_count > 0 && time.CompareTo(At(_count - 1).Time) == 0)
                {
                    _samples[(_start + _count - 1) % Capacity] = new VectorSample(value, time);
                    return;
                }

                Add(value, time);
            }

            internal bool Sample(double offsetFromNewest, out Double3 value)
            {
                value = default(Double3);
                if (_count == 0 || !SpatialMath.IsFinite(offsetFromNewest))
                {
                    return false;
                }

                // Outside the stored time range, hold the nearest sample. Interpolate only between stored samples.
                VectorSample newest = At(_count - 1);
                value = newest.Value;
                if (_count == 1 || offsetFromNewest >= 0)
                {
                    return true;
                }

                VectorSample oldest = At(0);
                if (offsetFromNewest <= oldest.Time.ElapsedSince(newest.Time))
                {
                    value = oldest.Value;
                    return true;
                }

                // Search newest samples first because playback usually needs a recent pair.
                VectorSample after = newest;
                for (int index = _count - 2; index >= 0; index--)
                {
                    VectorSample before = At(index);
                    double beforeOffset = before.Time.ElapsedSince(newest.Time);
                    if (offsetFromNewest >= beforeOffset)
                    {
                        double weight = (offsetFromNewest - beforeOffset) / after.Time.ElapsedSince(before.Time);
                        value = Interpolate(before.Value, after.Value, weight);
                        return true;
                    }

                    after = before;
                }

                value = oldest.Value;
                return true;
            }

            internal void Clear()
            {
                // Reset the sample count to empty the buffer. The old array values can stay in place.
                _start = 0;
                _count = 0;
            }

            internal bool Read(int lookback, Timestamp? through, out Timestamp time, out Double3 value, Timestamp? since = null)
            {
                // Address retained raw samples without interpolation or future-only input.
                int index = _count - 1;
                while (index >= 0 && through.HasValue && At(index).Time.CompareTo(through.Value) > 0)
                {
                    index--;
                }

                index -= lookback;
                time = default(Timestamp);
                value = default(Double3);
                if (index < 0 || (since.HasValue && At(index).Time.CompareTo(since.Value) < 0))
                {
                    return false;
                }

                VectorSample sample = At(index);
                time = sample.Time;
                value = sample.Value;
                return true;
            }

            internal bool Integrate(double start, double end, out Double3 value)
            {
                value = default(Double3);
                if (_count == 0 || !SpatialMath.IsFinite(start) || !SpatialMath.IsFinite(end))
                {
                    return false;
                }

                if (start == end)
                {
                    return true;
                }

                // Put the earlier time first so the same calculation can sum motion forward or backward.
                bool reverse = start > end;
                try
                {
                    value = reverse ? Integral(end, start) * -1d : Integral(start, end);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Even valid time and velocity values can produce movement too large to store.
                    value = default(Double3);
                    return false;
                }
            }

            private Double3 Integral(double start, double end)
            {
                VectorSample newest = At(_count - 1);
                VectorSample after = newest;
                double afterOffset = 0;
                Double3 result = default(Double3);
                if (end > 0)
                {
                    // After the last timestamp, hold the latest velocity. Prediction from
                    // acceleration, when enabled, is added separately by the strategy.
                    double tailStart = Math.Max(start, 0d);
                    result = newest.Value * (end - tailStart);
                    end = tailStart;
                }

                // Search only the samples crossed during this update. Most frames cross
                // few samples, but a slow frame may need to sum motion across many samples.
                for (int index = _count - 2; index >= 0 && end > start; index--)
                {
                    VectorSample before = At(index);
                    double beforeOffset = before.Time.ElapsedSince(newest.Time);
                    if (end > beforeOffset)
                    {
                        double segmentStart = Math.Max(start, beforeOffset);
                        result += IntegralSegment(before.Value, after.Value,
                            beforeOffset, afterOffset, segmentStart, end);
                        end = segmentStart;
                    }

                    after = before;
                    afterOffset = beforeOffset;
                }

                if (end > start)
                {
                    // Before the earliest stored time, hold the oldest available value.
                    result += At(0).Value * (end - start);
                }

                return result;
            }

            private static Double3 IntegralSegment(Double3 before, Double3 after, double beforeOffset,
                double afterOffset, double start, double end)
            {
                // Velocity changes linearly between samples. A trapezoid gives the exact movement over any part of that interval.
                double duration = afterOffset - beforeOffset;
                Double3 startValue = Interpolate(before, after, (start - beforeOffset) / duration);
                Double3 endValue = Interpolate(before, after, (end - beforeOffset) / duration);
                return (startValue * 0.5d + endValue * 0.5d) * (end - start);
            }

            private VectorSample At(int index)
            {
                return _samples[(_start + index) % Capacity];
            }

            private static Double3 Interpolate(Double3 before, Double3 after, double weight)
            {
                if (weight <= 0)
                {
                    return before;
                }

                if (weight >= 1)
                {
                    return after;
                }

                return new Double3(Coordinate(before.X, after.X, weight),
                    Coordinate(before.Y, after.Y, weight), Coordinate(before.Z, after.Z, weight));
            }

            private static double Coordinate(double before, double after, double weight)
            {
                // Subtract nearby large coordinates before scaling, but avoid overflowing the
                // difference when the endpoints are extremely large and have opposite signs.
                double difference = after - before;
                return SpatialMath.IsFinite(difference)
                    ? before + difference * weight : before * (1d - weight) + after * weight;
            }
        }

        private const int Capacity = 128;
        private readonly VectorHistory _positions = new VectorHistory();
        private readonly VectorHistory _velocities = new VectorHistory();
        private readonly VectorHistory _accelerations = new VectorHistory();
        private readonly VectorHistory _angularVelocities = new VectorHistory();
        private readonly RotationSample[] _rotations = new RotationSample[Capacity];
        private int _rotationStart;
        private int _rotationCount;
        private Timestamp? _positionEstimateStart;
        private Timestamp? _velocityEstimateStart;
        private Timestamp? _rotationEstimateStart;
        private Timestamp? _angularEstimateStart;

        internal Timestamp? NewestPositionTime => _positions.NewestTime;

        internal Timestamp? OldestPositionTime => _positions.OldestTime;

        internal long Version { get; private set; }

        internal Timestamp? NewestRotationTime => _rotationCount == 0
            ? (Timestamp?)null : RotationAt(_rotationCount - 1).Time;

        internal Timestamp? OldestRotationTime => _rotationCount == 0
            ? (Timestamp?)null : RotationAt(0).Time;

        internal Timestamp? NewestVelocityTime => _velocities.NewestTime;

        internal Timestamp? OldestVelocityTime => _velocities.OldestTime;

        internal Timestamp? NewestAccelerationTime => _accelerations.NewestTime;

        internal Timestamp? OldestAccelerationTime => _accelerations.OldestTime;

        internal Timestamp? NewestAngularVelocityTime => _angularVelocities.NewestTime;

        internal Timestamp? OldestAngularVelocityTime => _angularVelocities.OldestTime;

        internal void UpdatePosition(Double3 value, Timestamp time)
        {
            Version++;
            _positions.Update(value, time);
        }

        internal void UpdateVelocity(Double3 value, Timestamp time)
        {
            Version++;
            _velocities.Update(value, time);
        }

        internal void UpdateAcceleration(Double3 value, Timestamp time)
        {
            Version++;
            _accelerations.Update(value, time);
        }

        internal void UpdateAngularVelocity(Double3 value, Timestamp time)
        {
            Version++;
            _angularVelocities.Update(value, time);
        }

        internal void UpdateRotation(Quaternion value, Timestamp time)
        {
            if (_rotationCount > 0 && time.CompareTo(RotationAt(_rotationCount - 1).Time) == 0)
            {
                Version++;
                _rotations[(_rotationStart + _rotationCount - 1) % Capacity] = new RotationSample(value, time);
                return;
            }

            AddRotation(value, time);
        }

        internal void AddPosition(Double3 position, Timestamp time)
        {
            Version++;
            _positions.Add(position, time);
        }

        internal void AddRotation(Quaternion rotation, Timestamp time)
        {
            Version++;
            if (_rotationCount > 0 && time.CompareTo(RotationAt(_rotationCount - 1).Time) <= 0)
            {
                return;
            }

            int index = (_rotationStart + _rotationCount) % Capacity;
            _rotations[index] = new RotationSample(rotation, time);
            if (_rotationCount == Capacity)
            {
                _rotationStart = (_rotationStart + 1) % Capacity;
            }
            else
            {
                _rotationCount++;
            }
        }

        internal void AddVelocity(Double3 velocity, Timestamp time)
        {
            Version++;
            _velocities.Add(velocity, time);
        }

        internal void AddAcceleration(Double3 acceleration, Timestamp time)
        {
            Version++;
            _accelerations.Add(acceleration, time);
        }

        internal void AddAngularVelocity(Double3 angularVelocity, Timestamp time)
        {
            Version++;
            _angularVelocities.Add(angularVelocity, time);
        }

        internal bool ReadPosition(int lookback, Timestamp? through, out Timestamp time, out Double3 value)
        {
            return _positions.Read(lookback, through, out time, out value, _positionEstimateStart);
        }

        internal bool ReadAcceleration(int lookback, Timestamp? through, out Timestamp time, out Double3 acceleration)
        {
            return _accelerations.Read(lookback, through, out time, out acceleration);
        }

        internal bool ReadVelocity(int lookback, Timestamp? through, out Timestamp time, out Double3 value)
        {
            return _velocities.Read(lookback, through, out time, out value, _velocityEstimateStart);
        }

        internal bool ReadAngularVelocity(int lookback, Timestamp? through, out Timestamp time, out Double3 value)
        {
            return _angularVelocities.Read(lookback, through, out time, out value, _angularEstimateStart);
        }

        internal bool ReadRotation(int lookback, Timestamp? through, out Timestamp time, out Quaternion value)
        {
            int index = _rotationCount - 1;
            while (index >= 0 && through.HasValue && RotationAt(index).Time.CompareTo(through.Value) > 0)
            {
                index--;
            }

            index -= lookback;
            time = default(Timestamp);
            value = Quaternion.identity;
            if (index < 0 || (_rotationEstimateStart.HasValue && RotationAt(index).Time.CompareTo(_rotationEstimateStart.Value) < 0))
            {
                return false;
            }

            RotationSample sample = RotationAt(index);
            time = sample.Time;
            value = sample.Value;
            return true;
        }

        internal void RestartEstimation()
        {
            Version++;
            // Reset derivative fits without discarding independently timed SDK data needed
            // for playback or interpolation. The latest sample seeds each new fitting window.
            _positionEstimateStart = NewestPositionTime;
            _velocityEstimateStart = NewestVelocityTime;
            _rotationEstimateStart = NewestRotationTime;
            _angularEstimateStart = NewestAngularVelocityTime;
        }

        // Offsets are playback time minus the newest sample time. Negative offsets read
        // stored samples. Positive offsets hold the latest value so the caller can predict from it.
        internal bool SamplePosition(double offsetFromNewest, out Double3 position)
        {
            return _positions.Sample(offsetFromNewest, out position);
        }

        internal bool SampleVelocity(double offsetFromNewest, out Double3 velocity)
        {
            return _velocities.Sample(offsetFromNewest, out velocity);
        }

        // Sum movement along the same velocity curve used for sampling, including every
        // crossed sample. Looking only at the endpoints would miss turns during a slow frame.
        internal bool IntegrateVelocity(double startOffsetFromNewest, double endOffsetFromNewest, out Double3 motion)
        {
            return _velocities.Integrate(startOffsetFromNewest, endOffsetFromNewest, out motion);
        }

        internal bool SampleAcceleration(double offsetFromNewest, out Double3 acceleration)
        {
            return _accelerations.Sample(offsetFromNewest, out acceleration);
        }

        internal bool SampleAngularVelocity(Timestamp time, out Double3 angularVelocity)
        {
            angularVelocity = default(Double3);
            if (!_angularVelocities.OldestTime.HasValue || time.CompareTo(_angularVelocities.OldestTime.Value) < 0)
            {
                return false;
            }

            return _angularVelocities.Sample(time.ElapsedSince(_angularVelocities.NewestTime.Value), out angularVelocity);
        }

        internal bool SampleRotation(double offsetFromNewest, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (_rotationCount == 0 || !SpatialMath.IsFinite(offsetFromNewest))
            {
                return false;
            }

            RotationSample newest = RotationAt(_rotationCount - 1);
            rotation = newest.Value;
            if (_rotationCount == 1)
            {
                return true;
            }

            if (offsetFromNewest >= 0)
            {
                return true;
            }

            RotationSample oldest = RotationAt(0);
            if (offsetFromNewest <= oldest.Time.ElapsedSince(newest.Time))
            {
                // If playback is earlier than stored history, hold the oldest available sample.
                // Do not invent an earlier pose or change the shared clock for this one entity.
                rotation = oldest.Value;
                return true;
            }

            RotationSample after = newest;
            for (int index = _rotationCount - 2; index >= 0; index--)
            {
                RotationSample before = RotationAt(index);
                double beforeOffset = before.Time.ElapsedSince(newest.Time);
                if (offsetFromNewest >= beforeOffset)
                {
                    double weight = (offsetFromNewest - beforeOffset) / after.Time.ElapsedSince(before.Time);
                    rotation = Quaternion.Slerp(before.Value, after.Value, (float)weight);
                    return true;
                }

                after = before;
            }

            rotation = oldest.Value;
            return true;
        }

        internal void ClearPosition()
        {
            Version++;
            _positions.Clear();
            _positionEstimateStart = null;
        }

        internal void ClearRotation()
        {
            Version++;
            _rotationStart = 0;
            _rotationCount = 0;
            _rotationEstimateStart = null;
        }

        internal void ClearVelocity()
        {
            Version++;
            _velocities.Clear();
            _velocityEstimateStart = null;
        }

        internal void ClearAcceleration()
        {
            Version++;
            _accelerations.Clear();
        }

        internal void ClearAngularVelocity()
        {
            Version++;
            _angularVelocities.Clear();
            _angularEstimateStart = null;
        }

        internal void Clear()
        {
            ClearPosition();
            ClearRotation();
            ClearVelocity();
            ClearAcceleration();
            ClearAngularVelocity();
        }

        internal bool EstimateAngularVelocity(Timestamp? time, out Double3 rate)
        {
            // Estimate from rotations at or before the requested time. Do not use a later-only rotation.
            rate = default(Double3);
            int newestIndex = _rotationCount - 1;
            while (newestIndex >= 0 && time.HasValue && RotationAt(newestIndex).Time.CompareTo(time.Value) > 0)
            {
                newestIndex--;
            }

            if (newestIndex < 1 || (_rotationEstimateStart.HasValue
                && RotationAt(newestIndex - 1).Time.CompareTo(_rotationEstimateStart.Value) < 0))
            {
                return false;
            }

            RotationSample newest = RotationAt(newestIndex);
            RotationSample previous = RotationAt(newestIndex - 1);
            double elapsed = newest.Time.ElapsedSince(previous.Time);
            try
            {
                // Convert the shortest rotation change into a body rotation rate so the same strategy
                // can use supplied and estimated rates. A single rotation cannot tell us a rate.
                Quaternion arc = Quaternion.Inverse(previous.Value) * newest.Value;
                if (arc.w < 0)
                {
                    arc = new Quaternion(-arc.x, -arc.y, -arc.z, -arc.w);
                }

                double length = Math.Sqrt((double)arc.x * arc.x + (double)arc.y * arc.y + (double)arc.z * arc.z);
                if (length > 0)
                {
                    // atan2 retains tiny turns when the float scalar component has rounded to one.
                    // acos(w), including Quaternion.ToAngleAxis, would lose their angular rate entirely.
                    rate = new Double3(arc.x, arc.y, arc.z) * (2d * Math.Atan2(length, arc.w) / (length * elapsed));
                }

                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private RotationSample RotationAt(int index)
        {
            return _rotations[(_rotationStart + index) % Capacity];
        }
    }
}
