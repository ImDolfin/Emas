using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Supplies SDK yaw/pitch/roll degrees; Spatial converts them and accounts for the entity's tangent plane.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoOrientationModule : EntityModule<GeoPoseReading>
    {
        /// <summary>Updates the Ghost's local geographic attitude.</summary>
        /// <param name="value">Latest SDK snapshot with heading clockwise from north, nose-up pitch and right-wing-down roll.</param>
        public override void Apply(GeoPoseReading value)
        {
            Ghost.GetRequired<Spatial>().SetGeographicRotation(value.YawDegrees, value.PitchDegrees, value.RollDegrees);
        }
    }
}
