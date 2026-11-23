using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Stores independent source poses and applies reference-relative placement, attachments and range suppression.</summary>
    /// <remarks>
    /// Place on the Ghost root. While enabled, realm projection owns its world pose; disable to release placement
    /// and range suppression. Setters retain raw double-precision input for the next projection. Timed channels
    /// share one SDK clock and reject older or duplicate samples independently. Optional Smoothing and Extrapolation
    /// modify presentation only. Repeating identical untimed pose input retains its observation age;
    /// provide SDK timestamps to identify fresh stationary observations.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Spatial")]
    public sealed class Spatial : MonoBehaviour
    {
        private readonly PoseData _data = new PoseData();
        private readonly PoseHistory _history = new PoseHistory();
        private readonly PosePresenter _presenter = new PosePresenter();
        private readonly PresentationSuppression _suppression = new PresentationSuppression();
        private bool _directPose;
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

        /// <summary>Gets the last Cartesian source position or absolute ECEF metres after geographic/ECEF input; valid after HasPosition is true.</summary>
        /// <value>The raw accepted position, unaffected by extrapolation, smoothing or the projected Unity transform.</value>
        public Double3 Position
        {
            get
            {
                return _data.Position;
            }
        }

        /// <summary>Gets the last normalized quaternion in RotationSpace; meaningful after HasRotation becomes true.</summary>
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

        /// <summary>Gets whether a Cartesian or geographic position has been supplied.</summary>
        /// <value>True after the first accepted position input; remains true while attached or temporarily unavailable.</value>
        public bool HasPosition
        {
            get
            {
                return _data.HasPosition;
            }
        }

        /// <summary>Gets whether absolute source orientation has been supplied; when detached, missing orientation leaves the root's rotation alone.</summary>
        /// <value>True after the first accepted rotation input, independently of position availability.</value>
        public bool HasRotation
        {
            get
            {
                return _data.HasRotation;
            }
        }

        /// <summary>Gets whether the last projection permits presentation, independently of source availability.</summary>
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

        internal double PositionReceivedTime => _data.PositionReceivedTime;

        internal double RotationReceivedTime => _data.RotationReceivedTime;

        internal PoseHistory History => _history;

        internal long PositionVersion => _data.PositionVersion;

        internal long RotationVersion => _data.RotationVersion;

        internal Double3 PresentationPosition => _presenter.HasPose ? _presenter.Position : Position;

        internal Quaternion PresentationRotation => _presenter.HasPose ? _presenter.Rotation : Rotation;

        internal CoordinateSystem? EarthCenteredBodyAxes => _earthCenteredBodyAxes;

        internal bool IsPoseProcessingBypassed => _directPose;

        /// <summary>Projects the latest accepted absolute observation into Unity world space without prediction or smoothing.</summary>
        /// <param name="pose">The observed Unity pose on success; identity rotation when no attitude has been received. Undefined on failure.</param>
        /// <returns>True when this Spatial belongs to a tracked Ghost and its observation can be projected through the current reference and range limit.</returns>
        /// <remarks>
        /// Call on Unity's main thread after the Realm update to compare with the presented transform. Uses the cached,
        /// potentially predicted or smoothed reference, including for the followed Ghost itself. Position and rotation
        /// retain their independent observation times. Reads never advance playback, prediction or filters, and retain
        /// absolute input while attached or disabled. Returns false for missing position/reference or incompatible coordinates.
        /// </remarks>
        public bool TryGetObservedUnityPose(out Pose pose)
        {
            // Debug reads need input from a tracked Ghost. They do not update the followed reference.
            pose = default(Pose);
            Ghost ghost = GetComponent<Ghost>();
            if (!HasPosition || ghost == null || ghost.Anchor == null)
            {
                return false;
            }

            try
            {
                // Use the same saved reference for raw input and the displayed root so they can be compared.
                ReferenceFrame frame = ghost.Anchor.Realm.ReferenceFrame;
                ReferenceFrame.Projection projection = frame != null ? frame.Capture()
                    : new ReferenceFrame.Projection(true, default(Double3), Vector3.zero, Quaternion.identity, CoordinateSystem.Unity, null);
                if (!projection.TryToUnityPosition(Position, out Vector3 position))
                {
                    return false;
                }

                // If no source rotation exists, return identity rather than the displayed transform rotation.
                pose = new Pose(position, HasRotation ? ProjectRotation(projection, Rotation, Position) : Quaternion.identity);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Resets smoothing, estimated extrapolation and live-tuning transitions after an intentional discontinuity.</summary>
        /// <remarks>Retains raw pose, supplied motion and component settings. Clears pose playback history and seeds
        /// it with the current observation. Supply the new pose before calling this.</remarks>
        public void ResetPresentation()
        {
            // Reset all filters together, then restart playback from the stored observations.
            _presenter.Reset();
            _directPose = false;
            ResetPoseHistory();
            Smoothing smoothing = GetComponent<Smoothing>();
            if (smoothing != null)
            {
                smoothing.Reset();
            }

            Extrapolation extrapolation = GetComponent<Extrapolation>();
            if (extrapolation != null)
            {
                extrapolation.Reset();
            }
        }

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

            // Check every input before saving the attachment, then clear the old world-pose filter state.
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

        /// <summary>Supplies a shared Cartesian position in ReferenceFrame.Coordinates without changing rotation.</summary>
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
            // Check the packet time before changing rotation axes. Rejected packets must leave axes and filters unchanged.
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            bool representationChanged = _rotationSpace != Emas.RotationSpace.Source;
            if (!_data.SetRotation(normalized, sampleTime, Time.realtimeSinceStartupAsDouble, representationChanged))
            {
                return;
            }

            if (representationChanged)
            {
                ResetRotationSmoothing();
            }

            _rotationSpace = Emas.RotationSpace.Source;
            _earthCenteredBodyAxes = null;
            StoreRotationHistory(sampleTime);
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
            // Store yaw, pitch and roll using local geographic axes, regardless of the source axis settings.
            Quaternion rotation = SpatialMath.GeographicRotation(yawDegrees, pitchDegrees, rollDegrees);
            bool representationChanged = _rotationSpace != Emas.RotationSpace.Geographic;
            if (!_data.SetRotation(rotation, sampleTime, Time.realtimeSinceStartupAsDouble, representationChanged))
            {
                return;
            }

            if (representationChanged)
            {
                ResetRotationSmoothing();
            }

            _rotationSpace = Emas.RotationSpace.Geographic;
            _earthCenteredBodyAxes = null;
            StoreRotationHistory(sampleTime);
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
            // Changing body axes clears old corrections even when the rotation still uses ECEF.
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            CoordinateSystem axes = CoordinateSystem.RequireRightHandedBodyAxes(bodyAxes);
            bool representationChanged = _rotationSpace != Emas.RotationSpace.EarthCentered || !_earthCenteredBodyAxes.Equals(axes);
            if (!_data.SetRotation(normalized, sampleTime, Time.realtimeSinceStartupAsDouble, representationChanged))
            {
                return;
            }

            if (representationChanged)
            {
                ResetRotationSmoothing();
            }

            _rotationSpace = Emas.RotationSpace.EarthCentered;
            _earthCenteredBodyAxes = axes;
            StoreRotationHistory(sampleTime);
        }

        internal void ResetTuning()
        {
            _presenter.ResetTuning();
        }

        // Apply prediction and smoothing after all SDK readers run and before saving the shared reference.
        internal void PreparePresentation(SpatialClock clock, double timestamp, float interpolationDelay, CoordinateSystem sourceAxes,
            bool directPose = false)
        {
            // Attached roots get their displayed pose from the parent. Clear their separate prediction state.
            if (!enabled || AttachedTo.HasValue)
            {
                ResetPresentation();
                return;
            }

            if (directPose)
            {
                // Discard old corrections once on entry. Repeated distant updates only copy accepted input.
                if (!_directPose)
                {
                    ResetPresentation();
                    _directPose = true;
                }

                _presenter.Observe(this);
                return;
            }

            if (_directPose)
            {
                // Returning to processed presentation must start from current input, not a stale nearby filter.
                ResetPresentation();
            }

            _presenter.Prepare(this, clock, timestamp, interpolationDelay, sourceAxes);
        }

        internal void ResetTime(double timestamp)
        {
            _data.ResetTime(timestamp);
            _history.Clear();
            ResetPresentation();
            Extrapolation extrapolation = GetComponent<Extrapolation>();
            if (extrapolation != null)
            {
                extrapolation.ResetTime();
            }
        }

        // Called by the realm after every trait has updated and the shared reference is captured.
        internal bool ApplyProjection(ReferenceFrame.Projection projection)
        {
            Vector3 position = default(Vector3);
            // Check the raw observation at playback time. Comparing the newest packet with a delayed
            // reference can hide even the origin if it moved farther than the range limit during the delay.
            // Prediction and smoothing must not display an observation that is outside the range limit.
            Double3 observation = _presenter.HasPose ? _presenter.Observation : Position;
            bool observationVisible = HasPosition && projection.TryToUnityPosition(observation, out position);
            // Direct poses are the observation, so reuse its projection instead of converting the same point twice.
            bool visible = observationVisible && (_directPose || projection.TryToUnityPosition(PresentationPosition, out position));
            if (!observationVisible)
            {
                // Clear filter state when the observation is missing or outside range. If only the
                // filtered pose is outside range, let it keep moving toward the valid input instead of resetting it.
                ResetPresentation();
            }

            if (visible)
            {
                // Set the world pose so the anchor transform does not add another offset.
                if (HasRotation)
                {
                    transform.SetPositionAndRotation(position, ProjectRotation(projection, PresentationRotation, PresentationPosition));
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
            // Attachment offsets use local Unity units. Apply the parent rotation, but ignore its scale.
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

        private Quaternion ProjectRotation(ReferenceFrame.Projection projection, Quaternion rotation, Double3 position)
        {
            return _earthCenteredBodyAxes.HasValue
                ? projection.ToUnityEarthCenteredRotation(rotation, _earthCenteredBodyAxes.Value)
                : _rotationSpace == Emas.RotationSpace.Geographic
                    ? projection.ToUnityGeographicRotation(rotation, position)
                    : projection.ToUnityRotation(rotation, position);
        }

        private void ResetPoseHistory()
        {
            // After a reset, start the new history with the samples we still have.
            _history.ClearPosition();
            _history.ClearRotation();
            if (PositionTime.HasValue)
            {
                _history.AddPosition(Position, PositionTime.Value);
            }

            if (RotationTime.HasValue)
            {
                _history.AddRotation(Rotation, RotationTime.Value);
            }
        }

        private void ResetRotationSmoothing()
        {
            // Clear corrections that were calculated using the old rotation axes.
            _presenter.ResetRotation();
            _history.ClearRotation();
            Extrapolation extrapolation = GetComponent<Extrapolation>();
            if (extrapolation != null)
            {
                extrapolation.ResetPacketRotation();
            }

            Smoothing smoothing = GetComponent<Smoothing>();
            if (smoothing != null)
            {
                smoothing.ResetRotation();
            }
        }

        private void StorePosition(Double3 position, Timestamp? sampleTime)
        {
            // Check packet times first. Add only accepted samples with timestamps to playback history.
            if (!_data.SetPosition(position, sampleTime, Time.realtimeSinceStartupAsDouble))
            {
                return;
            }

            if (sampleTime.HasValue)
            {
                _history.AddPosition(position, sampleTime.Value);
            }
            else
            {
                _history.ClearPosition();
            }
        }

        private void StoreRotationHistory(Timestamp? sampleTime)
        {
            if (sampleTime.HasValue)
            {
                _history.AddRotation(Rotation, sampleTime.Value);
            }
            else
            {
                _history.ClearRotation();
            }
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
            // Turning the GameObject off and on must not restore components hidden by the range limit.
            if (!enabled)
            {
                SetInRange(true);
            }
        }
    }
}
