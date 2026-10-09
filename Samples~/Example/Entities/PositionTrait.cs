using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Stores anchor-local position from either SDK for root logic and view consumers.</summary>
    public sealed class PositionTrait : Trait<Vector3>, I3DPosition
    {
        /// <summary>Gets the last supplied position in the owning Anchor's local Unity coordinate frame.</summary>
        /// <value>The mapped source position, or the zero vector before the first reading.</value>
        public Vector3 Position { get; private set; }

        /// <summary>Replaces the stored position; ApplyPosition moves the root from this contract in LateUpdate.</summary>
        /// <param name="value">Position in the owning anchor's local Unity axes.</param>
        public override void Apply(Vector3 value)
        {
            Position = value;
        }
    }
}
