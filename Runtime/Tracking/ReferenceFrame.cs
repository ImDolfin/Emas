using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Maps double-precision shared Cartesian coordinates to a nearby Unity world pose.
    /// </summary>
    /// <remarks>
    /// Assign to Realm.ReferenceFrame to enable spatial projection. Positions share one Cartesian
    /// coordinate system and unit. Supply position and rotation independently, or follow a spatial ghost by key.
    /// All configuration and conversion calls require Unity's main thread.
    /// </remarks>
    public sealed class ReferenceFrame
    {
        private Double3 _position;
        private Quaternion _rotation = Quaternion.identity;
        private Vector3 _unityPosition;
        private Quaternion _unityRotation = Quaternion.identity;
        private double? _maxDistance;
        private Key? _followedGhost;
        private bool _hasPosition;
        private bool _isReferenceAvailable;

        /// <summary>
        /// Gets or sets the shared Cartesian position mapped to UnityPosition.
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
                _position = value;
                _hasPosition = true;
                _isReferenceAvailable = true;
            }
        }

        /// <summary>
        /// Gets or sets the reference orientation in the shared Cartesian frame, independently of position.
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
            }
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

        /// <summary>
        /// Converts a Unity world position back into double-precision shared Cartesian coordinates.
        /// </summary>
        public Double3 ToSimulationPosition(Vector3 unityPosition)
        {
            RequirePosition();
            if (!SpatialMath.IsFinite(unityPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(unityPosition), "The Unity position must be finite.");
            }

            Projection projection = Capture();
            Double3 offset = new Double3((double)unityPosition.x - _unityPosition.x,
                (double)unityPosition.y - _unityPosition.y, (double)unityPosition.z - _unityPosition.z);
            return _position + SpatialMath.Rotate(Quaternion.Inverse(projection.Alignment), offset);
        }

        /// <summary>
        /// Converts an orientation in the shared Cartesian frame to Unity world orientation using the current rotation mode.
        /// </summary>
        public Quaternion ToUnityRotation(Quaternion rotation)
        {
            RequirePosition();
            return Capture().ToUnityRotation(SpatialMath.NormalizeRotation(rotation, nameof(rotation)));
        }

        /// <summary>
        /// Converts a Unity world orientation back into the shared Cartesian frame.
        /// </summary>
        public Quaternion ToSimulationRotation(Quaternion unityRotation)
        {
            RequirePosition();
            Quaternion rotation = SpatialMath.NormalizeRotation(unityRotation, nameof(unityRotation));
            return SpatialMath.NormalizeRotation(Quaternion.Inverse(Capture().Alignment) * rotation, nameof(unityRotation));
        }

        /// <summary>
        /// Calculates the distance from the reference position entirely in double precision.
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
            if (!_isReferenceAvailable)
            {
                return;
            }

            _position = spatial.Position;
            _hasPosition = true;
            if (spatial.HasRotation)
            {
                _rotation = spatial.Rotation;
            }
        }

        internal Projection Capture()
        {
            Quaternion alignment = _unityRotation * (FollowRotation ? Quaternion.Inverse(_rotation) : Quaternion.identity);
            return new Projection(_hasPosition, _position, _unityPosition,
                SpatialMath.NormalizeRotation(alignment, nameof(Rotation)), _maxDistance);
        }

        internal static void ValidatePosition(Double3 position, string parameter)
        {
            if (!SpatialMath.IsFinite(position.X) || !SpatialMath.IsFinite(position.Y) || !SpatialMath.IsFinite(position.Z))
            {
                throw new ArgumentOutOfRangeException(parameter, "Spatial positions must be finite.");
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
            private readonly Double3 _position;
            private readonly Vector3 _unityPosition;
            private readonly double? _maxDistance;
            internal readonly Quaternion Alignment;

            internal Projection(bool hasPosition, Double3 position, Vector3 unityPosition, Quaternion alignment, double? maxDistance)
            {
                _hasPosition = hasPosition;
                _position = position;
                _unityPosition = unityPosition;
                Alignment = alignment;
                _maxDistance = maxDistance;
            }

            internal bool TryToUnityPosition(Double3 position, out Vector3 result)
            {
                result = default(Vector3);
                if (!_hasPosition || (_maxDistance.HasValue && Double3.Distance(_position, position) > _maxDistance.Value))
                {
                    return false;
                }

                try
                {
                    Double3 offset = SpatialMath.Rotate(Alignment, position - _position);
                    return SpatialMath.TryToVector3(offset + new Double3(_unityPosition.x, _unityPosition.y, _unityPosition.z), out result);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Finite coordinates can have a separation beyond representable range.
                    return false;
                }
            }

            internal Quaternion ToUnityRotation(Quaternion rotation)
            {
                return SpatialMath.NormalizeRotation(Alignment * rotation, nameof(rotation));
            }
        }
    }
}
