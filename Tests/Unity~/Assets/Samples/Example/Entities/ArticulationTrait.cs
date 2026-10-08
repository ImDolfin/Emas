using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Stores normalized steering from either SDK for the manifested vehicle's articulation.</summary>
    public sealed class ArticulationTrait : Trait<float>, IArticulate
    {
        /// <inheritdoc />
        public float Steering { get; private set; }

        /// <summary>Replaces the steering value exposed through IArticulate.</summary>
        /// <param name="value">The SDK's steering input, expected in [-1, 1]; stored without clamping.</param>
        public override void Apply(float value)
        {
            Steering = value;
        }
    }
}
