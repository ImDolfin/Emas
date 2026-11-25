using System;

namespace Emas
{
    // Align Unix packet ages or an arbitrary simulation epoch with the local presentation clock.
    internal sealed class SpatialClock
    {
        private const int OffsetCapacity = 31;
        private readonly double[] _offsets = new double[OffsetCapacity];
        private readonly double[] _ages = new double[OffsetCapacity];
        private readonly double[] _sortedOffsets = new double[OffsetCapacity];
        private Spatial _reference;
        private bool _hasAnchor;
        private Timestamp _sourceAnchor;
        private double _localAnchor;
        private Timestamp? _referenceSample;
        private int _offsetCount;
        private int _offsetIndex;
        private double _medianOffset;
        private double _medianAge;
        private TimestampClock _clockMode;
        private double _clockOffset;
        private bool _hasClockKind;
        private bool _usingUnixTime;
        private double _forwardTime;
        private double _requestedForwardTime;
        private float _manualPredictionTime;
        private double? _previousProjection;

        internal bool HasReference => !ReferenceEquals(_reference, null);

        internal bool HasUnixClock => HasReference && _usingUnixTime;

        internal bool HasManualPrediction => HasReference && _manualPredictionTime > 0;

        internal Timestamp? ProjectedTime { get; private set; }

        internal double? EstimatedPacketAge => HasUnixClock && _offsetCount > 0
            ? _medianAge + _clockOffset : (double?)null;

        private double AlignmentOffset => _medianOffset - (_usingUnixTime ? _medianAge + _clockOffset : 0d);

        internal void SetReference(Spatial reference, TimestampClock clockMode, double clockOffset)
        {
            if (!ReferenceEquals(_reference, reference) || _clockMode != clockMode || _clockOffset != clockOffset)
            {
                // A new reference or calibration starts fresh alignment. Reset the timeline floor
                // too, so a negative calibration edit can take effect on the next pass.
                Reset();
                _reference = reference;
                _clockMode = clockMode;
                _clockOffset = clockOffset;
            }
        }

        internal void Observe(Timestamp sampleTime, double receivedTime)
        {
            if (HasUnixClock)
            {
                return;
            }

            // A smaller arrival-minus-source offset can only advance the estimated SDK clock.
            // Slower packets must not move that clock backwards or reset every entity's extrapolation age.
            if (!_hasAnchor || sampleTime.ElapsedSince(_sourceAnchor) > receivedTime - _localAnchor)
            {
                _sourceAnchor = sampleTime;
                _localAnchor = receivedTime;
                _hasAnchor = true;
            }
        }

        internal void Capture(double localTime, float interpolationDelay, float predictionTime,
            Timestamp? referenceTime, double referenceReceivedTime, float manualPredictionTime)
        {
            if (!_hasAnchor)
            {
                _previousProjection = null;
                _forwardTime = 0;
                _manualPredictionTime = 0;
                ProjectedTime = null;
                return;
            }

            manualPredictionTime = HasReference && referenceTime.HasValue
                && SpatialMath.IsFinite(manualPredictionTime) && manualPredictionTime > 0 ? manualPredictionTime : 0;
            double automatic = (localTime - _localAnchor) - AlignmentOffset;
            // A manual interval replaces automatic packet-age compensation. Anchor it
            // to the reference observation and advance only by time since its receipt.
            // Keep measuring Unix ages in either mode so zero can restore automatic timing.
            double forward = manualPredictionTime > 0
                ? referenceTime.Value.ElapsedSince(_sourceAnchor) + localTime - referenceReceivedTime + manualPredictionTime - automatic
                : HasReference && referenceTime.HasValue
                    ? Math.Max(0d, predictionTime - Age(referenceTime, referenceReceivedTime, referenceReceivedTime)) : 0d;
            double projected = automatic + forward;
            if (_requestedForwardTime != predictionTime || _manualPredictionTime != manualPredictionTime)
            {
                // Explicit tuning can move the requested timeline in either direction.
                // Automatic Unix timing resumes its monotonic floor from this new target.
                _previousProjection = null;
            }

            double continuous = HasUnixClock && manualPredictionTime <= 0 && _previousProjection.HasValue
                ? Math.Max(projected, _previousProjection.Value) : projected;
            _forwardTime = forward + continuous - projected;
            _previousProjection = continuous;
            _requestedForwardTime = predictionTime;
            _manualPredictionTime = manualPredictionTime;
            ProjectedTime = null;
            if (_hasAnchor)
            {
                try
                {
                    ProjectedTime = _sourceAnchor.OffsetBy(continuous - interpolationDelay);
                }
                catch (OverflowException)
                {
                    // Extreme timestamps can still be sampled using relative differences.
                }
            }
        }

        internal double PredictionTimestamp(double localTime)
        {
            return HasReference ? localTime + _forwardTime : localTime;
        }

        internal double Age(Timestamp? sampleTime, double receivedTime, double localTime)
        {
            // Shared timestamps avoid differences caused by packet delays. Without a timestamp, use time since arrival.
            double age = sampleTime.HasValue && _hasAnchor
                ? (localTime - _localAnchor) - AlignmentOffset - sampleTime.Value.ElapsedSince(_sourceAnchor)
                : localTime - receivedTime;
            return Math.Max(0d, age);
        }

        internal double OffsetFrom(Timestamp sampleTime, double localTime, double delay)
        {
            // Preserve negative offsets: buffered playback usually lies BEFORE the newest observation.
            // Subtract timestamp parts before converting to doubles so large timestamps keep precise time differences.
            return (_hasAnchor ? (localTime - _localAnchor) - AlignmentOffset - sampleTime.ElapsedSince(_sourceAnchor) : 0d) - delay;
        }

        internal void Reset()
        {
            _hasAnchor = false;
            _reference = null;
            _referenceSample = null;
            _offsetCount = 0;
            _offsetIndex = 0;
            _medianOffset = 0;
            _medianAge = 0;
            _hasClockKind = false;
            _usingUnixTime = false;
            _clockOffset = 0;
            _forwardTime = 0;
            _requestedForwardTime = 0;
            _manualPredictionTime = 0;
            _previousProjection = null;
            ProjectedTime = null;
        }

        internal void ObserveReference(Timestamp sampleTime, double receivedTime, double localTime, Timestamp utcTime)
        {
            if (!_hasClockKind)
            {
                // Epoch-free simulation clocks remain supported. The explicit modes avoid
                // ambiguity for old recordings or simulation values that resemble current UTC.
                _usingUnixTime = _clockMode == TimestampClock.Unix
                    || (_clockMode == TimestampClock.Automatic && Math.Abs(utcTime.ElapsedSince(sampleTime) + _clockOffset) <= 86400d);
                _hasClockKind = true;
            }

            if (!_usingUnixTime)
            {
                Observe(sampleTime, receivedTime);
                return;
            }

            // Repeated reads, view requests and duplicate channels must not reweight one packet.
            if (_referenceSample.HasValue && sampleTime.CompareTo(_referenceSample.Value) <= 0)
            {
                return;
            }

            if (!_hasAnchor)
            {
                _sourceAnchor = sampleTime;
                _localAnchor = receivedTime;
                _hasAnchor = true;
            }

            _referenceSample = sampleTime;
            // Subtract timestamp parts first. Large simulation epochs retain their fractional precision.
            _offsets[_offsetIndex] = (receivedTime - _localAnchor) - sampleTime.ElapsedSince(_sourceAnchor);
            // Pair UTC with the monotonic capture time, then recover UTC at the actual setter receipt.
            // The two medians share packets: subtracting median age removes transport delay from alignment.
            _ages[_offsetIndex] = utcTime.ElapsedSince(sampleTime) + receivedTime - localTime;
            _offsetIndex = (_offsetIndex + 1) % OffsetCapacity;
            _offsetCount = Math.Min(_offsetCount + 1, OffsetCapacity);
            _medianOffset = Median(_offsets);
            _medianAge = Median(_ages);
        }

        private double Median(double[] values)
        {
            Array.Copy(values, _sortedOffsets, _offsetCount);
            // The window is small and fixed. Insertion sort avoids comparer/delegate
            // allocations made by Mono's generic array-sort path on every received packet.
            for (int index = 1; index < _offsetCount; index++)
            {
                double value = _sortedOffsets[index];
                int before = index - 1;
                while (before >= 0 && _sortedOffsets[before] > value)
                {
                    _sortedOffsets[before + 1] = _sortedOffsets[before];
                    before--;
                }

                _sortedOffsets[before + 1] = value;
            }
            int middle = _offsetCount / 2;
            return _offsetCount % 2 == 0
                ? _sortedOffsets[middle - 1] * 0.5d + _sortedOffsets[middle] * 0.5d : _sortedOffsets[middle];
        }
    }
}
