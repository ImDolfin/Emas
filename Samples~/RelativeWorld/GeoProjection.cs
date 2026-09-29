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
    internal static class GeoProjection
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

        static GeoProjection()
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
        /// Projects one WGS84 observation into the shared fixed-datum east/up/north frame.
        /// </summary>
        internal static Double3 ToPosition(double latitudeDegrees, double longitudeDegrees, double altitudeMeters)
        {
            double x;
            double y;
            double z;
            ToEarthCentered(latitudeDegrees, longitudeDegrees, altitudeMeters,
                out x, out y, out z);
            double dx = x - _originX;
            double dy = y - _originY;
            double dz = z - _originZ;

            double east = -_sinDatumLongitude * dx + _cosDatumLongitude * dy;
            double north = -_sinDatumLatitude * _cosDatumLongitude * dx
                - _sinDatumLatitude * _sinDatumLongitude * dy + _cosDatumLatitude * dz;
            double up = _cosDatumLatitude * _cosDatumLongitude * dx
                + _cosDatumLatitude * _sinDatumLongitude * dy + _sinDatumLatitude * dz;

            return new Double3(east, up, north);
        }

        internal static Quaternion ToRotation(double yawDegrees, double pitchDegrees, double rollDegrees)
        {
            Quaternion yaw = Quaternion.AngleAxis((float)(yawDegrees % 360.0), Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(-(float)(pitchDegrees % 360.0), Vector3.right);
            Quaternion roll = Quaternion.AngleAxis(-(float)(rollDegrees % 360.0), Vector3.forward);
            return yaw * pitch * roll;
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
