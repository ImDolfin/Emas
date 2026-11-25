using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Defines the signed source axes corresponding to Unity right, up and forward.</summary>
    /// <remarks>
    /// Each source axis must appear exactly once. The mapping determines handedness and preserves distances.
    /// Positions and quaternion rotation matrices use the same source basis for both world and local axes.
    /// </remarks>
    [Serializable]
    public struct CoordinateSystem
    {
        // Share immutable mappings instead of storing derived state in serialized
        // value types. Copies, Inspector edits and deserialization then use current axes.
        internal sealed class Mapping
        {
            private readonly Axis _right;
            private readonly Axis _up;
            private readonly Axis _forward;
            private readonly Axis _sourceX;
            private readonly Axis _sourceY;
            private readonly Axis _sourceZ;

            internal int Determinant { get; }

            internal Mapping(Axis right, Axis up, Axis forward)
            {
                _right = right;
                _up = up;
                _forward = forward;
                _sourceX = SourceAxis(right, up, forward, 0);
                _sourceY = SourceAxis(right, up, forward, 1);
                _sourceZ = SourceAxis(right, up, forward, 2);

                // A reflection reverses angular vectors as well as the axis order.
                int rightIndex = Index(right);
                int upIndex = Index(up);
                int forwardIndex = Index(forward);
                int inversions = (rightIndex > upIndex ? 1 : 0)
                    + (rightIndex > forwardIndex ? 1 : 0) + (upIndex > forwardIndex ? 1 : 0);
                Determinant = (inversions % 2 == 0 ? 1 : -1) * Sign(right) * Sign(up) * Sign(forward);
            }

            internal Double3 ToUnity(Double3 value)
            {
                return new Double3(Component(value, _right), Component(value, _up), Component(value, _forward));
            }

            internal Double3 ToSource(Double3 value)
            {
                return new Double3(Component(value, _sourceX), Component(value, _sourceY), Component(value, _sourceZ));
            }

            internal Double3 ToUnityAngularVelocity(Double3 value)
            {
                return ToUnity(value) * Determinant;
            }

            internal Double3 ToSourceAngularVelocity(Double3 value)
            {
                return ToSource(value) * Determinant;
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

            private Quaternion Rotation(Double3 vector, float scalar)
            {
                // For an orthogonal basis B, B R(q) B^-1 has quaternion
                // (det(B) * B * q.xyz, q.w), including mirrored conventions.
                return new Quaternion((float)(Determinant * vector.X), (float)(Determinant * vector.Y),
                    (float)(Determinant * vector.Z), scalar);
            }

            private static Axis SourceAxis(Axis right, Axis up, Axis forward, int index)
            {
                if (Index(right) == index)
                {
                    return (Axis)Sign(right);
                }

                if (Index(up) == index)
                {
                    return (Axis)(Sign(up) * 2);
                }

                return (Axis)(Sign(forward) * 3);
            }
        }

        /// <summary>Unity coordinates: X right, Y up and Z forward.</summary>
        public static readonly CoordinateSystem Unity = new CoordinateSystem(Axis.PositiveX, Axis.PositiveY, Axis.PositiveZ);

        /// <summary>Geographic coordinates: X east, Y north and Z up.</summary>
        public static readonly CoordinateSystem EastNorthUp = new CoordinateSystem(Axis.PositiveX, Axis.PositiveZ, Axis.PositiveY);

        /// <summary>Geographic coordinates: X north, Y east and Z down.</summary>
        public static readonly CoordinateSystem NorthEastDown = new CoordinateSystem(Axis.PositiveY, Axis.NegativeZ, Axis.PositiveX);

        private const int AxisCount = 7;
        private static readonly Mapping[] _mappings = CreateMappings();

        [Tooltip("Signed source axis mapped to Unity X (right). Each source axis must be used once.")]
        [SerializeField]
        private Axis _right;
        [Tooltip("Signed source axis mapped to Unity Y (up). Each source axis must be used once.")]
        [SerializeField]
        private Axis _up;
        [Tooltip("Signed source axis mapped to Unity Z (forward). Each source axis must be used once.")]
        [SerializeField]
        private Axis _forward;

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

        internal Mapping Prepared => _mappings[((int)_right + 3) * AxisCount * AxisCount
            + ((int)_up + 3) * AxisCount + (int)_forward + 3];

        /// <summary>Creates a coordinate convention from three distinct signed source axes.</summary>
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
            return Prepared.ToUnity(value);
        }

        internal Double3 ToSource(Double3 value)
        {
            return Prepared.ToSource(value);
        }

        internal Double3 ToUnityAngularVelocity(Double3 value)
        {
            return Prepared.ToUnityAngularVelocity(value);
        }

        internal Double3 ToSourceAngularVelocity(Double3 value)
        {
            return Prepared.ToSourceAngularVelocity(value);
        }

        internal Quaternion ToUnityRotation(Quaternion value)
        {
            return Prepared.ToUnityRotation(value);
        }

        internal Quaternion ToSourceRotation(Quaternion value)
        {
            return Prepared.ToSourceRotation(value);
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
            if (value.Prepared.Determinant != -1)
            {
                throw new ArgumentException("ECEF attitudes require right-handed body axes. Use ENU, NED or a right-handed custom mapping.", nameof(bodyAxes));
            }

            return value;
        }

        private static Mapping[] CreateMappings()
        {
            // Signed axes occupy -3 through +3. Prepare the 48 valid conventions
            // and the zero value used by default contexts; leave invalid slots empty.
            Mapping[] mappings = new Mapping[AxisCount * AxisCount * AxisCount];
            int index = 0;
            for (int right = -3; right <= 3; right++)
            {
                for (int up = -3; up <= 3; up++)
                {
                    for (int forward = -3; forward <= 3; forward++)
                    {
                        bool valid = right != 0 && up != 0 && forward != 0
                            && Math.Abs(right) != Math.Abs(up) && Math.Abs(right) != Math.Abs(forward)
                            && Math.Abs(up) != Math.Abs(forward);
                        if (valid || (right == 0 && up == 0 && forward == 0))
                        {
                            mappings[index] = new Mapping((Axis)right, (Axis)up, (Axis)forward);
                        }

                        index++;
                    }
                }
            }

            return mappings;
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
            switch (axis)
            {
                case Axis.PositiveX:
                    return value.X;
                case Axis.NegativeX:
                    return -value.X;
                case Axis.PositiveY:
                    return value.Y;
                case Axis.NegativeY:
                    return -value.Y;
                case Axis.PositiveZ:
                    return value.Z;
                case Axis.NegativeZ:
                    return -value.Z;
                default:
                    return Sign(axis) * value.Z;
            }
        }
    }
}
