using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Acts as SDK One's polling client and produces its proxy shape.</summary>

    public sealed class SdkOneVehicleFeed
    {
        /// <summary>Reads the current SDK One vehicle population.</summary>
        /// <param name="elapsedSeconds">The simulated source time.</param>
        /// <returns>The current SDK One proxies.</returns>
        public IEnumerable<SdkOneVehicleProxy> ReadVehicles(float elapsedSeconds)
        {
            for (var index = 0; index < 10; index++)
            {
                var position = SampleMotion.GetCarPosition(index, elapsedSeconds);
                yield return new SdkOneVehicleProxy(
                    index.ToString(),
                    index % 3,
                    position.x,
                    position.y,
                    position.z,
                    SampleMotion.GetCarSteering(index, elapsedSeconds));
            }
        }
    }
}
