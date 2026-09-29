using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>
    /// Configures marker presences and creates the detector for this prefab anchor.
    /// </summary>
    public sealed class MarkerSource : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        /// <summary>The entity category configured by this sample.</summary>
        public static readonly Kind Kind = new Kind("minimal.marker");

        /// <summary>
        /// Binds the marker root's configured position module before tracking starts.
        /// </summary>
        /// <param name="realm">The realm owned by this prefab setup.</param>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<Ghost>(Kind, (presence, marker) =>
            {
                marker.GetComponent<MarkerPositionModule>().Bind(() =>
                    (presence.Source as MarkerSource)?.ReadPosition(presence.Key.EntityId) ?? marker.transform.localPosition);
            });
        }

        /// <summary>
        /// Creates a detector that reads the sample's current SDK position.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            return new MarkerDetector(this);
        }
        private Vector3 ReadPosition(string id)
        {
            // Replace this simulated SDK lookup with the application's proxy position.
            return new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f);
        }
    }
}