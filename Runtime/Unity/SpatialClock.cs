using System;

namespace Emas
{
    // Estimate SDK time from the arrival with the smallest observed delay. Absolute transport delay is unknown.
    internal sealed class SpatialClock
    {
        private bool _hasAnchor;
        private Timestamp _sourceAnchor;
        private double _localAnchor;

        internal void Observe(Timestamp sampleTime, double receivedTime)
        {
            if (!_hasAnchor || sampleTime.ElapsedSince(_sourceAnchor) > receivedTime - _localAnchor)
            {
                _sourceAnchor = sampleTime;
                _localAnchor = receivedTime;
                _hasAnchor = true;
            }
        }

        internal double Age(Timestamp? sampleTime, double receivedTime, double localTime)
        {
            double age = sampleTime.HasValue && _hasAnchor
                ? (localTime - _localAnchor) - sampleTime.Value.ElapsedSince(_sourceAnchor)
                : localTime - receivedTime;
            return Math.Max(0d, age);
        }

        internal void Reset()
        {
            _hasAnchor = false;
        }
    }
}
