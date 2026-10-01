using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Maps double-precision Cartesian or WGS84 Earth-centered positions to a nearby Unity world pose.
    /// </summary>
    /// <remarks>
    /// Assign to Realm.ReferenceFrame to customize spatial projection; null uses an identity frame.
    /// Cartesian space uses shared units and axes. Geographic space uses ECEF metres with local or body-to-ECEF attitudes,
    /// projecting around the reference's current WGS84 tangent frame. Supply channels independently or follow a Ghost by key.
    /// All configuration and conversion calls require Unity's main thread.
    /// </remarks>
    public sealed class ReferenceFrame
    {
        private Double3 _position;
        private ReferenceSpace _space;
        private CoordinateSystem _coordinates = CoordinateSystem.Unity;
        private Quaternion _rotation = Quaternion.identity;
        private CoordinateSystem? _earthCenteredBodyAxes;
        private Vector3 _unityPosition;
        private Quaternion _unityRotation = Quaternion.identity;
        private double? _maxDistance;
        private Key? _followedGhost;
        private bool _hasPosition;
        private bool _isReferenceAvailable;

        /// <summary>Gets or sets Cartesian or WGS84 geographic projection; defaults to Cartesian.</summary>
        /// <remarks>Geographic positions are stored as ECEF metres. Geographic rotations accept local tangent or body-to-ECEF input.</remarks>
        public ReferenceSpace Space
        {
            get
            {
                return _space;
            }
            set
            {
                if (value != ReferenceSpace.Cartesian && value != ReferenceSpace.Geographic)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown reference space.");
                }
                if (value == ReferenceSpace.Geographic && _hasPosition)
                {
                    GeoPosition.FromEarthCentered(_position);
                }
                _space = value;
            }
        }

        /// <summary>Gets or sets the reference's WGS84 position in Geographic space.</summary>
        /// <remarks>A followed Ghost replaces it on the next update. Position exposes the same point in ECEF metres.</remarks>
        public GeoPosition GeographicPosition
        {
            get
            {
                RequireGeographic();
                RequirePosition();
                return GeoPosition.FromEarthCentered(_position);
            }
            set
            {
                RequireGeographic();
                Position = value.ToEarthCentered();
            }
        }

        /// <summary>
        /// Gets or sets the axes and handedness for Cartesian poses or geographic local attitudes; defaults to Unity.
        /// </summary>
        /// <remarks>
        /// Cartesian poses use this convention. In Geographic space it describes local attitude axes:
        /// Unity means east/up/north; ENU and NED have their geographic meanings. ECEF positions and explicit
        /// body-to-ECEF rotation inputs are unaffected; ECEF attitude supplies its own body-axis mapping.
        /// Changing it reinterprets stored poses
        /// on the next realm update; conversion methods use it immediately. UnityPosition and UnityRotation stay in Unity space.
        /// </remarks>
        /// <exception cref="ArgumentException">The mapping contains undefined or repeated source axes.</exception>
        public CoordinateSystem Coordinates
        {
            get
            {
                return _coordinates;
            }
            set
            {
                string error = value.GetConfigurationError();
                if (error != null)
                {
                    throw new ArgumentException(error, nameof(value));
                }

                _coordinates = value;
            }
        }

        /// <summary>
        /// Gets or sets the shared Cartesian position mapped to UnityPosition; Geographic space uses ECEF metres.
        /// </summary>
        /// <remarks>
        /// Setting this establishes a manual reference. A followed ghost overwrites it during realm projection.
        /// </remarks>
        public Double3 Position
        {
            get
            {
                return _position;
            }
            set
            {
                ValidatePosition(value, nameof(value));
                if (_space == ReferenceSpace.Geographic)
                {
                    GeoPosition.FromEarthCentered(value);
                }
                _position = value;
                _hasPosition = true;
                _isReferenceAvailable = true;
            }
        }

        /// <summary>
        /// Gets the cached reference quaternion, or sets local/source attitude and clears ECEF attitude mode.
        /// Use UsesEarthCenteredRotation to identify the cached representation.
        /// </summary>
        public Quaternion Rotation
        {
            get
            {
                return _rotation;
            }
            set
            {
                _rotation = SpatialMath.NormalizeRotation(value, nameof(value));
                _earthCenteredBodyAxes = null;
            }
        }

        /// <summary>Gets whether the cached reference Rotation is expressed as a body-to-ECEF quaternion.</summary>
        public bool UsesEarthCenteredRotation => _earthCenteredBodyAxes.HasValue;

        /// <summary>Sets manual reference attitude from a body-to-ECEF quaternion in Geographic space.</summary>
        /// <param name="rotation">Active rotation mapping source body XYZ vectors into ECEF XYZ.</param>
        /// <param name="bodyAxes">Body axes mapped to Unity directions; null uses forward/right/down (NED mapping).</param>
        public void SetEarthCenteredRotation(Quaternion rotation, CoordinateSystem? bodyAxes = null)
        {
            RequireGeographic();
            Quaternion normalized = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            CoordinateSystem axes = CoordinateSystem.RequireRightHandedBodyAxes(bodyAxes);
            _rotation = normalized;
            _earthCenteredBodyAxes = axes;
        }

        /// <summary>
        /// Gets or sets the desired Unity world position of the reference, normally near zero.
        /// </summary>
        public Vector3 UnityPosition
        {
            get
            {
                return _unityPosition;
            }
            set
            {
                if (!SpatialMath.IsFinite(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The Unity reference position must be finite.");
                }

                _unityPosition = value;
            }
        }

        /// <summary>
        /// Gets or sets the desired Unity world orientation of the reference.
        /// </summary>
        public Quaternion UnityRotation
        {
            get
            {
                return _unityRotation;
            }
            set
            {
                _unityRotation = SpatialMath.NormalizeRotation(value, nameof(value));
            }
        }

        /// <summary>
        /// Gets or sets whether the reference rotation is cancelled, keeping its Unity heading fixed.
        /// </summary>
        /// <remarks>
        /// True by default. False follows position only, while UnityRotation still defines the scene alignment.
        /// </remarks>
        public bool FollowRotation
        {
            get;
            set;
        } = true;

        /// <summary>
        /// Gets or sets the maximum presentation distance in shared coordinate units, or null for no distance limit.
        /// </summary>
        /// <remarks>
        /// A configured limit must be positive and finite. Outside it, ghosts remain available and their views
        /// are suppressed without cancelling requests. Returning into range restores requested presentation.
        /// Choose a local range suitable for Unity float precision; data and distance calculations remain doubles.
        /// </remarks>
        public double? MaxDistance
        {
            get
            {
                return _maxDistance;
            }
            set
            {
                if (value.HasValue && (!SpatialMath.IsFinite(value.Value) || value.Value <= 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The presentation distance must be positive and finite, or null.");
                }

                _maxDistance = value;
            }
        }

        /// <summary>
        /// Gets or sets the spatial ghost to follow in the owning realm, or null for manual reference updates.
        /// </summary>
        /// <remarks>
        /// Assign or replace the key when the target identity becomes known at runtime.
        /// The key is resolved again on each projection, including after source replacement or entity recreation.
        /// Loss freezes the last valid pose and sets IsReferenceAvailable to false. Before the first reference
        /// position arrives, spatial presentation is suppressed. Clearing this key keeps the last pose as a manual reference.
        /// </remarks>
        public Key? FollowedGhost
        {
            get
            {
                return _followedGhost;
            }
            set
            {
                if (value.HasValue && (string.IsNullOrEmpty(value.Value.AnchorId)
                    || !value.Value.Kind.IsValid || string.IsNullOrEmpty(value.Value.EntityId)))
                {
                    throw new ArgumentException("A valid ghost identity is required.", nameof(value));
                }

                _followedGhost = value;
                _isReferenceAvailable = !value.HasValue && _hasPosition;
            }
        }

        /// <summary>
        /// Gets whether any valid reference position has been supplied, including a frozen last-known position.
        /// </summary>
        public bool HasPosition
        {
            get
            {
                return _hasPosition;
            }
        }

        /// <summary>
        /// Gets whether the manual reference is defined or the followed ghost was available at the last projection.
        /// </summary>
        public bool IsReferenceAvailable
        {
            get
            {
                return _isReferenceAvailable;
            }
        }

        /// <summary>
        /// Converts a shared Cartesian position to Unity world space, subtracting the reference before float conversion.
        /// </summary>
        /// <returns>
        /// False if no reference position exists, the point exceeds MaxDistance, or the result cannot fit in Vector3.
        /// </returns>
        public bool TryToUnityPosition(Double3 position, out Vector3 unityPosition)
        {
            ValidatePosition(position, nameof(position));
            return Capture().TryToUnityPosition(position, out unityPosition);
        }

        /// <summary>Projects a WGS84 position using a Geographic reference and its range limit.</summary>
        public bool TryToUnityPosition(GeoPosition position, out Vector3 unityPosition)
        {
            RequireGeographic();
            return TryToUnityPosition(position.ToEarthCentered(), out unityPosition);
        }

        /// <summary>Converts a Unity world point back into WGS84 coordinates using a Geographic reference.</summary>
        public GeoPosition ToGeographicPosition(Vector3 unityPosition)
        {
            RequireGeographic();
            return GeoPosition.FromEarthCentered(ToSimulationPosition(unityPosition));
        }

        /// <summary>
        /// Converts a Unity world position back into double-precision Cartesian coordinates, or ECEF metres in Geographic space.
        /// </summary>
        public Double3 ToSimulationPosition(Vector3 unityPosition)
        {
            RequirePosition();
            if (!SpatialMath.IsFinite(unityPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(unityPosition), "The Unity position must be finite.");
            }

            return Capture().ToSimulationPosition(unityPosition);
        }

        /// <summary>
        /// Converts an orientation to Unity. In Geographic space this overload uses the reference location
        /// as the attitude origin; use the GeoPosition overload for an entity at another location.
        /// </summary>
        public Quaternion ToUnityRotation(Quaternion rotation)
        {
            RequirePosition();
            return Capture().ToUnityRotation(SpatialMath.NormalizeRotation(rotation, nameof(rotation)));
        }

        /// <summary>
        /// Converts Unity orientation back into the source axes. Geographic space uses the reference location;
        /// use the GeoPosition overload for attitude at another location.
        /// </summary>
        public Quaternion ToSimulationRotation(Quaternion unityRotation)
        {
            RequirePosition();
            Quaternion rotation = SpatialMath.NormalizeRotation(unityRotation, nameof(unityRotation));
            return SpatialMath.NormalizeRotation(_coordinates.ToSourceRotation(Quaternion.Inverse(Capture().Alignment) * rotation), nameof(unityRotation));
        }

        /// <summary>Converts an entity's local geographic attitude to Unity, including its tangent-frame orientation.</summary>
        public Quaternion ToUnityRotation(Quaternion rotation, GeoPosition position)
        {
            RequireGeographic();
            RequirePosition();
            return Capture().ToUnityRotation(SpatialMath.NormalizeRotation(rotation, nameof(rotation)), position);
        }

        /// <summary>Converts Unity orientation back into local attitude at the supplied geographic position.</summary>
        public Quaternion ToSimulationRotation(Quaternion unityRotation, GeoPosition position)
        {
            RequireGeographic();
            RequirePosition();
            Quaternion rotation = SpatialMath.NormalizeRotation(unityRotation, nameof(unityRotation));
            return Capture().ToGeographicRotation(rotation, position);
        }

        /// <summary>Converts body-to-ECEF attitude into Unity orientation using this Geographic reference.</summary>
        /// <param name="rotation">Active body-to-ECEF quaternion.</param>
        /// <param name="bodyAxes">Body axes mapped to Unity directions; null uses forward/right/down.</param>
        public Quaternion ToUnityEarthCenteredRotation(Quaternion rotation, CoordinateSystem? bodyAxes = null)
        {
            RequireGeographic();
            RequirePosition();
            return Capture().ToUnityEarthCenteredRotation(SpatialMath.NormalizeRotation(rotation, nameof(rotation)),
                CoordinateSystem.RequireRightHandedBodyAxes(bodyAxes));
        }

        /// <summary>Converts Unity orientation back into a body-to-ECEF quaternion using this Geographic reference.</summary>
        /// <param name="unityRotation">Unity world orientation.</param>
        /// <param name="bodyAxes">Body axes mapped to Unity directions; null uses forward/right/down.</param>
        public Quaternion ToEarthCenteredRotation(Quaternion unityRotation, CoordinateSystem? bodyAxes = null)
        {
            RequireGeographic();
            RequirePosition();
            return Capture().ToEarthCenteredRotation(SpatialMath.NormalizeRotation(unityRotation, nameof(unityRotation)),
                CoordinateSystem.RequireRightHandedBodyAxes(bodyAxes));
        }

        /// <summary>
        /// Calculates Cartesian distance in doubles; Geographic space measures the ECEF chord in metres, not surface distance.
        /// </summary>
        public double DistanceTo(Double3 position)
        {
            RequirePosition();
            ValidatePosition(position, nameof(position));
            return Double3.Distance(_position, position);
        }

        internal void UpdateFollowedPose(Spatial spatial)
        {
            _isReferenceAvailable = spatial != null && spatial.enabled && spatial.HasPosition;
            if (_isReferenceAvailable && _space == ReferenceSpace.Geographic)
            {
                try
                {
                    GeoPosition.FromEarthCentered(spatial.Position);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Retain the last usable tangent frame when a source supplies an undefined geographic point.
                    _isReferenceAvailable = false;
                }
            }
            if (!_isReferenceAvailable)
            {
                return;
            }

            _position = spatial.Position;
            _hasPosition = true;
            if (spatial.HasRotation)
            {
                _rotation = spatial.Rotation;
                _earthCenteredBodyAxes = spatial.EarthCenteredBodyAxes;
            }
        }

        internal Projection Capture()
        {
            Quaternion referenceRotation = _coordinates.ToUnityRotation(_rotation);
            if (_earthCenteredBodyAxes.HasValue)
            {
                RequireGeographic();
                referenceRotation = _hasPosition
                    ? new GeographicBasis(GeoPosition.FromEarthCentered(_position))
                        .ToLocalEarthCenteredRotation(_rotation, _earthCenteredBodyAxes.Value)
                    : Quaternion.identity;
            }
            Quaternion alignment = _unityRotation * (FollowRotation ? Quaternion.Inverse(referenceRotation) : Quaternion.identity);
            return new Projection(_hasPosition, _position, _unityPosition,
                SpatialMath.NormalizeRotation(alignment, nameof(Rotation)), _coordinates, _maxDistance, _space);
        }

        internal static void ValidatePosition(Double3 position, string parameter)
        {
            if (!SpatialMath.IsFinite(position.X) || !SpatialMath.IsFinite(position.Y) || !SpatialMath.IsFinite(position.Z))
            {
                throw new ArgumentOutOfRangeException(parameter, "Spatial positions must be finite.");
            }
        }

        private void RequireGeographic()
        {
            if (_space != ReferenceSpace.Geographic)
            {
                throw new InvalidOperationException("This conversion requires Geographic reference space.");
            }
        }

        private void RequirePosition()
        {
            if (!_hasPosition)
            {
                throw new InvalidOperationException("The reference position has not been supplied yet.");
            }
        }

        // One value is shared by every participant in a projection pass.
        internal readonly struct Projection
        {
            private readonly bool _hasPosition;
            private readonly ReferenceSpace _space;
            private readonly GeographicBasis _geographicBasis;
            private readonly CoordinateSystem _coordinates;
            private readonly Double3 _position;
            private readonly Vector3 _unityPosition;
            private readonly double? _maxDistance;
            internal readonly Quaternion Alignment;

            internal Projection(bool hasPosition, Double3 position, Vector3 unityPosition, Quaternion alignment, CoordinateSystem coordinates, double? maxDistance,
                ReferenceSpace space = ReferenceSpace.Cartesian)
            {
                _hasPosition = hasPosition;
                _space = space;
                _geographicBasis = space == ReferenceSpace.Geographic && hasPosition
                    ? new GeographicBasis(GeoPosition.FromEarthCentered(position)) : default;
                _coordinates = coordinates;
                _position = position;
                _unityPosition = unityPosition;
                Alignment = alignment;
                _maxDistance = maxDistance;
            }

            internal bool TryToUnityPosition(Double3 position, out Vector3 result)
            {
                result = default(Vector3);
                if (!_hasPosition || (_space == ReferenceSpace.Geographic && position == default(Double3))
                    || (_maxDistance.HasValue && Double3.Distance(_position, position) > _maxDistance.Value))
                {
                    return false;
                }

                try
                {
                    Double3 displacement = position - _position;
                    Double3 local = _space == ReferenceSpace.Geographic
                        ? _geographicBasis.ToLocal(displacement) : _coordinates.ToUnity(displacement);
                    Double3 offset = SpatialMath.Rotate(Alignment, local);
                    return SpatialMath.TryToVector3(offset + new Double3(_unityPosition.x, _unityPosition.y, _unityPosition.z), out result);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Finite coordinates can have a separation beyond representable range.
                    return false;
                }
            }

            internal Double3 ToSimulationPosition(Vector3 unityPosition)
            {
                Double3 offset = new Double3((double)unityPosition.x - _unityPosition.x,
                    (double)unityPosition.y - _unityPosition.y, (double)unityPosition.z - _unityPosition.z);
                Double3 local = SpatialMath.Rotate(Quaternion.Inverse(Alignment), offset);
                return _position + (_space == ReferenceSpace.Geographic
                    ? _geographicBasis.ToEarthCentered(local) : _coordinates.ToSource(local));
            }

            internal Quaternion ToUnityRotation(Quaternion rotation, Double3 position)
            {
                return _space == ReferenceSpace.Geographic
                    ? ToUnityRotation(rotation, GeoPosition.FromEarthCentered(position)) : ToUnityRotation(rotation);
            }

            internal Quaternion ToUnityRotation(Quaternion rotation, GeoPosition position)
            {
                return SpatialMath.NormalizeRotation(Alignment * _geographicBasis.RotationFrom(position)
                    * _coordinates.ToUnityRotation(rotation), nameof(rotation));
            }

            internal Quaternion ToUnityEarthCenteredRotation(Quaternion rotation, CoordinateSystem bodyAxes)
            {
                if (_space != ReferenceSpace.Geographic)
                {
                    throw new InvalidOperationException("ECEF attitudes require a Geographic reference frame.");
                }
                return SpatialMath.NormalizeRotation(Alignment
                    * _geographicBasis.ToLocalEarthCenteredRotation(rotation, bodyAxes), nameof(rotation));
            }

            internal Quaternion ToEarthCenteredRotation(Quaternion rotation, CoordinateSystem bodyAxes)
            {
                return _geographicBasis.ToEarthCenteredRotation(Quaternion.Inverse(Alignment) * rotation, bodyAxes);
            }

            internal Quaternion ToGeographicRotation(Quaternion rotation, GeoPosition position)
            {
                Quaternion local = Quaternion.Inverse(_geographicBasis.RotationFrom(position))
                    * Quaternion.Inverse(Alignment) * rotation;
                return SpatialMath.NormalizeRotation(_coordinates.ToSourceRotation(local), nameof(rotation));
            }

            internal Quaternion ToUnityRotation(Quaternion rotation)
            {
                return SpatialMath.NormalizeRotation(Alignment * _coordinates.ToUnityRotation(rotation), nameof(rotation));
            }
        }
    }
}
