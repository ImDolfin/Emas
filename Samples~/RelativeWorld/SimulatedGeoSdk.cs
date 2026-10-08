using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

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
        private readonly Dictionary<string, GeoPoseReading> _releasedFeet = new Dictionary<string, GeoPoseReading>();
        private bool _birdFeetAttached = true;

        internal IReadOnlyDictionary<string, GeoPoseReading> Current => _current;

        internal void SetBirdFeetAttached(bool attached)
        {
            if (_birdFeetAttached == attached)
            {
                return;
            }

            if (!attached)
            {
                ReadFrame();
                _releasedFeet.Clear();
                foreach (GeoPoseReading reading in _current.Values)
                {
                    if (reading.Kind == GeoSource.BirdFootKind)
                    {
                        // Release from the current SDK pose, preserving the same identities and absolute channels.
                        _releasedFeet.Add(reading.Id, new GeoPoseReading(reading.Id, reading.Label, reading.Kind,
                            reading.Variant, reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters,
                            reading.YawDegrees, reading.PitchDegrees, reading.RollDegrees, sampleTime: reading.SampleTime));
                    }
                }
            }

            _birdFeetAttached = attached;
        }

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
            GeoPoseReading bird = CreateReading("bird", "Circling bird", GeoSource.BirdKind, new Variant("bird"),
                1.5 + 4.0 * Math.Cos(orbit), north + 4.0 * Math.Sin(orbit),
                -orbit * 180.0 / Math.PI, 3.2, -20.0);
            // Parts are reported before their parent to demonstrate attachment by identity rather than a live root.
            AddFoot("bird-left-foot", "Bird left foot", bird, new Vector3(0.02f, -0.085f, 0.09f));
            AddFoot("bird-right-foot", "Bird right foot", bird, new Vector3(0.02f, 0.085f, 0.09f));
            _current.Add(bird.Id, bird);

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
            _current.Add(id, CreateReading(id, label, kind, variant, east, north, yaw, up, roll));
        }

        private void AddFoot(string id, string label, GeoPoseReading bird, Vector3 bodyOffset)
        {
            if (!_birdFeetAttached)
            {
                _current.Add(id, _releasedFeet[id]);
                return;
            }

            // Produce coherent absolute SDK input as well as attachment state, so a release needs no pose synthesis in Emas.
            Vector3 local = new Vector3(bodyOffset.y, -bodyOffset.z, bodyOffset.x);
            Quaternion attitude = Quaternion.AngleAxis((float)(bird.YawDegrees % 360.0), Vector3.up)
                * Quaternion.AngleAxis(-(float)(bird.PitchDegrees % 360.0), Vector3.right)
                * Quaternion.AngleAxis(-(float)(bird.RollDegrees % 360.0), Vector3.forward);
            Vector3 tangent = attitude * local; // East, up, north at the bird's WGS84 position.
            double latitude = bird.LatitudeDegrees * Math.PI / 180.0;
            double longitude = bird.LongitudeDegrees * Math.PI / 180.0;
            double sinLatitude = Math.Sin(latitude);
            double cosLatitude = Math.Cos(latitude);
            double sinLongitude = Math.Sin(longitude);
            double cosLongitude = Math.Cos(longitude);
            Double3 offset = new Double3(
                -sinLongitude * tangent.x + cosLatitude * cosLongitude * tangent.y - sinLatitude * cosLongitude * tangent.z,
                cosLongitude * tangent.x + cosLatitude * sinLongitude * tangent.y - sinLatitude * sinLongitude * tangent.z,
                sinLatitude * tangent.y + cosLatitude * tangent.z);
            GeoPosition position = GeoPosition.FromEarthCentered(
                new GeoPosition(bird.LatitudeDegrees, bird.LongitudeDegrees, bird.AltitudeMeters).ToEarthCentered() + offset);
            _current.Add(id, new GeoPoseReading(id, label, GeoSource.BirdFootKind, new Variant("foot"),
                position.LatitudeDegrees, position.LongitudeDegrees, position.HeightMeters,
                bird.YawDegrees, bird.PitchDegrees, bird.RollDegrees, bird.Id, bird.Kind, bodyOffset,
                sampleTime: bird.SampleTime));
        }

        private GeoPoseReading CreateReading(string id, string label, Kind kind, Variant variant,
            double east, double north, double yaw, double up = 0.0, double roll = 0.0)
        {
            // Only the mock SDK needs a starting road location. Emas has no fixed geographic origin.
            const double startLatitude = 52.520008;
            const double startLongitude = 13.404954;
            const double roadHeight = 40.0;
            const double semiMajorAxis = 6378137.0;
            const double flattening = 1.0 / 298.257223563;
            const double eccentricitySquared = flattening * (2.0 - flattening);
            const double radiansPerDegree = Math.PI / 180.0;
            // Approximate short road offsets with ellipsoid curvature radii; this is mock SDK input, not Emas projection.
            double sinStart = Math.Sin(startLatitude * radiansPerDegree);
            double meridianRadius = semiMajorAxis * (1.0 - eccentricitySquared)
                / Math.Pow(1.0 - eccentricitySquared * sinStart * sinStart, 1.5);
            double latitude = startLatitude + north / (meridianRadius + roadHeight) / radiansPerDegree;
            double sin = Math.Sin(latitude * radiansPerDegree);
            double radius = semiMajorAxis / Math.Sqrt(1.0 - eccentricitySquared * sin * sin);
            double longitude = startLongitude + east / ((radius + roadHeight)
                * Math.Cos(latitude * radiansPerDegree)) / radiansPerDegree;
            double altitude = roadHeight + up;
            return new GeoPoseReading(id, label, kind, variant, latitude, longitude, altitude, yaw, 0.0, roll,
                sampleTime: Timestamp.FromSeconds(_elapsed));
        }
    }
}
