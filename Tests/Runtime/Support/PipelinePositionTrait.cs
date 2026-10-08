using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Applies a mapped Cartesian value without depending on a test SDK shape.</summary>
    public sealed class PipelinePositionTrait : Trait<Double3>
    {
        /// <summary>Sets the shared position used by consumers.</summary>
        public override void Apply(Double3 position)
        {
            Ghost.GetRequired<Spatial>().SetCartesianPosition(position);
        }
    }
}
