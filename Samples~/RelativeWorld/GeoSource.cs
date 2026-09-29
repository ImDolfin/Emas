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

        private SimulatedGeoSdk _sdk;

        /// <inheritdoc />
        protected override void OnStart()
        {
            _sdk = new SimulatedGeoSdk();
            foreach (GeoPoseReading reading in _sdk.ReadFrame())
            {
                Detect(reading.Id, Kind, reading.Label, reading.Variant, source: _sdk);
            }
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
                _sdk.ReadFrame();
            }
        }
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            Advance(Time.deltaTime);
        }

    }
}
