using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Runs consumer behavior used to exercise trait failure handling.</summary>
    public sealed class PipelineActionTrait : Trait<int>
    {
        internal Action Applying;

        /// <summary>Runs the behavior selected by the consumer test.</summary>
        public override void Apply(int data)
        {
            Applying?.Invoke();
        }
    }
}
