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
    }
}
