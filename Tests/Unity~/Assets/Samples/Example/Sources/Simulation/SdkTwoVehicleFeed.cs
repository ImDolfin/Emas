using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Acts as SDK Two's polling client and produces a different proxy shape.
    /// </summary>

    public sealed class SdkTwoVehicleFeed
    {
        private readonly Dictionary<string, SdkTwoVehicleProxy> _current = new Dictionary<string, SdkTwoVehicleProxy>();

        /// <summary>Gets the live read-only dictionary updated by ReadVehicles, keyed by entity ID formatted as a string.</summary>
        /// <value>A live population view whose proxy entries are replaced on each call to <see cref="ReadVehicles"/>.</value>
        public IReadOnlyDictionary<string, SdkTwoVehicleProxy> Current => _current;

        /// <summary>
        /// Reads the current SDK Two vehicle population.
        /// </summary>
        /// <param name="elapsedSeconds">
        /// Elapsed source time, in seconds.
        /// </param>
        /// <returns>
        /// The current SDK Two proxies.
        /// </returns>
        /// <remarks>Refreshes the live dictionary immediately. Enumerate the returned values before calling this method again.</remarks>
        public IEnumerable<SdkTwoVehicleProxy> ReadVehicles(float elapsedSeconds)
        {
            for (int index = 0; index < SampleMotion.CarCount; index++)
            {
                SdkTwoVehicleProxy proxy = new SdkTwoVehicleProxy(
                    id: index,
                    modelCode: index % 3,
                    coordinates: SampleMotion.GetCarPosition(index, elapsedSeconds),
                    wheelAngle: SampleMotion.GetCarSteering(index, elapsedSeconds));
                _current[proxy.Id.ToString()] = proxy;
            }

            return _current.Values;
        }
    }
}
