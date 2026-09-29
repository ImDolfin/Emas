using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Applies a source-independent shared Cartesian position to Spatial.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoPositionModule : EntityModule<Double3>
    {
        /// <summary>Updates the Ghost's shared Cartesian position.</summary>
        /// <param name="value">The mapped value supplied by the initializer's reader.</param>
        public override void Apply(Double3 value)
        {
            GetComponent<Spatial>().SetPosition(value);
        }
    }
}
