using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Reports the sample SDK's origin and target together on each realm update.
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
            PublishFrame();
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            _sdk.Advance(Time.deltaTime);
            PublishFrame();
        }

        private void PublishFrame()
        {
            foreach (GeoPoseReading reading in _sdk.ReadFrame())
            {
                Report(reading.Id, RelativeCar.Kind, reading, reading.Label, reading.Variant);
            }
        }
    }
}
