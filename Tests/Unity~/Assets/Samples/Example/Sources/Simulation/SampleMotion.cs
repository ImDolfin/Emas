using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    internal static class SampleMotion
    {
        internal const int CarCount = 3;

        private const float CarRadiusX = 7.0f;
        private const float CarRadiusZ = 2.2f;

        internal static Vector3 GetCarPosition(int index, float elapsedSeconds)
        {
            float phase = elapsedSeconds * 0.55f + index * (Mathf.PI * 2.0f / CarCount);
            return new Vector3(
                Mathf.Sin(phase) * CarRadiusX,
                0.30f,
                Mathf.Cos(phase) * CarRadiusZ);
        }

        internal static float GetCarSteering(int index, float elapsedSeconds)
        {
            float phase = elapsedSeconds * 0.55f + index * (Mathf.PI * 2.0f / CarCount);
            return Mathf.Sin(phase) * 0.8f;
        }

    }
}
