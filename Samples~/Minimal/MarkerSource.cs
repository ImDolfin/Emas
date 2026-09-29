using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>Detects one permanent marker from a simulated SDK.</summary>
    public sealed class MarkerSource : PresenceDetectorComponent
    {
        /// <summary>The entity category configured by this sample.</summary>
        public static readonly Kind Kind = new Kind("minimal.marker");

        /// <inheritdoc />
        protected override void OnStart()
        {
            Detect("one", Kind, source: this);
        }

        internal Vector3 ReadPosition(string id)
        {
            return new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f);
        }
    }
}
