using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>A Ghost that declares the independent modules used by lifecycle tests.</summary>
    [RequireComponent(typeof(Spatial))]
    [RequireComponent(typeof(PipelinePositionModule), typeof(PipelineArticulationModule), typeof(PipelineActionModule))]
    public sealed class PipelineGhost : Ghost
    {
        internal int Articulation { get; set; }
        internal Action Updating;
        internal int UpdateCount;

        /// <summary>Runs consumer behavior in the realm's Ghost update phase.</summary>
        protected override void OnUpdate()
        {
            UpdateCount++;
            Updating?.Invoke();
        }
    }
}
