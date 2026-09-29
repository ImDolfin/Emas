using System;

namespace Emas.Callbacks
{
    /// <summary>
    /// Detects the sample feed's arrivals and departures and releases its subscriptions when stopped.
    /// </summary>
    internal sealed class FeedDetector : PresenceDetector
    {
        private readonly SimulatedFeed _feed;
        private Action<Reading> _arrived;
        private Action<string> _removed;

        internal FeedDetector(SimulatedFeed feed)
        {
            _feed = feed;
        }

        /// <inheritdoc />
        protected override void OnStart()
        {
            // The feed raises events on Unity's main thread. Capture this attachment's
            // dispatcher so callbacks retained after a restart cannot publish stale data.
            Action<Action> dispatch = CaptureDispatcher();
            _arrived = reading => dispatch(() => Detect(reading.Id, FeedSource.Kind, source: _feed));
            _removed = id => dispatch(() => Disappear(FeedSource.Kind, id));
            _feed.Arrived += _arrived;
            _feed.Removed += _removed;
            if (_feed.Current != null)
            {
                _arrived(_feed.Current);
            }
        }

        /// <inheritdoc />
        protected override void OnStop()
        {
            _feed.Arrived -= _arrived;
            _feed.Removed -= _removed;
            _arrived = null;
            _removed = null;
        }
    }
}
