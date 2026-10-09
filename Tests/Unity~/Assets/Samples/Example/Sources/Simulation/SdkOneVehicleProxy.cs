using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Captures one SDK One vehicle observation with separate position coordinates and normalized steering.
    /// </summary>

    public sealed class SdkOneVehicleProxy
    {
        /// <summary>
        /// Creates a first-SDK proxy.
        /// </summary>
        /// <param name="identifier">
        /// The source identifier.
        /// </param>
        /// <param name="typeCode">
        /// The source appearance code.
        /// </param>
        /// <param name="positionX">
        /// The X coordinate in the owning Anchor's local Unity units.
        /// </param>
        /// <param name="positionY">
        /// The Y coordinate in the owning Anchor's local Unity units.
        /// </param>
        /// <param name="positionZ">
        /// The Z coordinate in the owning Anchor's local Unity units.
        /// </param>
        /// <param name="steering">
        /// Normalized steering, expected in [-1, 1]; stored without clamping.
        /// </param>
        /// <remarks>Stores the supplied values without validation or unit conversion.</remarks>
        public SdkOneVehicleProxy(
            string identifier,
            int typeCode,
            float positionX,
            float positionY,
            float positionZ,
            float steering)
        {
            Identifier = identifier;
            TypeCode = typeCode;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            Steering = steering;
        }

        /// <summary>
        /// Gets the source identifier.
        /// </summary>
        /// <value>
        /// The source identifier.
        /// </value>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source appearance code.
        /// </summary>
        /// <value>
        /// The first-SDK appearance code: 0 selects a small car, 1 a large car, and other values a truck.
        /// </value>
        public int TypeCode
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source X coordinate.
        /// </summary>
        /// <value>
        /// Position along the Anchor's local right axis, in Unity units.
        /// </value>
        public float PositionX
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source Y coordinate.
        /// </summary>
        /// <value>
        /// Position along the Anchor's local up axis, in Unity units.
        /// </value>
        public float PositionY
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source Z coordinate.
        /// </summary>
        /// <value>
        /// Position along the Anchor's local forward axis, in Unity units.
        /// </value>
        public float PositionZ
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source articulation value.
        /// </summary>
        /// <value>
        /// The normalized steering input mapped directly to <see cref="IArticulate.Steering"/>.
        /// </value>
        public float Steering
        {
            get;
            private set;
        }
    }
}
