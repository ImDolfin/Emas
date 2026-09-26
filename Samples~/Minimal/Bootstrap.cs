using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>
    /// Configures marker presences and creates the detector for this prefab anchor.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        /// <summary>
        /// Registers the marker root and its SDK position module before tracking starts.
        /// </summary>
        /// <param name="realm">The realm owned by this prefab setup.</param>
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
        /// Creates a detector that reads the sample's current SDK position.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            return new MarkerDetector();
        }
    }
}