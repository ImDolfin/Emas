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
        /// Position in the owning anchor's local Unity axes.
        /// </param>
        /// <param name="wheelAngle">
        /// Normalized steering in [-1, 1], despite the simulated SDK's angle-like name.
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
        /// Gets the position in the owning anchor's local Unity axes.
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
        /// Gets normalized steering in [-1, 1]; this simulated SDK does not use angular units.
        /// </summary>
        /// <value>
        /// The steering input mapped directly to IArticulate.Steering.
        /// </value>
        public float WheelAngle
        {
            get;
            private set;
        }
    }
}
