using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>
    /// Reports the sample's single marker when attached and on each realm update.
    /// </summary>
    internal sealed class MarkerDetector : PresenceDetector
    {
        /// <inheritdoc />
        protected override void OnStart()
        {
            Publish();
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            Publish();
        }

        private void Publish()
        {
            Reading reading = new Reading("one", new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f));
            Report(reading.Id, Marker.Kind, reading);
        }
    }
}
