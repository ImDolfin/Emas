using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Defines the signed source axes corresponding to Unity right, up and forward.
    /// </summary>
    /// <remarks>
    /// Each source axis must appear exactly once. The mapping determines handedness and preserves distances.
    /// Positions and quaternion rotation matrices use the same source basis for both world and local axes.
    /// </remarks>
    [Serializable]
    public struct CoordinateSystem
    {
        /// <summary>Unity coordinates: X right, Y up and Z forward.</summary>
        public static readonly CoordinateSystem Unity = new CoordinateSystem(Axis.PositiveX, Axis.PositiveY, Axis.PositiveZ);

        /// <summary>Geographic coordinates: X east, Y north and Z up.</summary>
        public static readonly CoordinateSystem EastNorthUp = new CoordinateSystem(Axis.PositiveX, Axis.PositiveZ, Axis.PositiveY);

        /// <summary>Geographic coordinates: X north, Y east and Z down.</summary>
        public static readonly CoordinateSystem NorthEastDown = new CoordinateSystem(Axis.PositiveY, Axis.NegativeZ, Axis.PositiveX);

        [SerializeField]
        private Axis _right;
        [SerializeField]
        private Axis _up;
        [SerializeField]
        private Axis _forward;

        /// <summary>
        /// Creates a coordinate convention from three distinct signed source axes.
        /// </summary>
        /// <param name="right">The source axis that maps to Unity positive X.</param>
        /// <param name="up">The source axis that maps to Unity positive Y.</param>
        /// <param name="forward">The source axis that maps to Unity positive Z.</param>
        /// <exception cref="ArgumentException">An axis is undefined or used more than once.</exception>
        public CoordinateSystem(Axis right, Axis up, Axis forward)
        {
            _right = right;
            _up = up;
            _forward = forward;
            string error = GetConfigurationError();
            if (error != null)
            {
                throw new ArgumentException(error);
            }
        }

        /// <summary>Gets the source axis mapped to Unity right.</summary>
        /// <value>The signed source axis that produces Unity positive X.</value>
        public Axis Right
        {
            get
            {
                return _right;
            }
        }

        /// <summary>Gets the source axis mapped to Unity up.</summary>
        /// <value>The signed source axis that produces Unity positive Y.</value>
        public Axis Up
        {
            get
            {
                return _up;
            }
        }

        /// <summary>Gets the source axis mapped to Unity forward.</summary>
        /// <value>The signed source axis that produces Unity positive Z.</value>
        public Axis Forward
        {
            get
            {
                return _forward;
            }
        }

        internal string GetConfigurationError()
        {
            int right = Index(_right);
            int up = Index(_up);
            int forward = Index(_forward);
            if (right < 0 || up < 0 || forward < 0)
            {
                return "Coordinate system axes must be +X, -X, +Y, -Y, +Z or -Z.";
            }

            if (right == up || right == forward || up == forward)
            {
                return "Coordinate system must use each source axis X, Y and Z exactly once.";
            }

            return null;
        }

        internal Double3 ToUnity(Double3 value)
        {
            return new Double3(Component(value, _right), Component(value, _up), Component(value, _forward));
        }

        internal Double3 ToSource(Double3 value)
        {
            return new Double3(SourceComponent(value, 0), SourceComponent(value, 1), SourceComponent(value, 2));
        }

        internal Quaternion ToUnityRotation(Quaternion value)
        {
            Double3 vector = ToUnity(new Double3(value.x, value.y, value.z));
            return Rotation(vector, value.w);
        }

        internal Quaternion ToSourceRotation(Quaternion value)
        {
            Double3 vector = ToSource(new Double3(value.x, value.y, value.z));
            return Rotation(vector, value.w);
        }

        internal static CoordinateSystem RequireRightHandedBodyAxes(CoordinateSystem? bodyAxes)
        {
            CoordinateSystem value = bodyAxes ?? NorthEastDown;
            string error = value.GetConfigurationError();
            if (error != null)
            {
                throw new ArgumentException(error, nameof(bodyAxes));
            }

            // A right-handed source mapped into Unity's left-handed directions requires a reflection.
            if (value.Determinant != -1)
            {
                throw new ArgumentException("ECEF attitudes require right-handed body axes. Use ENU, NED or a right-handed custom mapping.", nameof(bodyAxes));
            }

            return value;
        }

        private int Determinant
        {
            get
            {
                int right = Index(_right);
                int up = Index(_up);
                int forward = Index(_forward);
                int inversions = (right > up ? 1 : 0) + (right > forward ? 1 : 0) + (up > forward ? 1 : 0);
                return (inversions % 2 == 0 ? 1 : -1) * Sign(_right) * Sign(_up) * Sign(_forward);
            }
        }

        private Quaternion Rotation(Double3 vector, float scalar)
        {
            // For an orthogonal basis B, B R(q) B^-1 has quaternion (det(B) * B * q.xyz, q.w).
            // The determinant factor also handles reflections; treating q.xyz as a position would reverse turns.
            int determinant = Determinant;
            return new Quaternion((float)(determinant * vector.X), (float)(determinant * vector.Y),
                (float)(determinant * vector.Z), scalar);
        }

        private double SourceComponent(Double3 value, int index)
        {
            if (Index(_right) == index)
            {
                return Sign(_right) * value.X;
            }

            if (Index(_up) == index)
            {
                return Sign(_up) * value.Y;
            }

            return Sign(_forward) * value.Z;
        }

        private static int Index(Axis axis)
        {
            switch (axis)
            {
                case Axis.PositiveX:
                case Axis.NegativeX:
                    return 0;
                case Axis.PositiveY:
                case Axis.NegativeY:
                    return 1;
                case Axis.PositiveZ:
                case Axis.NegativeZ:
                    return 2;
                default:
                    return -1;
            }
        }

        private static int Sign(Axis axis)
        {
            return (int)axis < 0 ? -1 : 1;
        }

        private static double Component(Double3 value, Axis axis)
        {
            switch (Index(axis))
            {
                case 0:
                    return Sign(axis) * value.X;
                case 1:
                    return Sign(axis) * value.Y;
                default:
                    return Sign(axis) * value.Z;
            }
        }
    }
}
