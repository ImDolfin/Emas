using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Supplies attitude in local east/up/north axes; the geographic frame accounts for tangent orientation.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoOrientationModule : EntityModule<Quaternion>
    {
        /// <summary>Updates the Ghost's local geographic attitude.</summary>
        /// <param name="value">The mapped value supplied by the initializer's reader.</param>
        public override void Apply(Quaternion value)
        {
            GetComponent<Spatial>().SetRotation(value);
        }
    }
}
