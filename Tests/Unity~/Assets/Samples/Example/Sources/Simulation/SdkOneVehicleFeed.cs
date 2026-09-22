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
        /// <summary>
        /// Reads the current SDK One vehicle population.
        /// </summary>
        /// <param name="elapsedSeconds">
        /// The simulated source time.
        /// </param>
        /// <returns>
        /// The current SDK One proxies.
        /// </returns>
        public IEnumerable<SdkOneVehicleProxy> ReadVehicles(float elapsedSeconds)
        {
            for (int index = 0; index < 10; index++)
            {
                Vector3 position = SampleMotion.GetCarPosition(index, elapsedSeconds);
                yield return new SdkOneVehicleProxy(
                    identifier: index.ToString(),
                    typeCode: index % 3,
                    positionX: position.x,
                    positionY: position.y,
                    positionZ: position.z,
                    steering: SampleMotion.GetCarSteering(index, elapsedSeconds));
            }
        }
    }
}
