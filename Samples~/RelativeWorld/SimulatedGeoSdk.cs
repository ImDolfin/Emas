using System;
using System.Collections.Generic;
using System.Globalization;

namespace Emas.RelativeWorld
{
    /// <summary>Simulates a car driving north past stationary roadside parking.</summary>
    internal sealed class SimulatedGeoSdk
    {
        private const double SpeedMetersPerSecond = 8.0;
        private const double ParkingSpacingMeters = 80.0;
        private const double FirstParkingNorthMeters = 24.0;
        private double _elapsed;
        private readonly Dictionary<string, GeoPoseReading> _current = new Dictionary<string, GeoPoseReading>();

        internal IReadOnlyDictionary<string, GeoPoseReading> Current => _current;

        internal void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            _elapsed += seconds;
        }

        internal IEnumerable<GeoPoseReading> ReadFrame()
        {
            double north = _elapsed * SpeedMetersPerSecond;
            _current.Clear();
            Add("origin", "Driving origin", GeoSource.Kind, new Variant("origin"), 1.5, north, 0.0);

            double orbit = _elapsed * Math.PI / 4.0;
            Add("bird", "Circling bird", GeoSource.BirdKind, new Variant("bird"),
                1.5 + 4.0 * Math.Cos(orbit), north + 4.0 * Math.Sin(orbit),
                -orbit * 180.0 / Math.PI, 3.2, -20.0);

            // Read only the nearby stretch of road; each parking bay has a stable identity and pose.
            long first = Math.Max(0L, (long)Math.Ceiling((north - 22.0 - FirstParkingNorthMeters) / ParkingSpacingMeters));
            long last = (long)Math.Floor((north + 38.0 - FirstParkingNorthMeters) / ParkingSpacingMeters);
            for (long index = first; index <= last; index++)
            {
                bool left = index % 2 == 0;
                Add("parked-" + index.ToString(CultureInfo.InvariantCulture), left ? "Parked left" : "Parked right",
                    GeoSource.Kind, new Variant("target"), left ? -4.5 : 4.5,
                    FirstParkingNorthMeters + index * ParkingSpacingMeters, left ? 180.0 : 0.0);
            }
            return _current.Values;
        }

        private void Add(string id, string label, Kind kind, Variant variant, double east, double north, double yaw,
            double up = 0.0, double roll = 0.0)
        {
            // Produce geographic SDK data from a level road in the fixed datum's tangent plane.
            GeoProjection.ToGeographic(new Double3(east, up, north), out double latitude,
                out double longitude, out double altitude);
            _current.Add(id, new GeoPoseReading(id, label, kind, variant, latitude, longitude, altitude, yaw, 0.0, roll));
        }
    }
}
