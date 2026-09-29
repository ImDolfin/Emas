using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Acts as the aircraft source and produces moving proxy data.
    /// </summary>

    public sealed class SimulatedAircraftFeed
    {
        private readonly Dictionary<string, SimulatedAircraftProxy> _current = new Dictionary<string, SimulatedAircraftProxy>();

        /// <summary>Gets the latest SDK observations keyed by entity ID.</summary>
        public IReadOnlyDictionary<string, SimulatedAircraftProxy> Current => _current;

        /// <summary>
        /// Reads the current aircraft population.
        /// </summary>
        /// <param name="elapsedSeconds">
        /// Elapsed source time, in seconds.
        /// </param>
        /// <returns>
        /// The current aircraft proxies.
        /// </returns>
        public IEnumerable<SimulatedAircraftProxy> ReadAircraft(float elapsedSeconds)
        {
            for (int index = 0; index < 3; index++)
            {
                SimulatedAircraftProxy proxy = new SimulatedAircraftProxy(
                    identifier: index.ToString(),
                    position: SampleMotion.GetAircraftPosition(index, elapsedSeconds));
                _current[proxy.Identifier] = proxy;
            }
            return _current.Values;
        }
    }
}
