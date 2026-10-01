using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Stores independent position and rotation updates for world or realm-relative placement.
    /// </summary>
    /// <remarks>
    /// Place on the Ghost root. Positions retain doubles; Cartesian poses use ReferenceFrame.Coordinates.
    /// In Geographic space, positions are ECEF metres; rotations accept named geographic angles, source quaternions or body-to-ECEF input.
    /// In Cartesian space, all participating poses share the source coordinate convention and unit.
    /// Realm projection owns this root's world pose while this component is enabled; views inherit that pose.
    /// With no reference frame, Cartesian poses map directly to Unity world space. Geographic and ECEF attitudes require a Geographic frame.
    /// Keep articulation on child transforms. A custom source updating a cached ghost still calls MarkPublished
    /// when inactivity expiry is enabled. Disable this component to release spatial placement and range suppression.
    /// Supply channels on Unity's main thread; setters store input for the next realm projection.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Spatial")]
    public sealed class Spatial : MonoBehaviour
    {
        private Double3 _position;
        private Quaternion _rotation = Quaternion.identity;
        private RotationSpace _rotationSpace;
        private CoordinateSystem? _earthCenteredBodyAxes;
        private bool _hasPosition;
        private bool _hasRotation;
        private bool _isInRange = true;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly HashSet<Renderer> _hiddenRenderers = new HashSet<Renderer>();
        private readonly HashSet<Collider> _hiddenColliders = new HashSet<Collider>();

        /// <summary>
        /// Gets the last Cartesian source position or absolute ECEF metres after geographic/ECEF input; valid after HasPosition is true.
        /// </summary>
        public Double3 Position
        {
            get
            {
                return _position;
            }
        }

        /// <summary>
        /// Gets the last normalized quaternion in RotationSpace; meaningful after HasRotation becomes true.
        /// </summary>
        public Quaternion Rotation
        {
            get
            {
                return _rotation;
            }
        }

        /// <summary>Gets the basis of Rotation: configured source axes, local east/up/north, or body-to-ECEF.</summary>
        public RotationSpace RotationSpace => _rotationSpace;

        internal CoordinateSystem? EarthCenteredBodyAxes => _earthCenteredBodyAxes;

        /// <summary>
        /// Gets whether a Cartesian or geographic position has been supplied.
        /// </summary>
        public bool HasPosition
        {
            get
            {
                return _hasPosition;
            }
        }

        /// <summary>
        /// Gets whether source orientation has been supplied; otherwise the root's rotation is left alone.
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
        /// Supplies a shared Cartesian position in ReferenceFrame.Coordinates without changing rotation.
        /// </summary>
        /// <param name="position">Finite source XYZ coordinates in the Cartesian world's shared units.</param>
        /// <remarks>Use with Cartesian space, or without a reference. Stores input for the next realm projection.</remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">An input coordinate is not finite.</exception>
        public void SetCartesianPosition(Double3 position)
        {
            StorePosition(position);
        }

        /// <summary>Supplies absolute ECEF XYZ metres for a Geographic reference, independently of attitude.</summary>
        /// <param name="position">Finite Earth-centered, Earth-fixed coordinates in metres, not latitude/longitude/height.</param>
        /// <remarks>Stores input for the next projection. No conversion through ReferenceFrame.Coordinates is applied.</remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">An input coordinate is not finite.</exception>
        public void SetEarthCenteredPosition(Double3 position)
        {
            StorePosition(position);
        }

        /// <summary>Supplies a WGS84 position, storing ECEF metres for use with a Geographic reference.</summary>
        /// <param name="position">The WGS84 position with ellipsoidal height in metres.</param>
        /// <remarks>Requires Geographic projection. Retains attitude; local geographic attitude follows the new tangent plane.</remarks>
        public void SetGeographicPosition(GeoPosition position)
        {
            StorePosition(position.ToEarthCentered());
        }

        /// <summary>
        /// Supplies orientation without changing position. In Geographic space, use local tangent attitude
        /// in ReferenceFrame.Coordinates; otherwise use the shared Cartesian frame.
        /// </summary>
        /// <param name="rotation">The finite nonzero source quaternion; normalized before storage.</param>
        /// <remarks>Sets RotationSpace to Source. Position remains unchanged; use SetGeographicRotation for named angles.</remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">The quaternion is not finite or has zero length.</exception>
        public void SetSourceRotation(Quaternion rotation)
        {
            _rotation = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            _rotationSpace = Emas.RotationSpace.Source;
            _earthCenteredBodyAxes = null;
            _hasRotation = true;
        }

        /// <summary>Supplies local geographic yaw, pitch and roll in degrees for a Geographic reference.</summary>
        /// <param name="yawDegrees">Heading clockwise from true north about local up.</param>
        /// <param name="pitchDegrees">Nose-up pitch about the heading's body-right axis.</param>
        /// <param name="rollDegrees">Right-wing-down bank about the pitched body's forward axis.</param>
        /// <remarks>
        /// Intrinsic yaw, then pitch, then roll; zero faces north with model +Z forward and +Y up.
        /// Stores a normalized east/up/north quaternion with RotationSpace.Geographic, independently of Coordinates.
        /// Angles wrap modulo 360 before float conversion. Position can arrive later; moving it preserves this local attitude.
        /// </remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">An angle is not finite.</exception>
        public void SetGeographicRotation(double yawDegrees, double pitchDegrees, double rollDegrees)
        {
            Quaternion rotation = SpatialMath.GeographicRotation(yawDegrees, pitchDegrees, rollDegrees);
            _rotation = rotation;
            _rotationSpace = Emas.RotationSpace.Geographic;
            _earthCenteredBodyAxes = null;
            _hasRotation = true;
        }

        /// <summary>Supplies a body-to-ECEF quaternion for a Geographic reference, independently of position.</summary>
        /// <param name="rotation">Active rotation mapping source body XYZ vectors into ECEF XYZ.</param>
        /// <param name="bodyAxes">Signed body axes mapped to Unity right/up/forward. Null uses X forward, Y right, Z down (NED mapping).</param>
        /// <remarks>Sets RotationSpace to EarthCentered. Body axes must be right-handed; position updates retain this global attitude.</remarks>
        /// <exception cref="System.ArgumentException">The body axes are invalid or not right-handed.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">The quaternion is not finite or has zero length.</exception>
        public void SetEarthCenteredRotation(Quaternion rotation, CoordinateSystem? bodyAxes = null)
        {
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            CoordinateSystem axes = CoordinateSystem.RequireRightHandedBodyAxes(bodyAxes);
            _rotation = normalized;
            _rotationSpace = Emas.RotationSpace.EarthCentered;
            _earthCenteredBodyAxes = axes;
            _hasRotation = true;
        }

        private void StorePosition(Double3 position)
        {
            ReferenceFrame.ValidatePosition(position, nameof(position));
            _position = position;
            _hasPosition = true;
        }

        // Called by the realm after every module has updated and the shared reference is captured.
        internal bool ApplyProjection(ReferenceFrame.Projection projection)
        {
            Vector3 position = default(Vector3);
            bool visible = HasPosition && projection.TryToUnityPosition(Position, out position);
            if (visible)
            {
                // Assign world pose so anchor transforms do not introduce a second offset.
                if (HasRotation)
                {
                    Quaternion rotation = _earthCenteredBodyAxes.HasValue
                        ? projection.ToUnityEarthCenteredRotation(Rotation, _earthCenteredBodyAxes.Value)
                        : _rotationSpace == Emas.RotationSpace.Geographic
                            ? projection.ToUnityGeographicRotation(Rotation, Position)
                            : projection.ToUnityRotation(Rotation, Position);
                    transform.SetPositionAndRotation(position, rotation);
                }
                else
                {
                    transform.position = position;
                }
            }

            SetInRange(visible);
            return visible;
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
