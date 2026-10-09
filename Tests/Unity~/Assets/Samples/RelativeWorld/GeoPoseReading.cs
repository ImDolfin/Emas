using System;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Represents one SDK spatial observation in WGS84 geographic coordinates.
    /// </summary>
    /// <remarks>
    /// Latitude, longitude and attitude are degrees. AltitudeMeters is height above the WGS84
    /// ellipsoid in metres, not mean sea level. Values are captured when this object is created.
    /// </remarks>
    public sealed class GeoPoseReading
    {
        /// <summary>
        /// Creates an immutable geographic pose observation.
        /// </summary>
        /// <param name="id">The stable SDK entity ID.</param>
        /// <param name="label">The entity display label.</param>
        /// <param name="kind">The observed entity category.</param>
        /// <param name="variant">The requested visual variant.</param>
        /// <param name="latitudeDegrees">WGS84 geodetic latitude in degrees north.</param>
        /// <param name="longitudeDegrees">WGS84 longitude in degrees east.</param>
        /// <param name="altitudeMeters">Height above the WGS84 ellipsoid in metres.</param>
        /// <param name="yawDegrees">Heading clockwise from true north, in degrees.</param>
        /// <param name="pitchDegrees">Nose-up pitch in degrees.</param>
        /// <param name="rollDegrees">Right-wing-down roll in degrees.</param>
        /// <param name="parentId">The parent entity ID while attached; null for absolute placement.</param>
        /// <param name="parentKind">The attached parent's category.</param>
        /// <param name="bodyOffset">Parent-local metres in SDK axes: X forward, Y right and Z down.</param>
        /// <param name="eastNorthUpVelocity">Optional local ENU velocity in metres per second for prediction and motion-assisted smoothing.</param>
        /// <param name="eastNorthUpAcceleration">Optional local ENU linear acceleration in metres per second squared, with gravity removed.</param>
        /// <param name="sampleTime">Optional observation time in the SDK's shared seconds/nanoseconds clock.</param>
        /// <exception cref="ArgumentException">The entity ID is blank, or an attached reading has a blank parent ID or invalid parent Kind.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A pose or body-offset component is non-finite, or latitude or longitude is outside its geographic range.</exception>
        public GeoPoseReading(string id, string label, Kind kind, Variant variant, double latitudeDegrees,
            double longitudeDegrees, double altitudeMeters, double yawDegrees, double pitchDegrees,
            double rollDegrees, string parentId = null, Kind parentKind = default, Vector3 bodyOffset = default,
            Double3? eastNorthUpVelocity = null, Double3? eastNorthUpAcceleration = null, Timestamp? sampleTime = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A geographic reading requires a nonempty entity ID.", nameof(id));
            }

            ValidateRange(latitudeDegrees, -90.0, 90.0, nameof(latitudeDegrees));
            ValidateRange(longitudeDegrees, -180.0, 180.0, nameof(longitudeDegrees));
            ValidateFinite(altitudeMeters, nameof(altitudeMeters));
            ValidateFinite(yawDegrees, nameof(yawDegrees));
            ValidateFinite(pitchDegrees, nameof(pitchDegrees));
            ValidateFinite(rollDegrees, nameof(rollDegrees));
            ValidateFinite(bodyOffset.x, nameof(bodyOffset));
            ValidateFinite(bodyOffset.y, nameof(bodyOffset));
            ValidateFinite(bodyOffset.z, nameof(bodyOffset));
            if (parentId != null && (string.IsNullOrWhiteSpace(parentId) || !parentKind.IsValid))
            {
                throw new ArgumentException("An attached reading requires a parent ID and Kind.", nameof(parentId));
            }

            Id = id;
            Label = label;
            Kind = kind;
            Variant = variant;
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            AltitudeMeters = altitudeMeters;
            YawDegrees = yawDegrees;
            PitchDegrees = pitchDegrees;
            RollDegrees = rollDegrees;
            ParentId = parentId;
            ParentKind = parentKind;
            BodyOffset = bodyOffset;
            EastNorthUpVelocity = eastNorthUpVelocity;
            EastNorthUpAcceleration = eastNorthUpAcceleration;
            SampleTime = sampleTime;
        }

        /// <summary>Gets the stable SDK entity ID.</summary>
        /// <value>The nonempty identity used for source lookup and presence detection.</value>
        public string Id { get; }

        /// <summary>Gets the SDK display label.</summary>
        /// <value>The optional display label supplied by the SDK; it does not determine identity.</value>
        public string Label { get; }

        /// <summary>Gets the observed entity category.</summary>
        /// <value>The Kind passed to presence detection to select the entity blueprint.</value>
        public Kind Kind { get; }

        /// <summary>Gets the requested visual variant.</summary>
        /// <value>The appearance identifier selected independently of entity identity.</value>
        public Variant Variant { get; }

        /// <summary>Gets WGS84 geodetic latitude in degrees north.</summary>
        /// <value>A finite angle in [-90, 90].</value>
        public double LatitudeDegrees { get; }

        /// <summary>Gets WGS84 longitude in degrees east.</summary>
        /// <value>A finite angle in [-180, 180].</value>
        public double LongitudeDegrees { get; }

        /// <summary>Gets height above the WGS84 ellipsoid in metres.</summary>
        /// <value>Finite ellipsoidal height, which may be negative and is not mean-sea-level altitude.</value>
        public double AltitudeMeters { get; }

        /// <summary>Gets heading clockwise from true north in degrees.</summary>
        /// <value>A finite heading angle; values are not restricted to a single turn.</value>
        public double YawDegrees { get; }

        /// <summary>Gets nose-up pitch in degrees.</summary>
        /// <value>A finite pitch angle in the SDK's geographic attitude convention.</value>
        public double PitchDegrees { get; }

        /// <summary>Gets right-wing-down roll in degrees.</summary>
        /// <value>A finite roll angle in the SDK's geographic attitude convention.</value>
        public double RollDegrees { get; }

        /// <summary>Gets the parent entity ID while attached, or null for absolute spatial placement.</summary>
        /// <value>A nonempty parent ID, or null when the entity is detached.</value>
        public string ParentId { get; }

        /// <summary>Gets the attached parent's category.</summary>
        /// <value>A valid parent Kind when attached; unused when <see cref="ParentId"/> is null.</value>
        public Kind ParentKind { get; }

        /// <summary>Gets the parent-local offset in metres using SDK X-forward, Y-right and Z-down axes.</summary>
        /// <value>The body offset converted by <see cref="GeoAttachmentTrait"/> into Unity local axes.</value>
        public Vector3 BodyOffset { get; }

        /// <summary>Gets optional X-east, Y-north, Z-up velocity in metres per second at this reading's location.</summary>
        /// <value>Supplied linear velocity, or null when prediction must estimate it from timestamped positions.</value>
        public Double3? EastNorthUpVelocity { get; }

        /// <summary>Gets optional X-east, Y-north, Z-up linear acceleration in metres per second squared, with gravity removed.</summary>
        /// <value>Supplied linear acceleration, or null when no acceleration observation is available.</value>
        public Double3? EastNorthUpAcceleration { get; }

        /// <summary>Gets the SDK sample time shared by pose and motion channels, or null when unavailable.</summary>
        /// <value>The original observation time, preserved through packet delay; this is not the packet's arrival time.</value>
        public Timestamp? SampleTime { get; }

        private static void ValidateRange(double value, double minimum, double maximum, string parameter)
        {
            ValidateFinite(value, parameter);
            if (value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameter, "The geographic angle is outside its valid range.");
            }
        }

        private static void ValidateFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameter, "Geographic readings must contain finite values.");
            }
        }
    }
}
