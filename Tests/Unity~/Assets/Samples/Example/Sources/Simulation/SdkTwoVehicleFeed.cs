using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Acts as SDK Two's polling client and produces a different proxy shape.</summary>

    public sealed class SdkTwoVehicleFeed
    {
        /// <summary>Reads the current SDK Two vehicle population.</summary>
        /// <param name="elapsedSeconds">The simulated source time.</param>
        /// <returns>The current SDK Two proxies.</returns>
        public IEnumerable<SdkTwoVehicleProxy> ReadVehicles(float elapsedSeconds)
        {
            for (var index = 0; index < 10; index++)
            {
                yield return new SdkTwoVehicleProxy(
                    id: index,
                    modelCode: index % 3,
                    coordinates: SampleMotion.GetCarPosition(index, elapsedSeconds),
                    wheelAngle: SampleMotion.GetCarSteering(index, elapsedSeconds));
            }
        }
    }
}
