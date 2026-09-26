using System;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Converts WGS84 positions into a fixed local tangent frame for Spatial.
    /// </summary>
    /// <remarks>
    /// Every car uses the same datum for the sample's entire lifetime. The resulting Double3 axes
    /// are X east, Y up and Z north, matching Unity's scene axes without reducing positions to floats.
    /// </remarks>
    public sealed class GeoPositionModule : EntityModule<GeoPoseReading>
    {
        /// <summary>Gets the fixed datum's WGS84 latitude in degrees north.</summary>
        public const double DatumLatitudeDegrees = 52.520008;

        /// <summary>Gets the fixed datum's WGS84 longitude in degrees east.</summary>
        public const double DatumLongitudeDegrees = 13.404954;

        /// <summary>Gets the fixed datum's ellipsoidal height in metres.</summary>
        public const double DatumAltitudeMeters = 40.0;

        private const double Wgs84SemiMajorAxisMeters = 6378137.0;
        private const double Wgs84InverseFlattening = 298.257223563;
        private const double DegreesToRadians = Math.PI / 180.0;
        private static readonly double _flattening = 1.0 / Wgs84InverseFlattening;
        private static readonly double _eccentricitySquared = _flattening * (2.0 - _flattening);
        private static readonly double _datumLatitudeRadians = DatumLatitudeDegrees * DegreesToRadians;
        private static readonly double _datumLongitudeRadians = DatumLongitudeDegrees * DegreesToRadians;
        private static readonly double _sinDatumLatitude = Math.Sin(_datumLatitudeRadians);
        private static readonly double _cosDatumLatitude = Math.Cos(_datumLatitudeRadians);
        private static readonly double _sinDatumLongitude = Math.Sin(_datumLongitudeRadians);
        private static readonly double _cosDatumLongitude = Math.Cos(_datumLongitudeRadians);
        private static readonly double _originX;
        private static readonly double _originY;
        private static readonly double _originZ;

        static GeoPositionModule()
        {
            double x;
            double y;
            double z;
            ToEarthCentered(DatumLatitudeDegrees, DatumLongitudeDegrees, DatumAltitudeMeters,
                out x, out y, out z);
            _originX = x;
            _originY = y;
            _originZ = z;
        }

        /// <summary>
        /// Projects one WGS84 observation into the shared east/up/north simulation frame.
        /// </summary>
        /// <param name="reading">The geographic SDK observation.</param>
        public override void Apply(GeoPoseReading reading)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            Spatial spatial = RequireSpatial();
            double x;
            double y;
            double z;
            ToEarthCentered(reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters,
                out x, out y, out z);
            double dx = x - _originX;
            double dy = y - _originY;
            double dz = z - _originZ;

            double east = -_sinDatumLongitude * dx + _cosDatumLongitude * dy;
            double north = -_sinDatumLatitude * _cosDatumLongitude * dx
                - _sinDatumLatitude * _sinDatumLongitude * dy + _cosDatumLatitude * dz;
            double up = _cosDatumLatitude * _cosDatumLongitude * dx
                + _cosDatumLatitude * _sinDatumLongitude * dy + _sinDatumLatitude * dz;

            spatial.SetPosition(new Double3(east, up, north));
        }

        private Spatial RequireSpatial()
        {
            if (Presence == null || Presence.Root == null)
            {
                throw new InvalidOperationException("The geographic position module is not bound to a Ghost root.");
            }

            Spatial spatial = Presence.Root.GetComponent<Spatial>();
            if (spatial == null)
            {
                throw new InvalidOperationException("A geographic position module requires Spatial on the Ghost root.");
            }

            return spatial;
        }

        private static void ToEarthCentered(double latitudeDegrees, double longitudeDegrees, double heightMeters,
            out double x, out double y, out double z)
        {
            double latitude = latitudeDegrees * DegreesToRadians;
            double longitude = longitudeDegrees * DegreesToRadians;
            double sinLatitude = Math.Sin(latitude);
            double cosLatitude = Math.Cos(latitude);
            double radius = Wgs84SemiMajorAxisMeters
                / Math.Sqrt(1.0 - _eccentricitySquared * sinLatitude * sinLatitude);

            x = (radius + heightMeters) * cosLatitude * Math.Cos(longitude);
            y = (radius + heightMeters) * cosLatitude * Math.Sin(longitude);
            z = (radius * (1.0 - _eccentricitySquared) + heightMeters) * sinLatitude;
        }
    }
}
