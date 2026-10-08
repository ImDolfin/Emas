using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Stores independent pose and optional motion updates with opt-in smoothing for world or realm-relative placement.
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
        [Tooltip("Position smoothing time constant in unscaled seconds. Zero applies SDK positions directly, independently of rotation.")]
        [Min(0)]
        [SerializeField] private float _positionSmoothingTime;
        [Tooltip("Rotation smoothing time constant in unscaled seconds. Zero applies SDK rotations directly. On a followed Ghost, this controls how quickly reference orientation repositions other Ghosts when Follow Rotation is enabled.")]
        [Min(0)]
        [SerializeField] private float _rotationSmoothingTime;

        private readonly PoseSmoother _smoother = new PoseSmoother();
        private readonly PresentationSuppression _suppression = new PresentationSuppression();
        private RotationSpace _rotationSpace;
        private CoordinateSystem? _earthCenteredBodyAxes;
        private Key? _attachedTo;
        private Vector3 _attachmentPosition;
        private Quaternion _attachmentRotation = Quaternion.identity;

        /// <summary>Gets or sets the position smoothing time constant in unscaled seconds; zero disables position smoothing (the default).</summary>
        /// <remarks>Larger values reduce positional jitter and increase lag. Optional velocity and acceleration predict motion
        /// for at most this many seconds after a changed position input, while allowing corrections in every direction. Raw Position remains unchanged.
        /// Changing this setting resets only position smoothing on the next projection; rotation smoothing retains its history.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The time is negative or nonfinite.</exception>
        public float PositionSmoothingTime
        {
            get => _positionSmoothingTime;
            set
            {
                RequireNonnegative(value, nameof(value));
                if (_positionSmoothingTime != value)
                {
                    _positionSmoothingTime = value;
                    _smoother.ResetPosition();
                }
            }
        }

        /// <summary>Gets or sets the rotation smoothing time constant in unscaled seconds; zero disables rotation smoothing (the default).</summary>
        /// <remarks>Larger values reduce angular jitter and increase lag. Raw Rotation remains unchanged.
        /// Changing this setting resets only rotation smoothing on the next projection; position smoothing retains its history.
        /// When ReferenceFrame follows this Ghost's rotation, its smoothed orientation also controls repositioning of other Ghosts.
        /// Set this to zero for immediate reference orientation changes while independently smoothing position.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The time is negative or nonfinite.</exception>
        public float RotationSmoothingTime
        {
            get => _rotationSmoothingTime;
            set
            {
                RequireNonnegative(value, nameof(value));
                if (_rotationSmoothingTime != value)
                {
                    _rotationSmoothingTime = value;
                    _smoother.ResetRotation();
                }
            }
        }

        /// <summary>Gets the last supplied Cartesian velocity or ECEF metres per second; valid when HasVelocity is true.</summary>
        public Double3 Velocity => _smoother.Velocity;

        /// <summary>Gets whether an SDK velocity has been supplied for motion-assisted position smoothing.</summary>
        public bool HasVelocity => _smoother.HasVelocity;

        /// <summary>Supplies velocity in the shared Cartesian source axes and units per second, independently of position.</summary>
        /// <remarks>Assists position smoothing with bounded motion prediction; corrections in every direction remain accepted.
        /// Update velocity when stopping or reversing. Prediction ends one PositionSmoothingTime after the latest changed position input.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is nonfinite.</exception>
        public void SetCartesianVelocity(Double3 velocity)
        {
            _smoother.SetVelocity(velocity);
        }

        /// <summary>Supplies an ECEF velocity vector in metres per second for Geographic smoothing.</summary>
        /// <remarks>Use ECEF XYZ, not body or local tangent axes. Does not change position or rotation.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is nonfinite.</exception>
        public void SetEarthCenteredVelocity(Double3 velocity)
        {
            _smoother.SetVelocity(velocity);
        }

        /// <summary>Supplies ENU velocity in metres per second, converting it to ECEF at the supplied tangent origin.</summary>
        /// <param name="velocity">X east, Y north, Z up, in metres per second.</param>
        /// <param name="origin">The SDK's ENU tangent origin; use the entity's location for entity-local ENU.</param>
        /// <remarks>Stores an ECEF vector independently of position and reference axes; does not change pose.</remarks>
        public void SetGeographicVelocity(Double3 velocity, GeoPosition origin)
        {
            _smoother.SetVelocity(origin.ToEarthCenteredVector(velocity));
        }

        /// <summary>Clears velocity and disables motion prediction; position smoothing continues accepting all corrections.</summary>
        public void ClearVelocity()
        {
            _smoother.ClearVelocity();
        }

        /// <summary>Gets the last supplied Cartesian acceleration or ECEF metres per second squared; valid when HasAcceleration is true.</summary>
        public Double3 Acceleration => _smoother.Acceleration;

        /// <summary>Gets whether optional acceleration has been supplied to assist prediction when velocity is available.</summary>
        public bool HasAcceleration => _smoother.HasAcceleration;

        /// <summary>Supplies linear acceleration in shared Cartesian source axes and units per second squared.</summary>
        /// <remarks>Assists position smoothing only when velocity is also present. Does not change pose or velocity.</remarks>
        public void SetCartesianAcceleration(Double3 acceleration)
        {
            _smoother.SetAcceleration(acceleration);
        }

        /// <summary>Supplies ECEF linear acceleration in metres per second squared for Geographic smoothing.</summary>
        /// <remarks>Supply kinematic acceleration, with gravity already removed from accelerometer measurements.</remarks>
        public void SetEarthCenteredAcceleration(Double3 acceleration)
        {
            _smoother.SetAcceleration(acceleration);
        }

        /// <summary>Supplies ENU linear acceleration in metres per second squared, converting it at the supplied tangent origin.</summary>
        /// <param name="acceleration">X east, Y north, Z up, in metres per second squared; gravity must already be removed.</param>
        /// <param name="origin">The SDK's ENU tangent origin; use the entity's location for entity-local ENU.</param>
        public void SetGeographicAcceleration(Double3 acceleration, GeoPosition origin)
        {
            _smoother.SetAcceleration(origin.ToEarthCenteredVector(acceleration));
        }

        /// <summary>Clears optional acceleration without changing velocity or smoothing history.</summary>
        public void ClearAcceleration()
        {
            _smoother.ClearAcceleration();
        }

        /// <summary>Discards smoothing history so the next projection snaps to the latest SDK pose, including after a teleport.</summary>
        /// <remarks>Retains all input channels, attachment and smoothing settings.</remarks>
        public void ResetSmoothing()
        {
            _smoother.Reset();
        }

        internal Double3 PresentationPosition => _smoother.PresentationPosition;
        internal Quaternion PresentationRotation => _smoother.PresentationRotation;

        private static void RequireNonnegative(float value, string parameter)
        {
            if (!SpatialMath.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameter, "The value must be finite and nonnegative.");
            }
        }

        // Filter absolute source data before capturing the shared reference; never filter origin movement.
        internal void PrepareSmoothing(double timestamp)
        {
            if (!enabled || AttachedTo.HasValue)
            {
                ResetSmoothing();
                return;
            }

            _smoother.Prepare(_positionSmoothingTime, _rotationSmoothingTime, timestamp);
        }

        /// <summary>
        /// Gets the last Cartesian source position or absolute ECEF metres after geographic/ECEF input; valid after HasPosition is true.
        /// </summary>
        public Double3 Position
        {
            get
            {
                return _smoother.Position;
            }
        }

        /// <summary>
        /// Gets the last normalized quaternion in RotationSpace; meaningful after HasRotation becomes true.
        /// </summary>
        public Quaternion Rotation
        {
            get
            {
                return _smoother.Rotation;
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
                return _smoother.HasPosition;
            }
        }

        /// <summary>
        /// Gets whether absolute source orientation has been supplied; when detached, missing orientation leaves the root's rotation alone.
        /// </summary>
        public bool HasRotation
        {
            get
            {
                return _smoother.HasRotation;
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
                return _suppression.IsInRange;
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
                _smoother.ResetRotation();
            }
            _smoother.SetRotation(normalized);
            _rotationSpace = Emas.RotationSpace.Source;
            _earthCenteredBodyAxes = null;
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
                _smoother.ResetRotation();
            }
            _smoother.SetRotation(rotation);
            _rotationSpace = Emas.RotationSpace.Geographic;
            _earthCenteredBodyAxes = null;
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
                _smoother.ResetRotation();
            }
            _smoother.SetRotation(normalized);
            _rotationSpace = Emas.RotationSpace.EarthCentered;
            _earthCenteredBodyAxes = axes;
        }

        private void StorePosition(Double3 position)
        {
            _smoother.SetPosition(position, Time.realtimeSinceStartupAsDouble);
        }

        // Called by the realm after every trait has updated and the shared reference is captured.
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
            if (!value)
            {
                ResetSmoothing();
            }

            _suppression.SetInRange(this, value);
        }

        private void OnDestroy()
        {
            _suppression.Restore();
        }

        private void OnEnable()
        {
            if (!IsInRange)
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
                SetInRange(true);
            }
        }
    }
}
