using System;
using UnityEngine;

namespace Emas.Callbacks
{
    /// <summary>
    /// Configures marker presences, creates the callback detector, and advances its simulated feed.
    /// </summary>
    /// <remarks>
    /// This feed raises events on Unity's main thread. SDK adapters must deliver events there before calling Emas.
    /// </remarks>
    public sealed class Bootstrap : MonoBehaviour, IDetectorProvider, IRealmConfigurator
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
        /// Creates a detector connected to the simulated SDK feed.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            SimulatedFeed feed = new SimulatedFeed();
            _feed = feed;
            return new CallbackPresenceDetector<Reading>(Marker.Kind)
                .IdentifyBy(item => item.Id)
                .Listen((publish, remove) =>
                {
                    feed.Changed += publish;
                    feed.Removed += remove;
                    Action unsubscribe = () =>
                    {
                        feed.Changed -= publish;
                        feed.Removed -= remove;
                    };
                    try
                    {
                        if (feed.Current != null)
                        {
                            publish(feed.Current);
                        }

                        return unsubscribe;
                    }
                    catch
                    {
                        unsubscribe();
                        throw;
                    }
                });
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
