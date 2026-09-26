using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Supplies the car detector and demonstrates replacing its SDK without replacing its ghosts.
    /// </summary>
    [RequireComponent(typeof(AnchorSetup))]
    public sealed class CarSource : MonoBehaviour, IDetectorProvider
    {
        [Tooltip("Seconds before switching from SDK One to SDK Two.")]
        [SerializeField]
        private float _replacementDelay = 4f;

        private SdkOneCarDetector _firstSource;
        private float _elapsed;

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
            _firstSource = new SdkOneCarDetector { Name = "SDK One cars" };
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

            anchor.ReplaceDetector(_firstSource, new SdkTwoCarDetector { Name = "SDK Two cars" });
            IsUsingSecondSdk = true;
        }

        private void Update()
        {
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
    }
}
