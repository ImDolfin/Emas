using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Represents a proxy shape supplied by the first sample SDK.
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
        /// The source X coordinate.
        /// </param>
        /// <param name="positionY">
        /// The source Y coordinate.
        /// </param>
        /// <param name="positionZ">
        /// The source Z coordinate.
        /// </param>
        /// <param name="steering">
        /// The source articulation value.
        /// </param>
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
        /// The first-SDK appearance code.
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
        /// The source X coordinate.
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
        /// The source Y coordinate.
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
        /// The source Z coordinate.
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
        /// The first-SDK articulation value.
        /// </value>
        public float Steering
        {
            get;
            private set;
        }
    }
}
