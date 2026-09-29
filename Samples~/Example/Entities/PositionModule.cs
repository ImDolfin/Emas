using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Stores source-independent data for the Ghost's consumers.</summary>
    public sealed class PositionModule : EntityModule<Vector3>, I3DPosition
    {
        /// <inheritdoc />
        public Vector3 Position { get; private set; }

        /// <summary>Updates the value exposed to consumers.</summary>
        /// <param name="value">The value read through the initializer's mapping.</param>
        public override void Apply(Vector3 value)
        {
            Position = value;
        }
    }
}
