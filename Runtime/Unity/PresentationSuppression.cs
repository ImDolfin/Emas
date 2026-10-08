using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Owns suppression state and restores only components disabled by spatial presentation.
    internal sealed class PresentationSuppression
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly HashSet<Renderer> _hiddenRenderers = new HashSet<Renderer>();
        private readonly HashSet<Collider> _hiddenColliders = new HashSet<Collider>();

        internal bool IsInRange { get; private set; } = true;

        internal void SetInRange(Component root, bool value)
        {
            IsInRange = value;
            if (value)
            {
                Restore();
                return;
            }

            // Include newly attached components while hidden; preserve components already disabled by the application.
            root.GetComponentsInChildren(true, _renderers);
            foreach (Renderer renderer in _renderers)
            {
                if (renderer != null && renderer.enabled)
                {
                    _hiddenRenderers.Add(renderer);
                    renderer.enabled = false;
                }
            }

            root.GetComponentsInChildren(true, _colliders);
            foreach (Collider collider in _colliders)
            {
                if (collider != null && collider.enabled)
                {
                    _hiddenColliders.Add(collider);
                    collider.enabled = false;
                }
            }

            _renderers.Clear();
            _colliders.Clear();
            _hiddenRenderers.RemoveWhere(item => item == null);
            _hiddenColliders.RemoveWhere(item => item == null);
        }

        internal void Restore()
        {
            foreach (Renderer renderer in _hiddenRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }

            foreach (Collider collider in _hiddenColliders)
            {
                if (collider != null)
                {
                    collider.enabled = true;
                }
            }

            _hiddenRenderers.Clear();
            _hiddenColliders.Clear();
        }
    }
}
