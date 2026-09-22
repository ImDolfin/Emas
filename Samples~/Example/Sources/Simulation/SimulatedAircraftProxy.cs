using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Represents a moving aircraft proxy used by the sample source.
    /// </summary>

    public sealed class SimulatedAircraftProxy
    {
        /// <summary>
        /// Creates an aircraft proxy.
        /// </summary>
        /// <param name="identifier">
        /// The source identifier.
        /// </param>
        /// <param name="position">
        /// The source position.
        /// </param>
        public SimulatedAircraftProxy(string identifier, Vector3 position)
        {
            Identifier = identifier;
            Position = position;
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
        /// Gets the source position.
        /// </summary>
        /// <value>
        /// The source position.
        /// </value>
        public Vector3 Position
        {
            get;
            private set;
        }
    }
}
