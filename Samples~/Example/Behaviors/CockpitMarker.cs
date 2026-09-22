using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Maps normalized top-left cockpit coordinates onto a resolved screen.
    /// </summary>

    public sealed class CockpitMarker : MonoBehaviour
    {
        private string _screenId;
        private Vector2 _normalizedTopLeft;

        /// <summary>
        /// Sets the source screen ID and normalized top-left coordinate.
        /// </summary>
        /// <param name="screenId">
        /// The source screen identifier.
        /// </param>
        /// <param name="normalizedTopLeft">
        /// The normalized top-left coordinate.
        /// </param>
        public void SetData(string screenId, Vector2 normalizedTopLeft)
        {
            _screenId = screenId;
            _normalizedTopLeft = normalizedTopLeft;
        }

        /// <summary>
        /// Updates this marker using the current screen transform.
        /// </summary>
        /// <param name="resolveScreen">
        /// Resolves a screen ID to a transform.
        /// </param>
        public void Apply(Func<string, Transform> resolveScreen)
        {
            Transform screen;
            if (resolveScreen == null || string.IsNullOrEmpty(_screenId) || !TryResolve(resolveScreen, out screen))
            {
                gameObject.SetActive(false);
                return;
            }

            transform.SetParent(screen, false);
            transform.localPosition = new Vector3(
                _normalizedTopLeft.x - 0.5f,
                0.5f - _normalizedTopLeft.y,
                -0.02f);
            gameObject.SetActive(true);
        }

        private bool TryResolve(Func<string, Transform> resolveScreen, out Transform screen)
        {
            screen = resolveScreen(_screenId);
            return screen != null;
        }
    }
}
