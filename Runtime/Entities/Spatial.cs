using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Stores independent position and rotation updates for optional realm-relative placement.
    /// </summary>
    /// <remarks>
    /// Place on the Ghost root. Positions use the realm's shared Cartesian coordinates and retain doubles.
    /// Realm projection owns this root's world pose when a reference frame is configured; views inherit that pose.
    /// Keep articulation on child transforms. A custom source updating a cached ghost still calls MarkPublished
    /// when inactivity expiry is enabled. Disable this component to release spatial placement and range suppression.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Spatial")]
    public sealed class Spatial : MonoBehaviour
    {
        private Double3 _position;
        private Quaternion _rotation = Quaternion.identity;
        private bool _hasPosition;
        private bool _hasRotation;
        private bool _isInRange = true;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly HashSet<Renderer> _hiddenRenderers = new HashSet<Renderer>();
        private readonly HashSet<Collider> _hiddenColliders = new HashSet<Collider>();

        /// <summary>
        /// Gets the last position in shared Cartesian coordinates, which is meaningful after HasPosition becomes true.
        /// </summary>
        public Double3 Position
        {
            get
            {
                return _position;
            }
        }

        /// <summary>
        /// Gets the last rotation in the shared Cartesian frame, independently of position.
        /// </summary>
        public Quaternion Rotation
        {
            get
            {
                return _rotation;
            }
        }

        /// <summary>
        /// Gets whether a position in shared Cartesian coordinates has been supplied.
        /// </summary>
        public bool HasPosition
        {
            get
            {
                return _hasPosition;
            }
        }

        /// <summary>
        /// Gets whether a rotation in the shared Cartesian frame has been supplied; otherwise the root's rotation is left alone.
        /// </summary>
        public bool HasRotation
        {
            get
            {
                return _hasRotation;
            }
        }

        /// <summary>
        /// Gets whether the last projection permits presentation, independently of source availability.
        /// </summary>
        /// <remarks>
        /// Outside range or before the first position/reference, renderers and colliders are suppressed while
        /// the ghost remains active and queryable. Originally enabled components are restored on return.
        /// False also covers coordinates that cannot be projected to finite Unity floats.
        /// </remarks>
        public bool IsInRange
        {
            get
            {
                return _isInRange;
            }
        }

        /// <summary>
        /// Supplies a position in shared Cartesian coordinates without changing rotation or other ghost data.
        /// </summary>
        public void SetPosition(Double3 position)
        {
            ReferenceFrame.ValidatePosition(position, nameof(position));
            _position = position;
            _hasPosition = true;
        }

        /// <summary>
        /// Supplies a rotation in the shared Cartesian frame without changing position or other ghost data.
        /// </summary>
        public void SetRotation(Quaternion rotation)
        {
            _rotation = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            _hasRotation = true;
        }

        internal void SetInRange(bool value)
        {
            _isInRange = value;
            if (value)
            {
                RestorePresentation();
                return;
            }

            // Include newly attached components while hidden; preserve components already disabled by the application.
            GetComponentsInChildren(true, _renderers);
            foreach (Renderer renderer in _renderers)
            {
                if (renderer != null && renderer.enabled)
                {
                    _hiddenRenderers.Add(renderer);
                    renderer.enabled = false;
                }
            }

            GetComponentsInChildren(true, _colliders);
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

        private void RestorePresentation()
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

        private void OnDestroy()
        {
            RestorePresentation();
        }

        private void OnEnable()
        {
            if (!_isInRange)
            {
                SetInRange(false);
            }
        }

        private void OnDisable()
        {
            // Temporarily hiding the hierarchy must not release range suppression on reactivation.
            if (!enabled)
            {
                _isInRange = true;
                RestorePresentation();
            }
        }
    }
}
