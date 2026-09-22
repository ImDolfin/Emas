using System;
using UnityEngine;

namespace Emas.Callbacks
{
    /// <summary>Connects SDK events and their unsubscribe action to Inspector-configured tracking.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private SimulatedFeed _feed;

        private void OnEnable()
        {
            var feed = new SimulatedFeed();
            _feed = feed;
            GetComponent<SceneSetup>().Track(new CallbackPresenceSource<Reading, Marker>(Marker.Kind)
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
                }));
        }

        private void Update()
        {
            _feed.Advance(Time.deltaTime);
        }
    }
}
