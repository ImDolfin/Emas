using System;
using UnityEngine;

namespace Emas
{
    // Preserve independent SDK channels without converting their absolute epochs to floating point.
    // Each ring retains 128 observations; sampling and insertion allocate nothing after construction.
    internal sealed class PoseHistory
    {
        private const int Capacity = 128;
        private readonly VectorHistory _positions = new VectorHistory();
        private readonly VectorHistory _velocities = new VectorHistory();
        private readonly VectorHistory _accelerations = new VectorHistory();
        private readonly RotationSample[] _rotations = new RotationSample[Capacity];
        private int _rotationStart;
        private int _rotationCount;

        internal Timestamp? NewestPositionTime => _positions.NewestTime;
        internal Timestamp? OldestPositionTime => _positions.OldestTime;
        internal Timestamp? NewestRotationTime => _rotationCount == 0
            ? (Timestamp?)null : RotationAt(_rotationCount - 1).Time;
        internal Timestamp? NewestVelocityTime => _velocities.NewestTime;
        internal Timestamp? OldestVelocityTime => _velocities.OldestTime;
        internal Timestamp? NewestAccelerationTime => _accelerations.NewestTime;
        internal Timestamp? OldestAccelerationTime => _accelerations.OldestTime;

        internal void AddPosition(Double3 position, Timestamp time)
        {
            _positions.Add(position, time);
        }

        internal void AddRotation(Quaternion rotation, Timestamp time)
        {
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
            _velocities.Add(velocity, time);
        }

        internal void AddAcceleration(Double3 acceleration, Timestamp time)
        {
            _accelerations.Add(acceleration, time);
        }

        // Offsets are playback time minus this channel's newest timestamp. Negative values sample
        // recorded history; positive values hold the latest vector for the caller's bounded prediction.
        internal bool SamplePosition(double offsetFromNewest, out Double3 position)
        {
            return _positions.Sample(offsetFromNewest, out position);
        }

        internal bool SampleVelocity(double offsetFromNewest, out Double3 velocity)
        {
            return _velocities.Sample(offsetFromNewest, out velocity);
        }

        // Integrate the same piecewise-linear velocity curve used by sampling, including every
        // crossed observation. Endpoint-only trapezoids miss a maneuver inside a long render step.
        internal bool IntegrateVelocity(double startOffsetFromNewest, double endOffsetFromNewest, out Double3 motion)
        {
            return _velocities.Integrate(startOffsetFromNewest, endOffsetFromNewest, out motion);
        }

        internal bool SampleAcceleration(double offsetFromNewest, out Double3 acceleration)
        {
            return _accelerations.Sample(offsetFromNewest, out acceleration);
        }

        internal bool SampleRotation(double offsetFromNewest, double maximumExtrapolation, out Quaternion rotation)
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
                rotation = ExtrapolateRotation(newest, offsetFromNewest, maximumExtrapolation);
                return true;
            }

            RotationSample oldest = RotationAt(0);
            if (offsetFromNewest <= oldest.Time.ElapsedSince(newest.Time))
            {
                // Startup and an exhausted ring hold the oldest available observation rather than
                // inventing an earlier pose or stretching the playback clock for this one entity.
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
            _positions.Clear();
        }

        internal void ClearRotation()
        {
            _rotationStart = 0;
            _rotationCount = 0;
        }

        internal void ClearVelocity()
        {
            _velocities.Clear();
        }

        internal void ClearAcceleration()
        {
            _accelerations.Clear();
        }

        internal void Clear()
        {
            ClearPosition();
            ClearRotation();
            ClearVelocity();
            ClearAcceleration();
        }

        private Quaternion ExtrapolateRotation(RotationSample newest, double offset, double maximumExtrapolation)
        {
            if (offset <= 0 || !SpatialMath.IsFinite(maximumExtrapolation) || maximumExtrapolation <= 0)
            {
                return newest.Value;
            }

            RotationSample previous = RotationAt(_rotationCount - 2);
            double elapsed = newest.Time.ElapsedSince(previous.Time);
            double amount = 1d + Math.Min(offset, maximumExtrapolation) / elapsed;
            if (!SpatialMath.IsFinite(amount) || amount > float.MaxValue)
            {
                return newest.Value;
            }

            try
            {
                // Extend the shortest observed quaternion arc, freezing at the capped future pose
                // once the gap exceeds the horizon. One observation cannot define an angular rate.
                return SpatialMath.NormalizeRotation(
                    Quaternion.SlerpUnclamped(previous.Value, newest.Value, (float)amount), nameof(newest));
            }
            catch (ArgumentOutOfRangeException)
            {
                return newest.Value;
            }
        }

        private RotationSample RotationAt(int index)
        {
            return _rotations[(_rotationStart + index) % Capacity];
        }

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

            internal bool Sample(double offsetFromNewest, out Double3 value)
            {
                value = default(Double3);
                if (_count == 0 || !SpatialMath.IsFinite(offsetFromNewest))
                {
                    return false;
                }

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
                // Samples contain values only; clearing counts releases the timeline without clearing the arrays.
                _start = 0;
                _count = 0;
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

                bool reverse = start > end;
                try
                {
                    value = reverse ? Integral(end, start) * -1d : Integral(start, end);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // A finite interval and velocity can still produce an unrepresentable displacement.
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
                    // Sampling holds the latest value beyond its timestamp. Acceleration-based
                    // extrapolation, when requested, is added separately by the prediction model.
                    double tailStart = Math.Max(start, 0d);
                    result = newest.Value * (end - tailStart);
                    end = tailStart;
                }

                // Scan back only as far as this update needs. A normal render interval crosses
                // few recent samples; a frame hitch can integrate all intervening maneuver segments.
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
                    // Before startup history (or after ring exhaustion), sampling holds the oldest value.
                    result += At(0).Value * (end - start);
                }

                return result;
            }

            private static Double3 IntegralSegment(Double3 before, Double3 after, double beforeOffset,
                double afterOffset, double start, double end)
            {
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
                // difference when finite endpoints lie near opposite ends of the double range.
                double difference = after - before;
                return SpatialMath.IsFinite(difference)
                    ? before + difference * weight : before * (1d - weight) + after * weight;
            }
        }
    }
}
