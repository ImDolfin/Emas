using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Applies an independent articulation value.</summary>
    public sealed class PipelineArticulationTrait : Trait<int>
    {
        /// <summary>Sets the Ghost's consumer-facing articulation state.</summary>
        public override void Apply(int articulation)
        {
            GetComponent<PipelineGhost>().Articulation = articulation;
        }
    }
}
