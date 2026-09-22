using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>Connects one source to the Inspector-configured SceneSetup.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private void OnEnable()
        {
            GetComponent<SceneSetup>().Track(new PollingCoordinator<Reading, Marker>(Marker.Kind)
                .ReadFrom(() => new[] { new Reading(id: "one", position: new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f)) })
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.SetPosition(item.Position)));
        }
    }
}
