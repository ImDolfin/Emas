using System;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Animates the authored cockpit screen and maps its simulated marker data.
    /// </summary>
    public sealed class CockpitDemo : MonoBehaviour
    {
        [SerializeField]
        private Transform _screen;
        [SerializeField]
        private CockpitMarker _marker;

        private readonly SimulatedCockpitFeed _feed = new SimulatedCockpitFeed();

        private void Update()
        {
            _screen.localRotation = Quaternion.Euler(
                0f, Mathf.Sin(Time.time * 0.35f) * 7f, Mathf.Sin(Time.time * 0.55f) * 2f);
            CockpitMarkerData data = _feed.ReadMarker(Time.time);
            _marker.SetData(data.ScreenId, data.NormalizedTopLeft);
            _marker.Apply(ResolveScreen);
        }

        private Transform ResolveScreen(string screenId)
        {
            return string.Equals(screenId, "screen", StringComparison.Ordinal) ? _screen : null;
        }
    }
}
