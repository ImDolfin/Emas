using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Applies a source-independent shared orientation to Spatial.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoOrientationModule : EntityModule<Quaternion>
    {
        /// <summary>Updates the Ghost's shared orientation.</summary>
        /// <param name="value">The mapped value supplied by the initializer's reader.</param>
        public override void Apply(Quaternion value)
        {
            GetComponent<Spatial>().SetRotation(value);
        }
    }
}
