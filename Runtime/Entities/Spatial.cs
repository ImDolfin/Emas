using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Stores independent pose and optional velocity updates with opt-in smoothing for world or realm-relative placement.
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
    /// Attach selects a same-realm parent by Key, including before discovery. Local attachment poses use Unity axes
    /// and units; absolute inputs remain cached for Detach. Roots retain their Anchor parents and independent lifetimes.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Spatial")]
    public sealed class Spatial : MonoBehaviour
    {
        [Tooltip("Position and rotation smoothing time constant in unscaled seconds. Zero applies SDK poses directly.")]
        [Min(0)]
        [SerializeField] private float _smoothingTime;
        [Tooltip("Minimum SDK speed for rejecting position corrections opposite velocity, in source units per second (ECEF metres/second in Geographic space).")]
        [Min(0)]
        [SerializeField] private float _minimumForwardSpeed = 0.1f;

        private Double3 _velocity;
        private bool _hasVelocity;
        private Double3 _smoothedPosition;
        private Quaternion _smoothedRotation = Quaternion.identity;
        private bool _hasSmoothedPosition;
        private bool _hasSmoothedRotation;
        private double _smoothingTimestamp;
        private Double3 _position;
        private Quaternion _rotation = Quaternion.identity;
        private RotationSpace _rotationSpace;
        private CoordinateSystem? _earthCenteredBodyAxes;
        private bool _hasPosition;
        private bool _hasRotation;
        private bool _isInRange = true;
        private Key? _attachedTo;
        private Vector3 _attachmentPosition;
        private Quaternion _attachmentRotation = Quaternion.identity;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly HashSet<Renderer> _hiddenRenderers = new HashSet<Renderer>();
        private readonly HashSet<Collider> _hiddenColliders = new HashSet<Collider>();

        /// <summary>Gets or sets the smoothing time constant in unscaled seconds; zero disables smoothing (the default).</summary>
        /// <remarks>Larger values reduce jitter and increase lag. Raw Position and Rotation remain unchanged.
        /// Changing this setting resets smoothing on the next projection.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The time is negative or nonfinite.</exception>
        public float SmoothingTime
        {
            get => _smoothingTime;
            set
            {
                RequireNonnegative(value, nameof(value));
                if (_smoothingTime != value)
                {
                    _smoothingTime = value;
                    ResetSmoothing();
                }
            }
        }

        /// <summary>Gets or sets the minimum supplied speed for preventing motion opposite velocity, in source units per second.</summary>
        /// <remarks>Defaults to 0.1. Only applies with smoothing enabled and a nonzero velocity channel.
        /// In Geographic space the unit is ECEF metres per second. Lower speeds permit corrections in any direction.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The speed is negative or nonfinite.</exception>
        public float MinimumForwardSpeed
        {
            get => _minimumForwardSpeed;
            set
            {
                RequireNonnegative(value, nameof(value));
                _minimumForwardSpeed = value;
            }
        }

        /// <summary>Gets the last supplied Cartesian velocity or ECEF metres per second; valid when HasVelocity is true.</summary>
        public Double3 Velocity => _velocity;

        /// <summary>Gets whether an SDK velocity has been supplied for the backward-motion guard.</summary>
        public bool HasVelocity => _hasVelocity;

        /// <summary>Supplies velocity in the shared Cartesian source axes and units per second, independently of position.</summary>
        /// <remarks>Used only by smoothing to reject backward jitter. Does not extrapolate position.
        /// Update velocity when stopping or reversing; ClearVelocity releases the guard.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is nonfinite.</exception>
        public void SetCartesianVelocity(Double3 velocity)
        {
            StoreVelocity(velocity);
        }

        /// <summary>Supplies an ECEF velocity vector in metres per second for Geographic smoothing.</summary>
        /// <remarks>Use ECEF XYZ, not body or local tangent axes. Does not change position or rotation.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is nonfinite.</exception>
        public void SetEarthCenteredVelocity(Double3 velocity)
        {
            StoreVelocity(velocity);
        }

        /// <summary>Clears the velocity channel so smoothing permits position corrections in any direction.</summary>
        public void ClearVelocity()
        {
            _hasVelocity = false;
            _velocity = default(Double3);
        }

        /// <summary>Discards smoothing history so the next projection snaps to the latest SDK pose, including after a teleport.</summary>
        /// <remarks>Retains all input channels, attachment and smoothing settings.</remarks>
        public void ResetSmoothing()
        {
            _hasSmoothedPosition = false;
            _hasSmoothedRotation = false;
        }

        internal Double3 PresentationPosition => _hasSmoothedPosition ? _smoothedPosition : Position;
        internal Quaternion PresentationRotation => _hasSmoothedRotation ? _smoothedRotation : Rotation;

        private static void RequireNonnegative(float value, string parameter)
        {
            if (!SpatialMath.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameter, "The value must be finite and nonnegative.");
            }
        }

        private void StoreVelocity(Double3 velocity)
        {
            ReferenceFrame.ValidatePosition(velocity, nameof(velocity));
            _velocity = velocity;
            _hasVelocity = true;
        }

        // Filter absolute source data before capturing the shared reference; never filter origin movement.
        internal void PrepareSmoothing(double timestamp)
        {
            if (!enabled || AttachedTo.HasValue || !SpatialMath.IsFinite(_smoothingTime) || _smoothingTime <= 0)
            {
                ResetSmoothing();
                return;
            }

            double weight = 1d - Math.Exp(-Math.Max(0d, timestamp - _smoothingTimestamp) / _smoothingTime);
            if (HasPosition)
            {
                _smoothedPosition = _hasSmoothedPosition ? SmoothPosition(weight) : Position;
                _hasSmoothedPosition = true;
            }

            if (HasRotation)
            {
                _smoothedRotation = _hasSmoothedRotation
                    ? Quaternion.Slerp(_smoothedRotation, Rotation, (float)weight) : Rotation;
                _hasSmoothedRotation = true;
            }

            _smoothingTimestamp = timestamp;
        }

        private Double3 SmoothPosition(double weight)
        {
            double x = Position.X - _smoothedPosition.X;
            double y = Position.Y - _smoothedPosition.Y;
            double z = Position.Z - _smoothedPosition.Z;
            if (!SpatialMath.IsFinite(x) || !SpatialMath.IsFinite(y) || !SpatialMath.IsFinite(z))
            {
                return Position;
            }

            double speed = Double3.Distance(Velocity, default(Double3));
            if (HasVelocity && speed > 0d && speed >= _minimumForwardSpeed)
            {
                // Scale before normalization to support finite velocities whose magnitude overflows.
                double largest = Math.Max(Math.Abs(Velocity.X), Math.Max(Math.Abs(Velocity.Y), Math.Abs(Velocity.Z)));
                double vx = Velocity.X / largest;
                double vy = Velocity.Y / largest;
                double vz = Velocity.Z / largest;
                double length = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                vx /= length;
                vy /= length;
                vz /= length;
                double along = x * vx + y * vy + z * vz;
                if (along < 0d)
                {
                    x -= along * vx;
                    y -= along * vy;
                    z -= along * vz;
                }
            }

            double px = _smoothedPosition.X + x * weight;
            double py = _smoothedPosition.Y + y * weight;
            double pz = _smoothedPosition.Z + z * weight;
            return SpatialMath.IsFinite(px) && SpatialMath.IsFinite(py) && SpatialMath.IsFinite(pz)
                ? new Double3(px, py, pz) : Position;
        }

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
        /// Gets whether absolute source orientation has been supplied; when detached, missing orientation leaves the root's rotation alone.
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
        /// False also covers coordinates that cannot be projected to finite Unity floats and attachments awaiting a presentable parent.
        /// </remarks>
        public bool IsInRange
        {
            get
            {
                return _isInRange;
            }
        }

        /// <summary>Gets the requested attachment parent, including while it is missing or cannot be presented.</summary>
        public Key? AttachedTo => _attachedTo;

        /// <summary>Attaches to a same-realm entity at a Unity-local position offset with no relative rotation.</summary>
        /// <param name="parent">The complete parent identity; the entity may be discovered later.</param>
        /// <param name="localPosition">Finite offset in Unity units: X right, Y up and Z forward.</param>
        /// <remarks>Equivalent to Attach(parent, localPosition, Quaternion.identity).</remarks>
        public void Attach(Key parent, Vector3 localPosition)
        {
            Attach(parent, localPosition, Quaternion.identity);
        }

        /// <summary>Uses a same-realm parent's projected pose and a Unity-local offset instead of the cached absolute pose.</summary>
        /// <param name="parent">The complete parent identity; the entity may be discovered later.</param>
        /// <param name="localPosition">Finite offset in Unity units: X right, Y up and Z forward. Parent scale is ignored.</param>
        /// <param name="localRotation">Finite nonzero relative quaternion in Unity local axes; normalized before storage.</param>
        /// <remarks>
        /// Applies on the next realm projection while Spatial is enabled. Attach again to change the parent or offsets.
        /// Missing, unavailable, hidden or cyclic parents suppress presentation without removing this entity or its attachment.
        /// Parent arrival or recovery resolves the attachment automatically. Absolute setters continue caching data for Detach.
        /// </remarks>
        /// <exception cref="ArgumentException">The parent identity is incomplete or identifies this Ghost.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An offset is nonfinite or the relative quaternion has zero length.</exception>
        public void Attach(Key parent, Vector3 localPosition, Quaternion localRotation)
        {
            if (string.IsNullOrEmpty(parent.AnchorId) || !parent.Kind.IsValid || string.IsNullOrEmpty(parent.EntityId))
            {
                throw new ArgumentException("An attachment parent requires an Anchor, Kind and entity ID.", nameof(parent));
            }

            Ghost ghost = GetComponent<Ghost>();
            if (ghost != null && ghost.Key == parent)
            {
                throw new ArgumentException("An entity cannot attach to itself.", nameof(parent));
            }

            if (!SpatialMath.IsFinite(localPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(localPosition), "The attachment position must be finite.");
            }

            Quaternion rotation = SpatialMath.NormalizeRotation(localRotation, nameof(localRotation));
            _attachedTo = parent;
            _attachmentPosition = localPosition;
            _attachmentRotation = rotation;
            ResetSmoothing();
        }

        /// <summary>Clears the attachment so the next realm projection uses the latest cached absolute position and rotation.</summary>
        /// <remarks>
        /// Safe while waiting for a parent or already detached. Does not synthesize an absolute pose from the attachment;
        /// publish a current SDK pose before detaching for a continuous handoff. Missing absolute position suppresses presentation.
        /// </remarks>
        public void Detach()
        {
            if (_attachedTo.HasValue)
            {
                _attachedTo = null;
                ResetSmoothing();
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
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            if (_rotationSpace != Emas.RotationSpace.Source)
            {
                _hasSmoothedRotation = false;
            }
            _rotation = normalized;
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
            if (_rotationSpace != Emas.RotationSpace.Geographic)
            {
                _hasSmoothedRotation = false;
            }
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
            if (_rotationSpace != Emas.RotationSpace.EarthCentered || !_earthCenteredBodyAxes.Equals(axes))
            {
                _hasSmoothedRotation = false;
            }
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
            bool visible = HasPosition && projection.TryToUnityPosition(Position, out position)
                && projection.TryToUnityPosition(PresentationPosition, out position);
            if (visible)
            {
                // Assign world pose so anchor transforms do not introduce a second offset.
                if (HasRotation)
                {
                    Quaternion rotation = _earthCenteredBodyAxes.HasValue
                        ? projection.ToUnityEarthCenteredRotation(PresentationRotation, _earthCenteredBodyAxes.Value)
                        : _rotationSpace == Emas.RotationSpace.Geographic
                            ? projection.ToUnityGeographicRotation(PresentationRotation, PresentationPosition)
                            : projection.ToUnityRotation(PresentationRotation, PresentationPosition);
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

        internal bool ApplyAttachment(ReferenceFrame.Projection projection, Transform parent)
        {
            Vector3 parentPosition = parent.position;
            Double3 offset = SpatialMath.Rotate(parent.rotation,
                new Double3(_attachmentPosition.x, _attachmentPosition.y, _attachmentPosition.z));
            Double3 position = new Double3(parentPosition.x, parentPosition.y, parentPosition.z) + offset;
            Vector3 unityPosition;
            bool visible = projection.TryUnityPlacement(position, out unityPosition);
            if (visible)
            {
                Quaternion rotation = SpatialMath.NormalizeRotation(parent.rotation * _attachmentRotation, "rotation");
                transform.SetPositionAndRotation(unityPosition, rotation);
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

            ResetSmoothing();

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
            ResetSmoothing();
            // Temporarily hiding the hierarchy must not release range suppression on reactivation.
            if (!enabled)
            {
                _isInRange = true;
                RestorePresentation();
            }
        }
    }
}
