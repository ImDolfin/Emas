using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Optionally supplies SDK ECEF velocity for Spatial's smoothing direction guard.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoVelocityModule : EntityModule<Double3?>
    {
        /// <summary>Applies ECEF metres per second, or clears the optional velocity channel when no reading is available.</summary>
        /// <param name="value">An ECEF XYZ velocity; null means the SDK does not currently supply velocity.</param>
        public override void Apply(Double3? value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            if (value.HasValue)
            {
                spatial.SetEarthCenteredVelocity(value.Value);
            }
            else
            {
                spatial.ClearVelocity();
            }
        }
    }
}
