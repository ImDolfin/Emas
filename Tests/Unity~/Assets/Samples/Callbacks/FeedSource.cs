using UnityEngine;

namespace Emas.Callbacks
{
    /// <summary>
    /// Configures marker presences, creates the callback detector, and advances its sample feed.
    /// </summary>
    /// <remarks>
    /// This feed raises events on Unity's main thread. SDK adapters must deliver events there before calling Emas.
    /// </remarks>
    public sealed class FeedSource : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        private SimulatedFeed _feed;

        /// <summary>
        /// Registers the marker root and its position module before detectors start.
        /// </summary>
        /// <param name="realm">The realm that owns this sample's presences.</param>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<Marker>(Marker.Kind, (presence, marker) =>
            {
                MarkerPositionModule module;
                if (!presence.TryGetModule(out module))
                {
                    presence.AddModule(new MarkerPositionModule(marker));
                }
            });
        }

        /// <summary>
        /// Creates a detector connected to the sample SDK feed.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            SimulatedFeed feed = new SimulatedFeed();
            _feed = feed;
            return new FeedDetector(feed);
        }

        private void Update()
        {
            if (_feed != null)
            {
                _feed.Advance(Time.deltaTime);
            }
        }
    }
}
