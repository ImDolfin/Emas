using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    internal static class SampleMotion
    {
        private const float CarRadiusX = 7.0f;
        private const float CarRadiusZ = 2.2f;

        internal static Vector3 GetCarPosition(int index, float elapsedSeconds)
        {
            float phase = elapsedSeconds * 0.55f + index * (Mathf.PI * 2.0f / 10.0f);
            return new Vector3(
                Mathf.Sin(phase) * CarRadiusX,
                0.30f,
                Mathf.Cos(phase) * CarRadiusZ);
        }

        internal static float GetCarSteering(int index, float elapsedSeconds)
        {
            float phase = elapsedSeconds * 0.55f + index * (Mathf.PI * 2.0f / 10.0f);
            return Mathf.Sin(phase) * 0.8f;
        }

        internal static Vector3 GetAircraftPosition(int index, float elapsedSeconds)
        {
            float phase = elapsedSeconds * 0.35f + index * (Mathf.PI * 2.0f / 3.0f);
            return new Vector3(
                Mathf.Sin(phase) * 6.0f,
                3.8f + Mathf.Sin(phase * 2.0f) * 0.65f,
                Mathf.Cos(phase) * 3.0f);
        }
    }
}
