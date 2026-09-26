using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>
    /// Creates the polling source configured by this prefab anchor.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour, ISourceProvider
    {
        /// <summary>
        /// Creates a source that reads the sample's current position.
        /// </summary>
        public PresenceSource CreateSource()
        {
            return new PollingPresenceSource<Reading, Marker>(Marker.Kind)
                .ReadFrom(() => new[] { new Reading(id: "one", position: new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f)) })
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.SetPosition(item.Position));
        }
    }
}
