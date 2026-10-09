using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Acts as SDK One's polling client and produces its proxy shape.
    /// </summary>

    public sealed class SdkOneVehicleFeed
    {
        private readonly Dictionary<string, SdkOneVehicleProxy> _current = new Dictionary<string, SdkOneVehicleProxy>();

        /// <summary>Gets the live read-only dictionary updated by ReadVehicles, keyed by stable entity ID.</summary>
        /// <value>A live population view whose proxy entries are replaced on each call to <see cref="ReadVehicles"/>.</value>
        public IReadOnlyDictionary<string, SdkOneVehicleProxy> Current => _current;

        /// <summary>
        /// Reads the current SDK One vehicle population.
        /// </summary>
        /// <param name="elapsedSeconds">
        /// Elapsed source time, in seconds.
        /// </param>
        /// <returns>
        /// The current SDK One proxies.
        /// </returns>
        /// <remarks>Refreshes the live dictionary immediately. Enumerate the returned values before calling this method again.</remarks>
        public IEnumerable<SdkOneVehicleProxy> ReadVehicles(float elapsedSeconds)
        {
            for (int index = 0; index < SampleMotion.CarCount; index++)
            {
                Vector3 position = SampleMotion.GetCarPosition(index, elapsedSeconds);
                SdkOneVehicleProxy proxy = new SdkOneVehicleProxy(
                    identifier: index.ToString(),
                    typeCode: index % 3,
                    positionX: position.x,
                    positionY: position.y,
                    positionZ: position.z,
                    steering: SampleMotion.GetCarSteering(index, elapsedSeconds));
                _current[proxy.Identifier] = proxy;
            }

            return _current.Values;
        }
    }
}
