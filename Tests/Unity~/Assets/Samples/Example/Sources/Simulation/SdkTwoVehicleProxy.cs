using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Represents a proxy shape supplied by the second sample SDK.
    /// </summary>

    public sealed class SdkTwoVehicleProxy
    {
        /// <summary>
        /// Creates a second-SDK proxy.
        /// </summary>
        /// <param name="id">
        /// The source identifier.
        /// </param>
        /// <param name="modelCode">
        /// The source appearance code.
        /// </param>
        /// <param name="coordinates">
        /// The source position.
        /// </param>
        /// <param name="wheelAngle">
        /// The source articulation value.
        /// </param>
        public SdkTwoVehicleProxy(
            int id,
            int modelCode,
            Vector3 coordinates,
            float wheelAngle)
        {
            Id = id;
            ModelCode = modelCode;
            Coordinates = coordinates;
            WheelAngle = wheelAngle;
        }

        /// <summary>
        /// Gets the numeric source identifier.
        /// </summary>
        /// <value>
        /// The numeric source identifier.
        /// </value>
        public int Id
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source appearance code.
        /// </summary>
        /// <value>
        /// The second-SDK appearance code.
        /// </value>
        public int ModelCode
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source position.
        /// </summary>
        /// <value>
        /// The second-SDK source position.
        /// </value>
        public Vector3 Coordinates
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source articulation value.
        /// </summary>
        /// <value>
        /// The second-SDK articulation value.
        /// </value>
        public float WheelAngle
        {
            get;
            private set;
        }
    }
}
