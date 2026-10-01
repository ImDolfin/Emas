using System;
using UnityEngine;

namespace Emas
{
    // WGS84 tangent axes in ECEF. Local components are east/up/north to match Unity.
    internal readonly struct GeographicBasis
    {
        private readonly Double3 _east;
        private readonly Double3 _up;
        private readonly Double3 _north;

        internal GeographicBasis(GeoPosition position)
        {
            position.Validate();
            double latitude = position.LatitudeDegrees * Math.PI / 180.0;
            double longitude = position.LongitudeDegrees * Math.PI / 180.0;
            double sinLatitude = Math.Sin(latitude);
            double cosLatitude = Math.Cos(latitude);
            double sinLongitude = Math.Sin(longitude);
            double cosLongitude = Math.Cos(longitude);
            _east = new Double3(-sinLongitude, cosLongitude, 0);
            _up = new Double3(cosLatitude * cosLongitude, cosLatitude * sinLongitude, sinLatitude);
            _north = new Double3(-sinLatitude * cosLongitude, -sinLatitude * sinLongitude, cosLatitude);
        }

        internal Double3 ToLocal(Double3 displacement)
        {
            return new Double3(Dot(_east, displacement), Dot(_up, displacement), Dot(_north, displacement));
        }

        internal Double3 ToEarthCentered(Double3 displacement)
        {
            return _east * displacement.X + _up * displacement.Y + _north * displacement.Z;
        }

        internal Quaternion RotationFrom(GeoPosition position)
        {
            GeographicBasis other = new GeographicBasis(position);
            Double3 forward = ToLocal(other._north);
            Double3 up = ToLocal(other._up);
            return Quaternion.LookRotation(new Vector3((float)forward.X, (float)forward.Y, (float)forward.Z),
                new Vector3((float)up.X, (float)up.Y, (float)up.Z));
        }

        // ECEF attitude maps source body vectors into ECEF. Body axes convert those vectors to Unity's model axes.
        internal Quaternion ToLocalEarthCenteredRotation(Quaternion rotation, CoordinateSystem bodyAxes)
        {
            Double3 forward = ToLocal(SpatialMath.Rotate(rotation, bodyAxes.ToSource(new Double3(0, 0, 1))));
            Double3 up = ToLocal(SpatialMath.Rotate(rotation, bodyAxes.ToSource(new Double3(0, 1, 0))));
            return Quaternion.LookRotation(ToVector3(forward), ToVector3(up));
        }

        internal Quaternion ToEarthCenteredRotation(Quaternion localRotation, CoordinateSystem bodyAxes)
        {
            Double3 forward = ToEarthCentered(SpatialMath.Rotate(localRotation, bodyAxes.ToUnity(new Double3(0, 0, 1))));
            Double3 up = ToEarthCentered(SpatialMath.Rotate(localRotation, bodyAxes.ToUnity(new Double3(0, 1, 0))));
            return Quaternion.LookRotation(ToVector3(forward), ToVector3(up));
        }

        private static Vector3 ToVector3(Double3 vector)
        {
            return new Vector3((float)vector.X, (float)vector.Y, (float)vector.Z);
        }

        private static double Dot(Double3 left, Double3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }
    }
}
