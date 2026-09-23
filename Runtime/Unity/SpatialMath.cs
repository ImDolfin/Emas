using System;
using UnityEngine;

namespace Emas
{
    internal static class SpatialMath
    {
        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        internal static bool IsFinite(Double3 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        internal static Quaternion NormalizeRotation(Quaternion value, string paramName)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z) || !IsFinite(value.w))
            {
                throw new ArgumentOutOfRangeException(paramName, "A rotation must have finite components.");
            }

            double x = value.x;
            double y = value.y;
            double z = value.z;
            double w = value.w;
            double length = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (length == 0d)
            {
                throw new ArgumentOutOfRangeException(paramName, "A rotation must have nonzero length.");
            }

            return new Quaternion((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
        }

        internal static Double3 Rotate(Quaternion normalizedRotation, Double3 vector)
        {
            double x = normalizedRotation.x;
            double y = normalizedRotation.y;
            double z = normalizedRotation.z;
            double w = normalizedRotation.w;
            double xx = x * x;
            double yy = y * y;
            double zz = z * z;
            double ww = w * w;
            double xy = x * y;
            double xz = x * z;
            double yz = y * z;
            double xw = x * w;
            double yw = y * w;
            double zw = z * w;
            double squaredLength = xx + yy + zz + ww;

            // A normalized Unity quaternion still has float rounding error. Dividing by
            // its double-precision norm avoids introducing scale error into large offsets.
            return new Double3(
                ((xx + ww - yy - zz) / squaredLength) * vector.X
                    + (2d * (xy - zw) / squaredLength) * vector.Y
                    + (2d * (xz + yw) / squaredLength) * vector.Z,
                (2d * (xy + zw) / squaredLength) * vector.X
                    + ((yy + ww - xx - zz) / squaredLength) * vector.Y
                    + (2d * (yz - xw) / squaredLength) * vector.Z,
                (2d * (xz - yw) / squaredLength) * vector.X
                    + (2d * (yz + xw) / squaredLength) * vector.Y
                    + ((zz + ww - xx - yy) / squaredLength) * vector.Z);
        }

        internal static bool TryToVector3(Double3 value, out Vector3 result)
        {
            if (!IsFinite(value)
                || Math.Abs(value.X) > float.MaxValue
                || Math.Abs(value.Y) > float.MaxValue
                || Math.Abs(value.Z) > float.MaxValue)
            {
                result = default;
                return false;
            }

            result = new Vector3((float)value.X, (float)value.Y, (float)value.Z);
            return true;
        }
    }
}
