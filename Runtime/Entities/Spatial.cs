using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Stores independent source poses and applies reference-relative placement, attachments and range suppression.
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
    /// Add optional Smoothing and Prediction traits on the same Ghost root to modify presentation.
    /// Timed inputs share one SDK clock; duplicate or older timestamps are ignored per channel.
    /// Timestamped stationary updates refresh sample age; untimed identical cached positions do not.
    /// Attach selects a same-realm parent by Key, including before discovery. Local attachment poses use Unity axes
    /// and units; absolute inputs remain cached for Detach. Roots retain their Anchor parents and independent lifetimes.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Spatial")]
    public sealed class Spatial : MonoBehaviour
    {
        private readonly PoseData _data = new PoseData();
        private Double3 _presentationPosition;
        private Quaternion _presentationRotation = Quaternion.identity;
        private bool _hasPresentation;
        private readonly PresentationTuning _tuning = new PresentationTuning();
        private readonly PresentationSuppression _suppression = new PresentationSuppression();
        private RotationSpace _rotationSpace;
        private CoordinateSystem? _earthCenteredBodyAxes;
        private Key? _attachedTo;
        private Vector3 _attachmentPosition;
        private Quaternion _attachmentRotation = Quaternion.identity;

        /// <summary>Gets the latest accepted position sample time, or null for arrival-timed input.</summary>
        /// <value>The original SDK timestamp for the stored position; null before timed input or after a spatial clock reset.</value>
        public Timestamp? PositionTime => _data.PositionTime;

        /// <summary>Gets the latest accepted orientation sample time, or null for untimestamped input.</summary>
        /// <value>The original SDK timestamp for the stored rotation; null before timed input or after a spatial clock reset.</value>
        public Timestamp? RotationTime => _data.RotationTime;

        /// <summary>Resets smoothing, estimated prediction and live-tuning transitions after an intentional discontinuity.</summary>
        /// <remarks>Retains raw pose, supplied motion and component settings. Supply the new pose before calling this.</remarks>
        public void ResetPresentation()
        {
            _hasPresentation = false;
            ResetTuning();
            Smoothing smoothing = GetComponent<Smoothing>();
            if (smoothing != null)
            {
                smoothing.Reset();
            }

            Prediction prediction = GetComponent<Prediction>();
            if (prediction != null)
            {
                prediction.Reset();
            }
        }

        internal double PositionReceivedTime => _data.PositionReceivedTime;
        internal long PositionVersion => _data.PositionVersion;
        internal Double3 PresentationPosition => _hasPresentation ? _presentationPosition : Position;
        internal Quaternion PresentationRotation => _hasPresentation ? _presentationRotation : Rotation;

        internal void ResetTuning()
        {
            _tuning.Reset();
        }

        // Compose optional behavior only after all SDK readers, before capturing the shared reference.
        internal void PreparePresentation(SpatialClock clock, double timestamp)
        {
            if (!enabled || AttachedTo.HasValue)
            {
                ResetPresentation();
                return;
            }

            Prediction prediction = GetComponent<Prediction>();
            Smoothing smoothing = GetComponent<Smoothing>();
            PresentationSettings settings = new PresentationSettings(prediction, smoothing);
            if (prediction != null)
            {
                prediction.Prepare(this, timestamp);
            }

            PresentationPose previous = default(PresentationPose);
            if (_tuning.Changed(settings))
            {
                previous = PreparePose(clock, timestamp, prediction, smoothing, _tuning.Settings, true);
            }

            PresentationPose current = PreparePose(clock, timestamp, prediction, smoothing, settings, false);
            current = _tuning.Apply(previous, current, settings, timestamp);
            _presentationPosition = current.Position;
            _presentationRotation = current.Rotation;
            _hasPresentation = true;
        }

        private PresentationPose PreparePose(SpatialClock clock, double timestamp, Prediction prediction,
            Smoothing smoothing, PresentationSettings settings, bool preview)
        {
            Double3 motion = default(Double3);
            Double3 position = prediction != null
                ? prediction.Project(this, clock, timestamp, settings.MaximumExtrapolation, out motion) : Position;
            PresentationPose pose = new PresentationPose(position, Rotation);

            // Keep direct (disabled) channels current too, so enabling a trait has fresh filter history.
            return smoothing != null ? smoothing.Prepare(this, pose, timestamp, motion, settings, preview) : pose;
        }

        internal void ResetTime(double timestamp)
        {
            _data.ResetTime(timestamp);
            ResetPresentation();
            Prediction prediction = GetComponent<Prediction>();
            if (prediction != null)
            {
                prediction.ResetTime();
            }
        }

        private void ResetRotationSmoothing()
        {
            // Corrections expressed in the former attitude basis cannot be reused in the new one.
            _tuning.ResetRotation();
            Smoothing smoothing = GetComponent<Smoothing>();
            if (smoothing != null)
            {
                smoothing.ResetRotation();
            }
        }

        /// <summary>
        /// Gets the last Cartesian source position or absolute ECEF metres after geographic/ECEF input; valid after HasPosition is true.
        /// </summary>
        /// <value>The raw accepted position, unaffected by prediction, smoothing or the projected Unity transform.</value>
        public Double3 Position
        {
            get
            {
                return _data.Position;
            }
        }

        /// <summary>
        /// Gets the last normalized quaternion in RotationSpace; meaningful after HasRotation becomes true.
        /// </summary>
        /// <value>The raw accepted orientation, unaffected by smoothing or reference-frame alignment.</value>
        public Quaternion Rotation
        {
            get
            {
                return _data.Rotation;
            }
        }

        /// <summary>Gets the basis of Rotation: configured source axes, local east/up/north, or body-to-ECEF.</summary>
        /// <value>The representation selected by the last accepted rotation setter.</value>
        public RotationSpace RotationSpace => _rotationSpace;

        internal CoordinateSystem? EarthCenteredBodyAxes => _earthCenteredBodyAxes;

        /// <summary>
        /// Gets whether a Cartesian or geographic position has been supplied.
        /// </summary>
        /// <value>True after the first accepted position input; remains true while attached or temporarily unavailable.</value>
        public bool HasPosition
        {
            get
            {
                return _data.HasPosition;
            }
        }

        /// <summary>
        /// Gets whether absolute source orientation has been supplied; when detached, missing orientation leaves the root's rotation alone.
        /// </summary>
        /// <value>True after the first accepted rotation input, independently of position availability.</value>
        public bool HasRotation
        {
            get
            {
                return _data.HasRotation;
            }
        }

        /// <summary>
        /// Gets whether the last projection permits presentation, independently of source availability.
        /// </summary>
        /// <value>True when spatial presentation is permitted; initially true until the first projection evaluates this root.</value>
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
        /// <value>The same-realm parent identity, or null when absolute pose projection is selected.</value>
        public Key? AttachedTo => _attachedTo;

        /// <summary>Attaches to a same-realm entity at a Unity-local position offset with no relative rotation.</summary>
        /// <param name="parent">The complete parent identity; the entity may be discovered later.</param>
        /// <param name="localPosition">Finite offset in Unity units: X right, Y up and Z forward.</param>
        /// <remarks>Equivalent to Attach(parent, localPosition, Quaternion.identity).</remarks>
        /// <exception cref="ArgumentException">The parent identity is incomplete or identifies this Ghost.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An offset coordinate is not finite.</exception>
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
        /// <exception cref="ArgumentOutOfRangeException">An offset or quaternion component is nonfinite, or the relative quaternion has zero length.</exception>
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
            ResetPresentation();
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
                ResetPresentation();
            }
        }

        /// <summary>
        /// Supplies a shared Cartesian position in ReferenceFrame.Coordinates without changing rotation.
        /// </summary>
        /// <param name="position">Finite source XYZ coordinates in the Cartesian world's shared units.</param>
        /// <param name="sampleTime">Optional SDK observation time; duplicate or older timed positions are ignored.</param>
        /// <remarks>Use with Cartesian space, or without a reference. Stores input for the next realm projection.</remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">An input coordinate is not finite.</exception>
        public void SetCartesianPosition(Double3 position, Timestamp? sampleTime = null)
        {
            StorePosition(position, sampleTime);
        }

        /// <summary>Supplies absolute ECEF XYZ metres for a Geographic reference, independently of attitude.</summary>
        /// <param name="position">Finite Earth-centered, Earth-fixed coordinates in metres, not latitude/longitude/height.</param>
        /// <param name="sampleTime">Optional SDK observation time; duplicate or older timed positions are ignored.</param>
        /// <remarks>Stores input for the next projection. No conversion through ReferenceFrame.Coordinates is applied.</remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">An input coordinate is not finite.</exception>
        public void SetEarthCenteredPosition(Double3 position, Timestamp? sampleTime = null)
        {
            StorePosition(position, sampleTime);
        }

        /// <summary>Supplies a WGS84 position, storing ECEF metres for use with a Geographic reference.</summary>
        /// <param name="position">The WGS84 position with ellipsoidal height in metres.</param>
        /// <param name="sampleTime">Optional SDK observation time; duplicate or older timed positions are ignored.</param>
        /// <remarks>Requires Geographic projection. Retains attitude; local geographic attitude follows the new tangent plane.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The geographic input is invalid or conversion produces nonfinite ECEF coordinates.</exception>
        public void SetGeographicPosition(GeoPosition position, Timestamp? sampleTime = null)
        {
            StorePosition(position.ToEarthCentered(), sampleTime);
        }

        /// <summary>
        /// Supplies orientation without changing position. In Geographic space, use local tangent attitude
        /// in ReferenceFrame.Coordinates; otherwise use the shared Cartesian frame.
        /// </summary>
        /// <param name="rotation">The finite nonzero source quaternion; normalized before storage.</param>
        /// <param name="sampleTime">Optional SDK observation time; duplicate or older timed orientations are ignored.</param>
        /// <remarks>Sets RotationSpace to Source. Position remains unchanged; use SetGeographicRotation for named angles.</remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">The quaternion is not finite or has zero length.</exception>
        public void SetSourceRotation(Quaternion rotation, Timestamp? sampleTime = null)
        {
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            if (!_data.SetRotation(normalized, sampleTime))
            {
                return;
            }

            if (_rotationSpace != Emas.RotationSpace.Source)
            {
                ResetRotationSmoothing();
            }

            _rotationSpace = Emas.RotationSpace.Source;
            _earthCenteredBodyAxes = null;
        }

        /// <summary>Supplies local geographic yaw, pitch and roll in degrees for a Geographic reference.</summary>
        /// <param name="yawDegrees">Heading clockwise from true north about local up.</param>
        /// <param name="pitchDegrees">Nose-up pitch about the heading's body-right axis.</param>
        /// <param name="rollDegrees">Right-wing-down bank about the pitched body's forward axis.</param>
        /// <param name="sampleTime">Optional SDK observation time; duplicate or older timed orientations are ignored.</param>
        /// <remarks>
        /// Intrinsic yaw, then pitch, then roll; zero faces north with model +Z forward and +Y up.
        /// Stores a normalized east/up/north quaternion with RotationSpace.Geographic, independently of Coordinates.
        /// Angles wrap modulo 360 before float conversion. Position can arrive later; moving it preserves this local attitude.
        /// </remarks>
        /// <exception cref="System.ArgumentOutOfRangeException">An angle is not finite.</exception>
        public void SetGeographicRotation(double yawDegrees, double pitchDegrees, double rollDegrees, Timestamp? sampleTime = null)
        {
            Quaternion rotation = SpatialMath.GeographicRotation(yawDegrees, pitchDegrees, rollDegrees);
            if (!_data.SetRotation(rotation, sampleTime))
            {
                return;
            }

            if (_rotationSpace != Emas.RotationSpace.Geographic)
            {
                ResetRotationSmoothing();
            }

            _rotationSpace = Emas.RotationSpace.Geographic;
            _earthCenteredBodyAxes = null;
        }

        /// <summary>Supplies a body-to-ECEF quaternion for a Geographic reference, independently of position.</summary>
        /// <param name="rotation">Active rotation mapping source body XYZ vectors into ECEF XYZ.</param>
        /// <param name="bodyAxes">Signed body axes mapped to Unity right/up/forward. Null uses X forward, Y right, Z down (NED mapping).</param>
        /// <param name="sampleTime">Optional SDK observation time; duplicate or older timed orientations are ignored.</param>
        /// <remarks>Sets RotationSpace to EarthCentered. Body axes must be right-handed; position updates retain this global attitude.</remarks>
        /// <exception cref="System.ArgumentException">The body axes are invalid or not right-handed.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">The quaternion is not finite or has zero length.</exception>
        public void SetEarthCenteredRotation(Quaternion rotation, CoordinateSystem? bodyAxes = null, Timestamp? sampleTime = null)
        {
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            CoordinateSystem axes = CoordinateSystem.RequireRightHandedBodyAxes(bodyAxes);
            if (!_data.SetRotation(normalized, sampleTime))
            {
                return;
            }

            if (_rotationSpace != Emas.RotationSpace.EarthCentered || !_earthCenteredBodyAxes.Equals(axes))
            {
                ResetRotationSmoothing();
            }

            _rotationSpace = Emas.RotationSpace.EarthCentered;
            _earthCenteredBodyAxes = axes;
        }

        private void StorePosition(Double3 position, Timestamp? sampleTime)
        {
            _data.SetPosition(position, sampleTime, Time.realtimeSinceStartupAsDouble);
        }

        // Called by the realm after every trait has updated and the shared reference is captured.
        internal bool ApplyProjection(ReferenceFrame.Projection projection)
        {
            Vector3 position = default(Vector3);
            // A predicted or smoothed pose must not bring an out-of-range raw observation back into presentation.
            bool observationVisible = HasPosition && projection.TryToUnityPosition(Position, out position);
            bool visible = observationVisible && projection.TryToUnityPosition(PresentationPosition, out position);
            if (!observationVisible)
            {
                // A missing or out-of-range observation invalidates history. A valid observation whose
                // filtered presentation is outside range must keep settling, without a reset-and-snap loop.
                ResetPresentation();
            }

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
            ResetPresentation();
            // Temporarily hiding the hierarchy must not release range suppression on reactivation.
            if (!enabled)
            {
                SetInRange(true);
            }
        }
    }
}
