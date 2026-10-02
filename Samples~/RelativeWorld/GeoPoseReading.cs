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
        public GeoPoseReading(string id, string label, Kind kind, Variant variant, double latitudeDegrees,
            double longitudeDegrees, double altitudeMeters, double yawDegrees, double pitchDegrees,
            double rollDegrees, string parentId = null, Kind parentKind = default, Vector3 bodyOffset = default)
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
        }

        /// <summary>Gets the stable SDK entity ID.</summary>
        public string Id { get; }

        /// <summary>Gets the SDK display label.</summary>
        public string Label { get; }

        /// <summary>Gets the observed entity category.</summary>
        public Kind Kind { get; }

        /// <summary>Gets the requested visual variant.</summary>
        public Variant Variant { get; }

        /// <summary>Gets WGS84 geodetic latitude in degrees north.</summary>
        public double LatitudeDegrees { get; }

        /// <summary>Gets WGS84 longitude in degrees east.</summary>
        public double LongitudeDegrees { get; }

        /// <summary>Gets height above the WGS84 ellipsoid in metres.</summary>
        public double AltitudeMeters { get; }

        /// <summary>Gets heading clockwise from true north in degrees.</summary>
        public double YawDegrees { get; }

        /// <summary>Gets nose-up pitch in degrees.</summary>
        public double PitchDegrees { get; }

        /// <summary>Gets right-wing-down roll in degrees.</summary>
        public double RollDegrees { get; }

        /// <summary>Gets the parent entity ID while attached, or null for absolute spatial placement.</summary>
        public string ParentId { get; }

        /// <summary>Gets the attached parent's category.</summary>
        public Kind ParentKind { get; }

        /// <summary>Gets the parent-local offset in metres using SDK X-forward, Y-right and Z-down axes.</summary>
        public Vector3 BodyOffset { get; }

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
