using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Runs consumer behavior used to exercise trait failure handling.</summary>
    public sealed class PipelineActionTrait : Trait<int>
    {
        internal Action Applying;

        /// <summary>Runs the behavior selected by the consumer test.</summary>
        /// <param name="data">The reader value; this probe runs its configured action independently of the value.</param>
        public override void Apply(int data)
        {
            Applying?.Invoke();
        }
    }
}
