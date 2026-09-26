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
        /// Adds the modules that map SDK positions and orientations to each authored car root.
        /// </summary>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<RelativeCar>(RelativeCar.Kind, (presence, root) =>
            {
                GeoPositionModule position;
                if (!presence.TryGetModule(out position))
                {
                    presence.AddModule(new GeoPositionModule());
                }

                GeoOrientationModule orientation;
                if (!presence.TryGetModule(out orientation))
                {
                    presence.AddModule(new GeoOrientationModule());
                }
            });
        }

        /// <summary>
        /// Creates a fresh simulated SDK and detector for this anchor attachment.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            _sdk = new SimulatedGeoSdk();
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
            }
        }
    }
}