using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Supplies the car detector and demonstrates replacing its SDK without replacing its ghosts.
    /// </summary>
    [RequireComponent(typeof(AnchorSetup))]
    public sealed class CarSource : PresenceDetectorComponent
    {
        [Tooltip("Seconds before switching from SDK One to SDK Two.")]
        [Min(0f)]
        [SerializeField]
        private float _replacementDelay = 4f;

        private float _elapsed;
        private readonly SdkOneVehicleFeed _firstFeed = new SdkOneVehicleFeed();
        private readonly SdkTwoVehicleFeed _secondFeed = new SdkTwoVehicleFeed();

        /// <summary>
        /// Gets whether the current attachment has switched to SDK Two.
        /// </summary>
        public bool IsUsingSecondSdk { get; private set; }

        /// <inheritdoc />
        protected override void OnStart()
        {
            _elapsed = 0f;
            IsUsingSecondSdk = false;
            foreach (SdkOneVehicleProxy proxy in _firstFeed.ReadVehicles(Time.time))
            {
                Variant variant = proxy.TypeCode == 0 ? CarVariants.SmallCar
                    : proxy.TypeCode == 1 ? CarVariants.LargeCar : CarVariants.Truck;
                Detect(proxy.Identifier, SampleKinds.Car, "Car " + proxy.Identifier, variant, source: _firstFeed);
            }
        }

        /// <summary>
        /// Switches to SDK Two once, retaining compatible car ghosts and their consumers.
        /// </summary>
        public void ReplaceCarSource()
        {
            Anchor anchor = GetComponent<AnchorSetup>().Anchor;
            if (anchor == null || !Detector.IsAttached || IsUsingSecondSdk)
            {
                return;
            }

            IsUsingSecondSdk = true;
            ReadProxies();
            anchor.ReplaceDetector(Detector, new SdkTwoCarDetector(_secondFeed) { Name = "SDK Two cars" });
        }

        // The application owns both feeds, including SDK Two after detector replacement.
        private void Update()
        {
            ReadProxies();
            if (!Detector.IsAttached || IsUsingSecondSdk)
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
            // The component continues refreshing the application-owned feed after its detector is replaced.
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
