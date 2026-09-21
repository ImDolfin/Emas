using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Acts as the aircraft source and produces moving proxy data.</summary>

    public sealed class SimulatedAircraftFeed
    {
        /// <summary>Reads the current aircraft population.</summary>
        /// <param name="elapsedSeconds">The simulated source time.</param>
        /// <returns>The current aircraft proxies.</returns>
        public IEnumerable<SimulatedAircraftProxy> ReadAircraft(float elapsedSeconds)
        {
            for (var index = 0; index < 3; index++)
            {
                yield return new SimulatedAircraftProxy(
                    index.ToString(),
                    SampleMotion.GetAircraftPosition(index, elapsedSeconds));
            }
        }
    }
}
