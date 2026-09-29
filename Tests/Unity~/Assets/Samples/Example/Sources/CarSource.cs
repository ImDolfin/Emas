using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Supplies the car detector and demonstrates replacing its SDK without replacing its ghosts.
    /// </summary>
    [RequireComponent(typeof(AnchorSetup))]
    public sealed class CarSource : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        [Tooltip("Seconds before switching from SDK One to SDK Two.")]
        [SerializeField]
        private float _replacementDelay = 4f;

        private SdkOneCarDetector _firstSource;
        private float _elapsed;
        private readonly SdkOneVehicleFeed _firstFeed = new SdkOneVehicleFeed();
        private readonly SdkTwoVehicleFeed _secondFeed = new SdkTwoVehicleFeed();

        /// <summary>Maps the active SDK to modules already configured on the car Ghost.</summary>
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<CarGhost>(SampleKinds.Car, (presence, root) =>
            {
                string id = presence.Key.EntityId;
                PositionModule position = root.GetComponent<PositionModule>();
                ArticulationModule articulation = root.GetComponent<ArticulationModule>();
                if (presence.Source is SdkTwoVehicleFeed)
                {
                    position.Bind(() => ((SdkTwoVehicleFeed)presence.Source).Current[id].Coordinates);
                    articulation.Bind(() => ((SdkTwoVehicleFeed)presence.Source).Current[id].WheelAngle);
                }
                else
                {
                    position.Bind(() =>
                    {
                        SdkOneVehicleProxy proxy = ((SdkOneVehicleFeed)presence.Source).Current[id];
                        return new Vector3(proxy.PositionX, proxy.PositionY, proxy.PositionZ);
                    });
                    articulation.Bind(() => ((SdkOneVehicleFeed)presence.Source).Current[id].Steering);
                }
            });
        }

        /// <summary>
        /// Gets whether the current attachment has switched to SDK Two.
        /// </summary>
        public bool IsUsingSecondSdk { get; private set; }

        /// <summary>
        /// Starts each anchor attachment with a fresh SDK One detector and replacement timer.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            _elapsed = 0f;
            IsUsingSecondSdk = false;
            ReadProxies();
            _firstSource = new SdkOneCarDetector(_firstFeed) { Name = "SDK One cars" };
            return _firstSource;
        }

        /// <summary>
        /// Switches to SDK Two once, retaining compatible car ghosts and their consumers.
        /// </summary>
        public void ReplaceCarSource()
        {
            Anchor anchor = GetComponent<AnchorSetup>().Anchor;
            if (anchor == null || _firstSource == null || !_firstSource.IsAttached || IsUsingSecondSdk)
            {
                return;
            }

            IsUsingSecondSdk = true;
            ReadProxies();
            anchor.ReplaceDetector(_firstSource, new SdkTwoCarDetector(_secondFeed) { Name = "SDK Two cars" });
        }

        private void Update()
        {
            ReadProxies();
            if (_firstSource == null || !_firstSource.IsAttached || IsUsingSecondSdk)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= _replacementDelay)
            {
                ReplaceCarSource();
            }
        }
        private void ReadProxies()
        {
            if (IsUsingSecondSdk)
            {
                _secondFeed.ReadVehicles(Time.time);
            }
            else
            {
                _firstFeed.ReadVehicles(Time.time);
            }
        }
    }
}
