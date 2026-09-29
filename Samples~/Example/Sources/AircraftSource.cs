using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Connects aircraft discovery and Ghost module inputs to the sample SDK.</summary>
    [RequireComponent(typeof(AnchorSetup))]
    public sealed class AircraftSource : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        private readonly SimulatedAircraftFeed _feed = new SimulatedAircraftFeed();

        /// <summary>Binds the position module configured on each aircraft Ghost.</summary>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<Ghost>(SampleKinds.Aircraft, (presence, root) =>
                root.GetComponent<PositionModule>().Bind(() => ((SimulatedAircraftFeed)presence.Source).Current[presence.Key.EntityId].Position));
        }

        /// <summary>Creates a fresh detector for each anchor attachment.</summary>
        public PresenceDetector CreateDetector()
        {
            Update();
            return new SimulatedAircraftDetector(_feed) { Name = "Sample aircraft" };
        }

        private void Update()
        {
            _feed.ReadAircraft(Time.time);
        }
    }
}
