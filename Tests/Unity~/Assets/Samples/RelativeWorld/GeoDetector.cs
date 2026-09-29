using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Detects the sample SDK's two permanent entities at startup.
    /// </summary>
    internal sealed class GeoDetector : PresenceDetector
    {
        private readonly SimulatedGeoSdk _sdk;

        internal GeoDetector(SimulatedGeoSdk sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        protected override void OnStart()
        {
            DetectEntities();
        }

        private void DetectEntities()
        {
            foreach (GeoPoseReading reading in _sdk.ReadFrame())
            {
                Detect(reading.Id, GeoSource.Kind, reading.Label, reading.Variant, source: _sdk);
            }
        }
    }
}
