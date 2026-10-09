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
            // A smaller arrival-minus-source offset can only advance the estimated SDK clock.
            // Slower packets must not move that clock backwards or reset every entity's prediction age.
            if (!_hasAnchor || sampleTime.ElapsedSince(_sourceAnchor) > receivedTime - _localAnchor)
            {
                _sourceAnchor = sampleTime;
                _localAnchor = receivedTime;
                _hasAnchor = true;
            }
        }

        internal double Age(Timestamp? sampleTime, double receivedTime, double localTime)
        {
            // Shared timestamps remove relative delivery jitter; untimed input can only use its own arrival age.
            double age = sampleTime.HasValue && _hasAnchor
                ? (localTime - _localAnchor) - sampleTime.Value.ElapsedSince(_sourceAnchor)
                : localTime - receivedTime;
            return Math.Max(0d, age);
        }

        internal double OffsetFrom(Timestamp sampleTime, double localTime, double delay)
        {
            // Preserve negative offsets: buffered playback usually lies BEFORE the newest observation.
            // Subtract timestamp parts before using doubles so large SDK epochs retain sub-frame precision.
            return (_hasAnchor ? (localTime - _localAnchor) - sampleTime.ElapsedSince(_sourceAnchor) : 0d) - delay;
        }

        internal void Reset()
        {
            _hasAnchor = false;
        }
    }
}
