using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>A Ghost that declares the independent traits used by lifecycle tests.</summary>
    [RequireComponent(typeof(Spatial))]
    [RequireComponent(typeof(PipelinePositionTrait), typeof(PipelineArticulationTrait), typeof(PipelineActionTrait))]
    public sealed class PipelineGhost : Ghost
    {
        internal Action Updating;
        internal int UpdateCount;

        internal int Articulation { get; set; }

        /// <summary>Runs consumer behavior in the realm's Ghost update phase.</summary>
        protected override void OnUpdate()
        {
            UpdateCount++;
            Updating?.Invoke();
        }
    }
}
