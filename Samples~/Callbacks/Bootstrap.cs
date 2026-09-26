using System;
using UnityEngine;

namespace Emas.Callbacks
{
    /// <summary>
    /// Creates the callback source and advances its simulated feed.
    /// </summary>
    /// <remarks>
    /// This feed raises events on Unity's main thread. SDK adapters must deliver events there before calling Emas.
    /// </remarks>
    public sealed class Bootstrap : MonoBehaviour, ISourceProvider
    {
        private SimulatedFeed _feed;

        /// <summary>
        /// Creates a source connected to the simulated SDK feed.
        /// </summary>
        public PresenceSource CreateSource()
        {
            SimulatedFeed feed = new SimulatedFeed();
            _feed = feed;
            return new CallbackPresenceSource<Reading, Marker>(Marker.Kind)
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.SetPosition(item.Position))
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
