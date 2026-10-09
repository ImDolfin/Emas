using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Supplies SDK yaw/pitch/roll degrees; Spatial converts them and accounts for the entity's tangent plane.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoOrientationTrait : Trait<GeoPoseReading>
    {
        /// <summary>Updates the Ghost's local geographic attitude.</summary>
        /// <param name="value">A non-null snapshot with heading clockwise from north, nose-up pitch and right-wing-down roll.</param>
        /// <remarks>The observation's timestamp is preserved so repeated or older timed attitudes are ignored by Spatial.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Ghost.GetRequired<Spatial>().SetGeographicRotation(value.YawDegrees, value.PitchDegrees, value.RollDegrees, value.SampleTime);
        }
    }
}
