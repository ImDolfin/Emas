using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Acts as the cockpit source and produces moving screen-coordinate data.</summary>

    public sealed class SimulatedCockpitFeed
    {
        /// <summary>Reads the current cockpit marker data.</summary>
        /// <param name="elapsedSeconds">The simulated source time.</param>
        /// <returns>The current source screen ID and normalized coordinate.</returns>
        public CockpitMarkerData ReadMarker(float elapsedSeconds)
        {
            var x = 0.5f + Mathf.Sin(elapsedSeconds * 1.15f) * 0.36f;
            var y = 0.5f + Mathf.Cos(elapsedSeconds * 1.55f) * 0.30f;
            return new CockpitMarkerData("screen", new Vector2(x, y));
        }
    }
}
