using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Stores source-independent data for the Ghost's consumers.</summary>
    public sealed class ArticulationModule : EntityModule<float>, IArticulate
    {
        /// <inheritdoc />
        public float Steering { get; private set; }

        /// <summary>Updates the value exposed to consumers.</summary>
        /// <param name="value">The value read through the initializer's mapping.</param>
        public override void Apply(float value)
        {
            Steering = value;
        }
    }
}
