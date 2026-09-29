using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Connects the simulated geodetic SDK to the Inspector-configured anchor.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Examples/Geo Source")]
    public sealed class GeoSource : PresenceDetectorComponent
    {
        /// <summary>The entity category configured by this sample.</summary>
        public static readonly Kind Kind = new Kind("relative.car");

        /// <summary>The circling bird's category, with its own blueprint.</summary>
        public static readonly Kind BirdKind = new Kind("relative.bird");

        private SimulatedGeoSdk _sdk;

        /// <inheritdoc />
        protected override void OnStart()
        {
            _sdk = new SimulatedGeoSdk();
            RefreshPresence();
        }

        /// <summary>
        /// Advances the simulation; the next realm update reads the resulting snapshot.
        /// </summary>
        /// <param name="seconds">Additional simulated time in seconds.</param>
        public void Advance(double seconds)
        {
            if (_sdk != null)
            {
                _sdk.Advance(seconds);
                RefreshPresence();
            }
        }
        private void RefreshPresence()
        {
            foreach (GeoPoseReading reading in _sdk.ReadFrame())
            {
                Detect(reading.Id, reading.Kind, reading.Label, reading.Variant, source: _sdk);
            }
            foreach (Presence presence in OwnedPresences)
            {
                if (!_sdk.Current.ContainsKey(presence.Key.EntityId))
                {
                    Disappear(presence.Key.Kind, presence.Key.EntityId);
                }
            }
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            Advance(Time.deltaTime);
        }

    }
}
