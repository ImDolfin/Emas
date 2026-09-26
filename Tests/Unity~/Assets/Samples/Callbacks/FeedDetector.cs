using System;

namespace Emas.Callbacks
{
    /// <summary>
    /// Reports the sample feed's changes and releases its subscriptions when stopped.
    /// </summary>
    internal sealed class FeedDetector : PresenceDetector
    {
        private readonly SimulatedFeed _feed;
        private Action<Reading> _changed;
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
            _changed = reading => dispatch(() => Report(reading.Id, Marker.Kind, reading));
            _removed = id => dispatch(() => Disappear(Marker.Kind, id));
            _feed.Changed += _changed;
            _feed.Removed += _removed;
            if (_feed.Current != null)
            {
                _changed(_feed.Current);
            }
        }

        /// <inheritdoc />
        protected override void OnStop()
        {
            _feed.Changed -= _changed;
            _feed.Removed -= _removed;
            _changed = null;
            _removed = null;
        }
    }
}
