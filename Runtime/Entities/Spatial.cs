using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Stores source pose and motion observations, then applies reference-relative placement, attachments and range suppression.</summary>
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
        private Ghost _ghost;
        private readonly GeographicInput _geographic = new GeographicInput();
        private MotionData _motion;
        private MotionProcessor _processor;
        private GeographicPoint _rawGeography;
        private GeographicPoint _sampledGeography;
        private Extrapolation _extrapolation;
        private Filter _filter;

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

        /// <summary>Gets the last supplied Cartesian velocity or ECEF metres per second, also available to Smoothing with extrapolation disabled.</summary>
        /// <value>The accepted SDK velocity, meaningful while <see cref="HasVelocity"/> is true; estimated velocity is not exposed here.</value>
        public Double3 Velocity => _motion != null ? _motion.Velocity : default(Double3);

        /// <summary>Gets whether SDK velocity is available; otherwise timestamped positions may estimate it.</summary>
        /// <value>True after a velocity setter accepts a sample, until supplied velocity is cleared.</value>
        public bool HasVelocity => _motion != null && _motion.HasVelocity;

        /// <summary>Gets the last supplied Cartesian acceleration or ECEF metres per second squared.</summary>
        /// <value>The accepted SDK linear acceleration, meaningful while <see cref="HasAcceleration"/> is true.</value>
        public Double3 Acceleration => _motion != null ? _motion.Acceleration : default(Double3);

        /// <summary>Gets whether optional SDK linear acceleration is available.</summary>
        /// <value>True after an acceleration setter accepts a sample, until acceleration is cleared.</value>
        public bool HasAcceleration => _motion != null && _motion.HasAcceleration;

        /// <summary>Gets the accepted angular velocity in radians per second.</summary>
        /// <value>Converted Unity-body quaternion rates or right-handed ECEF XYZ rates; meaningful while HasAngularVelocity is true.</value>
        /// <remarks>Body input uses X right, Y up and Z forward with Quaternion.AngleAxis signs; default aircraft input becomes
        /// (-pitch rate, yaw rate, -roll rate). Geographic ENU input is converted to ECEF. These are angular vectors, not Euler-angle derivatives.</remarks>
        public Double3 AngularVelocity => _motion != null ? _motion.AngularVelocity : default(Double3);

        /// <summary>Gets whether optional SDK angular velocity is available for orientation extrapolation.</summary>
        /// <value>True after an angular velocity setter accepts a sample, until the channel is cleared.</value>
        public bool HasAngularVelocity => _motion != null && _motion.HasAngularVelocity;

        internal double PositionReceivedTime => _data.PositionReceivedTime;

        internal double RotationReceivedTime => _data.RotationReceivedTime;

        internal bool HasMotionData => _motion != null;

        internal MotionData Motion
        {
            get
            {
                if (_motion == null)
                {
                    _motion = new MotionData(_history);
                }

                return _motion;
            }
        }

        internal MotionProcessor Processor
        {
            get
            {
                if (_processor == null)
                {
                    _processor = new MotionProcessor(this);
                }

                return _processor;
            }
        }

        internal PoseHistory History => _history;

        internal GeographicInput Geographic => _geographic;

        internal long PositionVersion => _data.PositionVersion;

        internal long RotationVersion => _data.RotationVersion;

        internal Double3 PresentationPosition => _presenter.HasPose ? _presenter.Position : Position;

        internal Quaternion PresentationRotation => _presenter.HasPose ? _presenter.Rotation : Rotation;

        internal CoordinateSystem? EarthCenteredBodyAxes => _earthCenteredBodyAxes;

        internal bool IsPoseProcessingBypassed => _directPose;

        internal Extrapolation Extrapolation
        {
            get
            {
                // Unity's null check also detects destruction. Do not cache absence,
                // since a processing component can be added after this root is active.
                if (_extrapolation == null)
                {
                    _extrapolation = GetComponent<Extrapolation>();
                }

                return _extrapolation;
            }
        }

        internal bool HasFilters
        {
            get
            {
                // This only checks presence. PredictionInput still reads the live
                // ordered filter list, including enabled state and settings changes.
                if (_filter == null)
                {
                    _filter = GetComponent<Filter>();
                }

                return _filter != null;
            }
        }

        internal Ghost Ghost
        {
            get
            {
                // A root's Ghost is unique. Resolve lazily because data can arrive before activation.
                if (_ghost == null)
                {
                    _ghost = GetComponent<Ghost>();
                }

                return _ghost;
            }
        }

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

            ResetPrediction();
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
        /// Processing resets only when entering attachment; repeated attached updates only store the local binding.
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

            Ghost ghost = Ghost;
            if (ghost != null && ghost.Key == parent)
            {
                throw new ArgumentException("An entity cannot attach to itself.", nameof(parent));
            }

            if (!SpatialMath.IsFinite(localPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(localPosition), "The attachment position must be finite.");
            }

            // Validate every input before storing it. A bound root never prepares its
            // own pose, so local binding updates need no repeated history/filter reset.
            Quaternion rotation = SpatialMath.NormalizeRotation(localRotation, nameof(localRotation));
            bool enteringAttachment = !_attachedTo.HasValue;
            _attachedTo = parent;
            _attachmentPosition = localPosition;
            _attachmentRotation = rotation;
            if (enteringAttachment)
            {
                ResetPresentation();
            }
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
            StorePosition(_geographic.Position(position), sampleTime);
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

        /// <summary>Supplies velocity in shared Cartesian source units per second; older or duplicate timed observations are ignored.</summary>
        /// <param name="velocity">Finite velocity in the same source axes and distance units as the Cartesian position.</param>
        /// <param name="sampleTime">The original SDK observation time, or null for untimed input.</param>
        /// <exception cref="ArgumentOutOfRangeException">A velocity coordinate is not finite.</exception>
        public void SetCartesianVelocity(Double3 velocity, Timestamp? sampleTime = null)
        {
            Motion.StoreVelocity(velocity, sampleTime);
        }

        /// <summary>Supplies ECEF XYZ velocity in metres per second; older or duplicate timed observations are ignored.</summary>
        /// <param name="velocity">Finite Earth-centered, Earth-fixed XYZ velocity in metres per second.</param>
        /// <param name="sampleTime">The original SDK observation time, or null for untimed input.</param>
        /// <exception cref="ArgumentOutOfRangeException">A velocity coordinate is not finite.</exception>
        public void SetEarthCenteredVelocity(Double3 velocity, Timestamp? sampleTime = null)
        {
            Motion.StoreVelocity(velocity, sampleTime);
        }

        /// <summary>Converts ENU metres per second (X east, Y north, Z up) at the SDK tangent origin into ECEF velocity.</summary>
        /// <param name="velocity">The finite local east/north/up velocity in metres per second.</param>
        /// <param name="origin">The SDK tangent origin defining the velocity axes; its height does not affect the conversion.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed velocity samples are ignored.</param>
        /// <remarks>ENU axes are geographic tangent directions, independent of aircraft pitch or roll. Convert body-frame
        /// velocity into those directions using the raw SDK attitude at the motion observation's time before calling this.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The origin or velocity is invalid, or conversion produces a nonfinite coordinate.</exception>
        public void SetGeographicVelocity(Double3 velocity, GeoPosition origin, Timestamp? sampleTime = null)
        {
            Motion.StoreVelocity(_geographic.Velocity(origin, velocity), sampleTime);
        }

        /// <summary>Clears supplied velocity; timestamp-based estimation remains available.</summary>
        /// <remarks>Also clears the velocity timestamp so the next supplied sample starts a new timed channel.</remarks>
        public void ClearVelocity()
        {
            Motion.ClearVelocity();
        }

        /// <summary>Supplies linear acceleration in Cartesian source units per second squared.</summary>
        /// <param name="acceleration">Finite linear acceleration in the position's source axes, with gravity removed.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed acceleration samples are ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException">An acceleration coordinate is not finite.</exception>
        public void SetCartesianAcceleration(Double3 acceleration, Timestamp? sampleTime = null)
        {
            Motion.StoreAcceleration(acceleration, sampleTime);
        }

        /// <summary>Supplies ECEF linear acceleration in metres per second squared, with gravity removed.</summary>
        /// <param name="acceleration">Finite Earth-centered, Earth-fixed XYZ linear acceleration in metres per second squared.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed acceleration samples are ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException">An acceleration coordinate is not finite.</exception>
        public void SetEarthCenteredAcceleration(Double3 acceleration, Timestamp? sampleTime = null)
        {
            Motion.StoreAcceleration(acceleration, sampleTime);
        }

        /// <summary>Converts ENU linear acceleration (X east, Y north, Z up) at the SDK origin; gravity must already be removed.</summary>
        /// <param name="acceleration">Finite east/north/up linear acceleration in metres per second squared.</param>
        /// <param name="origin">The SDK tangent origin defining the acceleration axes; its height does not affect the conversion.</param>
        /// <param name="sampleTime">The original SDK observation time; older or duplicate timed acceleration samples are ignored.</param>
        /// <remarks>ENU axes are geographic tangent directions, independent of aircraft pitch or roll. Convert body-frame
        /// linear acceleration using the raw SDK attitude at the motion observation's time; remove gravity before supplying it.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The origin or acceleration is invalid, or conversion produces a nonfinite coordinate.</exception>
        public void SetGeographicAcceleration(Double3 acceleration, GeoPosition origin, Timestamp? sampleTime = null)
        {
            Motion.StoreAcceleration(_geographic.Acceleration(origin, acceleration), sampleTime);
        }

        /// <summary>Clears acceleration while retaining supplied or estimated velocity.</summary>
        /// <remarks>Also clears the acceleration timestamp; acceleration alone never starts position extrapolation.</remarks>
        public void ClearAcceleration()
        {
            Motion.ClearAcceleration();
        }

        /// <summary>Supplies optional body angular velocity in radians per second for bounded orientation extrapolation.</summary>
        /// <param name="angularVelocity">Finite true body angular rates in the specified SDK axes; these are not yaw/pitch/roll Euler-angle derivatives.</param>
        /// <param name="bodyAxes">Signed SDK body axes mapped to model right/up/forward. Null uses X forward, Y right, Z down: roll, pitch and yaw rates.</param>
        /// <param name="sampleTime">Original SDK observation time, or null for untimed input; older or duplicate timed angular samples are ignored.</param>
        /// <remarks>
        /// Samples body rates at the attitude timestamp, independently of reader order. Supplied zero overrides
        /// estimation; ClearAngularVelocity restores it. World ENU rates use SetGeographicAngularVelocity.
        /// Stored rates leave the raw pose unchanged and remain available to prediction and smoothing,
        /// including when no Extrapolation component is present.
        /// </remarks>
        /// <exception cref="ArgumentException">The body-axis mapping is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An angular velocity coordinate or its converted value is not finite.</exception>
        public void SetBodyAngularVelocity(Double3 angularVelocity, CoordinateSystem? bodyAxes = null, Timestamp? sampleTime = null)
        {
            // Check the SDK rate and convert its body axes once, so all predictions use model axes.
            ReferenceFrame.ValidatePosition(angularVelocity, nameof(angularVelocity));
            CoordinateSystem axes = bodyAxes ?? CoordinateSystem.NorthEastDown;
            string error = axes.GetConfigurationError();
            if (error != null)
            {
                throw new ArgumentException(error, nameof(bodyAxes));
            }

            Motion.StoreAngularVelocity(axes.ToUnityAngularVelocity(angularVelocity), world: false, sampleTime: sampleTime);
        }

        /// <summary>Supplies world angular velocity in right-handed ECEF XYZ axes, in radians per second.</summary>
        /// <param name="angularVelocity">Finite Earth-centered, Earth-fixed angular vector; positive rotation follows the right-hand rule.</param>
        /// <param name="sampleTime">Original SDK observation time, or null for untimed input; older or duplicate timed angular samples are ignored.</param>
        /// <remarks>Use a Geographic reference with geographic/ECEF positions. World rates act before the raw attitude;
        /// they are not body rates or Euler-angle derivatives. Shares ordering, availability and clearing with SetBodyAngularVelocity.
        /// Accepted frame changes discard angular history so world and body samples are never interpolated together.
        /// Samples at the attitude's observation time and uses the existing extrapolation horizon without changing linear motion.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">An angular velocity coordinate is not finite.</exception>
        public void SetEarthCenteredAngularVelocity(Double3 angularVelocity, Timestamp? sampleTime = null)
        {
            ReferenceFrame.ValidatePosition(angularVelocity, nameof(angularVelocity));
            Motion.StoreAngularVelocity(angularVelocity, world: true, sampleTime: sampleTime);
        }

        /// <summary>Supplies world ENU angular velocity in radians per second at an explicit SDK tangent origin.</summary>
        /// <param name="angularVelocity">Finite angular vector with X east, Y north and Z up; positive rotation follows the right-hand rule.</param>
        /// <param name="origin">The SDK tangent origin defining the ENU axes; height does not affect the conversion.</param>
        /// <param name="sampleTime">Original SDK observation time, or null for untimed input; older or duplicate timed angular samples are ignored.</param>
        /// <remarks>Converts to ECEF without using aircraft attitude, independently of trait-reader order. Use a Geographic reference
        /// with geographic/ECEF positions. These are world angular rates, not aircraft body rates or Euler-angle derivatives.
        /// Shares ordering and clearing with the other angular setters. AngularVelocity exposes the converted ECEF vector.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The origin or angular vector is invalid, or conversion produces a nonfinite coordinate.</exception>
        /// <example><code>spatial.SetGeographicAngularVelocity(enuAngularVelocity, sdkTangentOrigin, sampleTime);</code></example>
        public void SetGeographicAngularVelocity(Double3 angularVelocity, GeoPosition origin, Timestamp? sampleTime = null)
        {
            SetEarthCenteredAngularVelocity(_geographic.AngularVelocity(origin, angularVelocity), sampleTime);
        }

        /// <summary>Clears supplied angular velocity, restoring extrapolation from consecutive timed rotations when available.</summary>
        /// <remarks>Also clears angular timestamp ordering and playback history; linear velocity, acceleration and pose remain unchanged.</remarks>
        public void ClearAngularVelocity()
        {
            Motion.ClearAngularVelocity();
        }

        internal void ResetPrediction()
        {
            if (_processor != null)
            {
                _processor.Reset();
            }
            else
            {
                _history.RestartEstimation();
            }
        }

        internal void ResetTuning()
        {
            _presenter.ResetTuning();
        }

        // Apply prediction and smoothing after all SDK readers run and before saving the shared reference.
        internal void PreparePresentation(SpatialClock clock, double timestamp, float interpolationDelay, CoordinateSystem sourceAxes,
            bool directPose = false)
        {
            // Attach/Detach reset once at the state transition. Attached roots only
            // inherit their parent; do not update or repeatedly reset their filters.
            if (!enabled || AttachedTo.HasValue)
            {
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
            _motion?.ResetTime();
            ResetPresentation();
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

        internal GeographicBasis BasisAt(Double3 position)
        {
            // The observed point is sampled repeatedly by model preparation. Keep it
            // separate from interpolated/presented points to avoid evicting it on previews.
            return position == Position ? _rawGeography.Get(position, _geographic)
                : _sampledGeography.Get(position, _geographic);
        }

        internal void SetInRange(bool value)
        {
            _suppression.SetInRange(this, value);
        }

        private Quaternion ProjectRotation(ReferenceFrame.Projection projection, Quaternion rotation, Double3 position)
        {
            if (_earthCenteredBodyAxes.HasValue)
            {
                return projection.ToUnityEarthCenteredRotation(rotation, _earthCenteredBodyAxes.Value);
            }

            if (projection.IsGeographic)
            {
                GeographicBasis basis = BasisAt(position);
                return _rotationSpace == Emas.RotationSpace.Geographic
                    ? projection.ToUnityGeographicRotation(rotation, basis) : projection.ToUnityRotation(rotation, basis);
            }

            return _rotationSpace == Emas.RotationSpace.Geographic
                ? projection.ToUnityGeographicRotation(rotation, position) : projection.ToUnityRotation(rotation, position);
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
            _processor?.ResetRotationFilters();

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
