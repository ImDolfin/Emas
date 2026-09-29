using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Connects the simulated geodetic SDK to the Inspector-configured anchor.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Examples/Geo Source")]
    public sealed class GeoSource : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        private SimulatedGeoSdk _sdk;

        /// <summary>
        /// Binds the authored car modules to positions and orientations converted from the SDK.
        /// </summary>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<RelativeCar>(RelativeCar.Kind, (presence, root) =>
            {
                string id = presence.Key.EntityId;
                root.GetComponent<GeoPositionModule>().Bind(() =>
                {
                    GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                    return GeoProjection.ToPosition(reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters);
                });
                root.GetComponent<GeoOrientationModule>().Bind(() =>
                {
                    GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                    return GeoProjection.ToRotation(reading.YawDegrees, reading.PitchDegrees, reading.RollDegrees);
                });
            });
        }

        /// <summary>
        /// Creates a fresh simulated SDK and detector for this anchor attachment.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            _sdk = new SimulatedGeoSdk();
            _sdk.ReadFrame();
            return new GeoDetector(_sdk) { Name = "Geodetic SDK entities" };
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
        private void Update()
        {
            Advance(Time.deltaTime);
        }

    }
}
