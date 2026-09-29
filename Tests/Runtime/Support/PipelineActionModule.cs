using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Runs consumer behavior used to exercise module failure handling.</summary>
    public sealed class PipelineActionModule : EntityModule<int>
    {
        internal Action Applying;

        /// <summary>Runs the behavior selected by the consumer test.</summary>
        public override void Apply(int data)
        {
            Applying?.Invoke();
        }
    }
}
