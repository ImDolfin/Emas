using System;
using System.Globalization;
using UnityEngine;

namespace Emas
{
    /// <summary>A WGS84 latitude, longitude and ellipsoidal height, stored in double precision.</summary>
    /// <remarks>Height is measured above the WGS84 ellipsoid, not mean sea level. The default value is 0 degrees, 0 degrees, 0 metres.</remarks>
    [Serializable]
    public struct GeoPosition
    {
        private const double SemiMajorAxis = 6378137.0;
        private const double Flattening = 1.0 / 298.257223563;
        private const double EccentricitySquared = Flattening * (2.0 - Flattening);
        private const double RadiansPerDegree = Math.PI / 180.0;

        [SerializeField] private double _latitudeDegrees;
        [SerializeField] private double _longitudeDegrees;
        [SerializeField] private double _heightMeters;

        /// <summary>Creates a WGS84 position in degrees and ellipsoidal metres.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Latitude is outside [-90, 90], longitude outside [-180, 180], or a value is not finite.</exception>
        public GeoPosition(double latitudeDegrees, double longitudeDegrees, double heightMeters)
        {
            Validate(latitudeDegrees, longitudeDegrees, heightMeters);
            _latitudeDegrees = latitudeDegrees;
            _longitudeDegrees = longitudeDegrees;
            _heightMeters = heightMeters;
        }

        /// <summary>Gets latitude in degrees north.</summary>
        public double LatitudeDegrees => _latitudeDegrees;
        /// <summary>Gets longitude in degrees east.</summary>
        public double LongitudeDegrees => _longitudeDegrees;
        /// <summary>Gets height above the WGS84 ellipsoid in metres.</summary>
        public double HeightMeters => _heightMeters;

        /// <summary>Converts to Earth-centered, Earth-fixed XYZ metres without a local origin.</summary>
        public Double3 ToEarthCentered()
        {
            Validate(_latitudeDegrees, _longitudeDegrees, _heightMeters);
            double latitude = _latitudeDegrees * RadiansPerDegree;
            double longitude = _longitudeDegrees * RadiansPerDegree;
            double sin = Math.Sin(latitude);
            double cos = Math.Cos(latitude);
            double radius = SemiMajorAxis / Math.Sqrt(1.0 - EccentricitySquared * sin * sin);
            return new Double3((radius + _heightMeters) * cos * Math.Cos(longitude),
                (radius + _heightMeters) * cos * Math.Sin(longitude),
                (radius * (1.0 - EccentricitySquared) + _heightMeters) * sin);
        }

        /// <summary>Converts Earth-centered, Earth-fixed XYZ metres into a WGS84 position.</summary>
        /// <remarks>At an exact pole with X = Y = 0, longitude is defined as zero.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The position is not finite, is Earth's center, or exceeds representable geographic values.</exception>
        public static GeoPosition FromEarthCentered(Double3 position)
        {
            ReferenceFrame.ValidatePosition(position, nameof(position));
            if (position == default(Double3))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Earth's center has no geographic reference direction.");
            }

            double horizontal = Double3.Distance(new Double3(position.X, position.Y, 0), default(Double3));
            double latitude = Math.Atan2(position.Z, horizontal * (1.0 - EccentricitySquared));
            for (int iteration = 0; iteration < 32; iteration++)
            {
                double sin = Math.Sin(latitude);
                double radius = SemiMajorAxis / Math.Sqrt(1.0 - EccentricitySquared * sin * sin);
                double next = Math.Atan2(position.Z + EccentricitySquared * radius * sin, horizontal);
                double change = Math.Abs(next - latitude);
                latitude = next;
                if (change < 1e-15)
                {
                    break;
                }
            }

            double sinLatitude = Math.Sin(latitude);
            // Projection along the ellipsoid normal avoids division by cos(latitude) at the poles.
            double height = horizontal * Math.Cos(latitude) + position.Z * sinLatitude
                - SemiMajorAxis * Math.Sqrt(1.0 - EccentricitySquared * sinLatitude * sinLatitude);
            return new GeoPosition(latitude / RadiansPerDegree,
                Math.Atan2(position.Y, position.X) / RadiansPerDegree, height);
        }

        /// <summary>Formats latitude, longitude and ellipsoidal height using invariant culture.</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:R} deg, {1:R} deg, {2:R} m)",
                _latitudeDegrees, _longitudeDegrees, _heightMeters);
        }

        internal void Validate()
        {
            Validate(_latitudeDegrees, _longitudeDegrees, _heightMeters);
        }

        private static void Validate(double latitudeDegrees, double longitudeDegrees, double heightMeters)
        {
            if (!SpatialMath.IsFinite(latitudeDegrees) || latitudeDegrees < -90 || latitudeDegrees > 90)
            {
                throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), "WGS84 latitude must be finite and between -90 and 90 degrees.");
            }
            if (!SpatialMath.IsFinite(longitudeDegrees) || longitudeDegrees < -180 || longitudeDegrees > 180)
            {
                throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), "WGS84 longitude must be finite and between -180 and 180 degrees.");
            }
            if (!SpatialMath.IsFinite(heightMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(heightMeters), "WGS84 ellipsoidal height must be finite.");
            }
        }
    }
}
