using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Supplies the simulated aircraft detector to its authored anchor.
    /// </summary>
    [RequireComponent(typeof(AnchorSetup))]
    public sealed class AircraftSource : MonoBehaviour, IDetectorProvider
    {
        /// <summary>
        /// Creates a fresh detector for each anchor attachment.
        /// </summary>
        public PresenceDetector CreateDetector()
        {
            return new SimulatedAircraftDetector { Name = "Sample aircraft" };
        }
    }
}
