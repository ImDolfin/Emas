using System;
using System.Collections.Generic;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Provides complete SDK-style snapshots for a moving reference entity and target entity.
    /// </summary>
    internal sealed class SimulatedGeoSdk
    {
        private double _elapsed;

        /// <summary>
        /// Advances both example entities before the next complete snapshot is read.
        /// </summary>
        internal void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            _elapsed += seconds;
        }

        /// <summary>
        /// Returns both entities sampled at the same SDK time.
        /// </summary>
        internal IEnumerable<GeoPoseReading> ReadFrame()
        {
            double time = _elapsed;
            double latitude = GeoPositionModule.DatumLatitudeDegrees;
            double longitude = GeoPositionModule.DatumLongitudeDegrees;
            double altitude = GeoPositionModule.DatumAltitudeMeters;
            return new[]
            {
                new GeoPoseReading(
                    "origin", "Moving origin", RelativeCar.Origin,
                    latitude + 0.00004 * Math.Sin(time * 0.31),
                    longitude + 0.00006 * Math.Sin(time * 0.23),
                    altitude + 1.5 * Math.Sin(time * 0.19),
                    8.0 * Math.Sin(time * 0.30),
                    3.0 * Math.Sin(time * 0.27),
                    4.0 * Math.Sin(time * 0.29)),
                new GeoPoseReading(
                    "target", "Moving target", RelativeCar.Target,
                    latitude + 0.00016 + 0.00003 * Math.Sin(time * 0.24),
                    longitude + 0.00004 + 0.00003 * Math.Cos(time * 0.31),
                    altitude + 1.0 + 0.7 * Math.Sin(time * 0.37 + 0.2),
                    25.0 + 15.0 * Math.Sin(time * 0.32),
                    4.0 * Math.Sin(time * 0.28 + 0.4),
                    5.0 * Math.Sin(time * 0.34 + 0.7))
            };
        }
    }
}