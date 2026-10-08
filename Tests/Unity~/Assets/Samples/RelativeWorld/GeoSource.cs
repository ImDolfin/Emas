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

        /// <summary>The bird's independently tracked, attachable feet.</summary>
        public static readonly Kind BirdFootKind = new Kind("relative.bird-foot");

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

        /// <summary>Changes the SDK attachment state; the next realm update attaches or detaches both bird feet.</summary>
        /// <param name="attached">True to follow the bird; false to hold each foot's current absolute world pose.</param>
        public void SetBirdFeetAttached(bool attached)
        {
            if (_sdk != null)
            {
                _sdk.SetBirdFeetAttached(attached);
                RefreshPresence();
            }
        }

        [ContextMenu("Attach bird feet")]
        private void AttachBirdFeet()
        {
            SetBirdFeetAttached(true);
        }

        [ContextMenu("Detach bird feet")]
        private void DetachBirdFeet()
        {
            SetBirdFeetAttached(false);
        }

        private void RefreshPresence()
        {
            foreach (GeoPoseReading reading in _sdk.ReadFrame())
            {
                Detect(reading.Id, reading.Kind, reading.Label, reading.Variant, source: _sdk);
            }
            foreach (Presence presence in OwnedPresences)
            {
                // Trait reads refresh data but do not signal departure; compare identities with the new SDK population.
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
