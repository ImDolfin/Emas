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
        /// <summary>The entity category configured by this sample.</summary>
        public static readonly Kind Kind = new Kind("callbacks.marker");

        private SimulatedFeed _feed;

        /// <summary>
        /// Binds the marker root's configured position module before detectors start.
        /// </summary>
        /// <param name="realm">The realm that owns this sample's presences.</param>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<Ghost>(Kind, (presence, marker) =>
            {
                marker.GetComponent<MarkerPositionModule>().Bind(() =>
                    (presence.Source as SimulatedFeed)?.Current?.Position ?? marker.transform.localPosition);
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
