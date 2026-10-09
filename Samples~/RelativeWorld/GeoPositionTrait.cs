using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Supplies WGS84 positions to Spatial for the geographic reference frame.</summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class GeoPositionTrait : Trait<GeoPoseReading>
    {
        /// <summary>Updates the Ghost's Earth-centered position from WGS84.</summary>
        /// <param name="value">A non-null observation containing WGS84 coordinates and the original SDK sample time.</param>
        /// <remarks>Re-reading a held timestamp leaves Spatial's existing observation unchanged.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Ghost.GetRequired<Spatial>().SetGeographicPosition(
                new GeoPosition(value.LatitudeDegrees, value.LongitudeDegrees, value.AltitudeMeters), value.SampleTime);
        }
    }
}
