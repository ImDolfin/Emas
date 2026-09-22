using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Application-defined extensible population kinds.</summary>

    public static class SampleKinds
    {
        /// <summary>Identifies sample cars.</summary>
        public static readonly Kind Car = new Kind("sample.vehicle.car");

        /// <summary>Identifies sample aircraft.</summary>
        public static readonly Kind Aircraft = new Kind("sample.vehicle.aircraft");
    }
}
