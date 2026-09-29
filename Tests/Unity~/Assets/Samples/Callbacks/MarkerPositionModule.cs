using UnityEngine;

namespace Emas.Callbacks
{
    /// <summary>Applies a source-independent local position to its Ghost root.</summary>
    public sealed class MarkerPositionModule : EntityModule<Vector3>
    {
        /// <summary>Updates the root in its anchor's coordinate frame.</summary>
        /// <param name="position">The local position supplied by the initializer's reader.</param>
        public override void Apply(Vector3 position)
        {
            transform.localPosition = position;
        }
    }
}
