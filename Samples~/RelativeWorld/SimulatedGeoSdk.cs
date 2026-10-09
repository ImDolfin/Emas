using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Simulates a car driving north past stationary roadside parking.</summary>
    /// <remarks>Capture and delivery share a simulated clock, but packets retain their original observation times.
    /// Consumers see only complete delivered snapshots; waiting for a packet never removes the previous population.</remarks>
    internal sealed class SimulatedGeoSdk
    {
        private const double ParkingSpacingMeters = 80.0;
        private const double FirstParkingNorthMeters = 24.0;
        private const double OrbitRadiansPerSecond = Math.PI / 4.0;
        private const int MaximumCapturesPerAdvance = 8;
        private double _elapsed;
        private double _speedMetersPerSecond = 100.0;
        private double _speedSegmentTime;
        private double _speedSegmentNorth;
        private Dictionary<string, GeoPoseReading> _current = new Dictionary<string, GeoPoseReading>();
        private readonly Dictionary<string, GeoPoseReading> _releasedFeet = new Dictionary<string, GeoPoseReading>();
        private readonly List<Packet> _pending = new List<Packet>();
        private bool _birdFeetAttached = true;
        private bool _simulateJitterAndDelay;
        private double _captureInterval = 1.0 / 60.0;
        private double _delay;
        private double _delayJitter;
        private double _positionJitter;
        private double _yawJitter;
        private double _nextCapture;
        private double _lastDelivered = double.NegativeInfinity;

        internal IReadOnlyDictionary<string, GeoPoseReading> Current => _current;

        internal void Configure(bool simulateJitterAndDelay, float speedKilometersPerHour, float packetsPerSecond, float delay,
            float delayJitter, float positionJitter, float yawJitter)
        {
            if (float.IsNaN(speedKilometersPerHour) || float.IsInfinity(speedKilometersPerHour) || speedKilometersPerHour < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(speedKilometersPerHour), "The speed must be finite and nonnegative.");
            }
            double speed = speedKilometersPerHour / 3.6;
            if (_speedMetersPerSecond != speed)
            {
                // Continue from the travelled distance, rather than reinterpreting the entire elapsed time.
                // Existing packets already contain their old pose and velocity; skip uncaptured earlier times.
                _speedSegmentNorth = NorthAt(_elapsed);
                _speedSegmentTime = _elapsed;
                _speedMetersPerSecond = speed;
                _nextCapture = Math.Max(_nextCapture, _elapsed);
            }

            double interval = 1.0 / Mathf.Clamp(packetsPerSecond, 1f, 120f);
            if (_simulateJitterAndDelay != simulateJitterAndDelay || _captureInterval != interval)
            {
                _pending.Clear();
                _nextCapture = _elapsed;
            }

            _simulateJitterAndDelay = simulateJitterAndDelay;
            _captureInterval = interval;
            _delay = Mathf.Clamp(delay, 0f, 0.5f);
            _delayJitter = Mathf.Clamp(delayJitter, 0f, 0.5f);
            _positionJitter = Mathf.Clamp(positionJitter, 0f, 2f);
            _yawJitter = Mathf.Clamp(yawJitter, 0f, 5f);
        }

        internal void SetBirdFeetAttached(bool attached)
        {
            if (_birdFeetAttached == attached)
            {
                return;
            }

            // A control event consumes one microsecond on the mock SDK clock. This keeps its changed pose
            // and zero release velocity newer than the previous packet, even while manual advancement is paused.
            _elapsed += 0.000001;

            if (!attached)
            {
                Dictionary<string, GeoPoseReading> captured = Capture(_elapsed);
                _releasedFeet.Clear();
                foreach (GeoPoseReading reading in captured.Values)
                {
                    if (reading.Kind == GeoSource.BirdFootKind)
                    {
                        // Release from the current SDK pose, preserving the same identities and absolute channels.
                        _releasedFeet.Add(reading.Id, new GeoPoseReading(reading.Id, reading.Label, reading.Kind,
                            reading.Variant, reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters,
                            reading.YawDegrees, reading.PitchDegrees, reading.RollDegrees,
                            eastNorthUpVelocity: default(Double3), sampleTime: reading.SampleTime));
                    }
                }
            }

            _birdFeetAttached = attached;
            // Explicit controls take effect immediately, including before the first delayed packet arrives.
            // Discard old queued attachment state so it cannot undo a detach or reattach.
            _pending.Clear();
            _current = Capture(_elapsed);
            _lastDelivered = _elapsed;
            _nextCapture = _elapsed + _captureInterval;
        }

        internal void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            _elapsed += seconds;
            if (!_simulateJitterAndDelay)
            {
                _current = Capture(_elapsed);
                _lastDelivered = _elapsed;
                return;
            }

            QueueDueCaptures();
            DeliverDuePackets();
        }

        private void QueueDueCaptures()
        {
            // Drop skipped captures after a pause or a large manual time jump; never replay an unbounded backlog.
            double due = Math.Floor((_elapsed - _nextCapture) / _captureInterval) + 1.0;
            if (due > MaximumCapturesPerAdvance)
            {
                _nextCapture += (due - MaximumCapturesPerAdvance) * _captureInterval;
            }

            for (int index = 0; index < MaximumCapturesPerAdvance && _nextCapture <= _elapsed; index++)
            {
                double captureTime = _nextCapture;
                double delay = Math.Max(0.0, _delay + _delayJitter * Noise(captureTime * 61.0 + 11.0));
                _pending.Add(new Packet(captureTime, captureTime + delay, Capture(captureTime)));
                _nextCapture += _captureInterval;
            }
        }

        private void DeliverDuePackets()
        {
            Packet newest = null;
            for (int index = _pending.Count - 1; index >= 0; index--)
            {
                Packet packet = _pending[index];
                if (packet.DeliveryTime <= _elapsed)
                {
                    if (packet.CaptureTime > _lastDelivered && (newest == null || packet.CaptureTime > newest.CaptureTime))
                    {
                        newest = packet;
                    }
                    _pending.RemoveAt(index);
                }
            }

            if (newest != null)
            {
                // Publish a complete immutable observation set atomically, including its membership.
                _current = newest.Readings;
                _lastDelivered = newest.CaptureTime;
            }

            // Delay jitter can reorder arrivals. Older queued snapshots must not restore stale membership or motion.
            for (int index = _pending.Count - 1; index >= 0; index--)
            {
                if (_pending[index].CaptureTime <= _lastDelivered)
                {
                    _pending.RemoveAt(index);
                }
            }
        }

        private Dictionary<string, GeoPoseReading> Capture(double sampleTime)
        {
            Dictionary<string, GeoPoseReading> captured = new Dictionary<string, GeoPoseReading>();
            double north = NorthAt(sampleTime);
            Add(captured, sampleTime, "origin", "Driving origin", GeoSource.Kind, new Variant("origin"),
                1.5, north, 0.0, new Double3(0.0, _speedMetersPerSecond, 0.0));

            double orbit = sampleTime * OrbitRadiansPerSecond;
            GeoPoseReading bird = CreateReading(sampleTime, "bird", "Circling bird", GeoSource.BirdKind, new Variant("bird"),
                1.5 + 4.0 * Math.Cos(orbit), north + 4.0 * Math.Sin(orbit),
                -orbit * 180.0 / Math.PI, new Double3(-4.0 * OrbitRadiansPerSecond * Math.Sin(orbit),
                    _speedMetersPerSecond + 4.0 * OrbitRadiansPerSecond * Math.Cos(orbit), 0.0), 3.2, -20.0);
            // Parts are reported before their parent to demonstrate attachment by identity rather than a live root.
            AddFoot(captured, "bird-left-foot", "Bird left foot", bird, new Vector3(0.02f, -0.085f, 0.09f));
            AddFoot(captured, "bird-right-foot", "Bird right foot", bird, new Vector3(0.02f, 0.085f, 0.09f));
            captured.Add(bird.Id, bird);

            // Read only the nearby stretch of road; each parking bay has a stable identity and pose.
            long first = Math.Max(0L, (long)Math.Ceiling((north - 22.0 - FirstParkingNorthMeters) / ParkingSpacingMeters));
            long last = (long)Math.Floor((north + 38.0 - FirstParkingNorthMeters) / ParkingSpacingMeters);
            for (long index = first; index <= last; index++)
            {
                bool left = index % 2 == 0;
                Add(captured, sampleTime, "parked-" + index.ToString(CultureInfo.InvariantCulture), left ? "Parked left" : "Parked right",
                    GeoSource.Kind, new Variant("target"), left ? -4.5 : 4.5,
                    FirstParkingNorthMeters + index * ParkingSpacingMeters, left ? 180.0 : 0.0, default(Double3));
            }

            return captured;
        }

        private double NorthAt(double sampleTime)
        {
            return _speedSegmentNorth + (sampleTime - _speedSegmentTime) * _speedMetersPerSecond;
        }

        private void Add(Dictionary<string, GeoPoseReading> captured, double sampleTime, string id, string label,
            Kind kind, Variant variant, double east, double north, double yaw, Double3 velocity)
        {
            captured.Add(id, CreateReading(sampleTime, id, label, kind, variant, east, north, yaw, velocity));
        }

        private void AddFoot(Dictionary<string, GeoPoseReading> captured, string id, string label,
            GeoPoseReading bird, Vector3 bodyOffset)
        {
            if (!_birdFeetAttached)
            {
                GeoPoseReading released = _releasedFeet[id];
                captured.Add(id, new GeoPoseReading(id, label, GeoSource.BirdFootKind, released.Variant,
                    released.LatitudeDegrees, released.LongitudeDegrees, released.AltitudeMeters,
                    released.YawDegrees, released.PitchDegrees, released.RollDegrees,
                    eastNorthUpVelocity: default(Double3), sampleTime: bird.SampleTime));
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
            // Attached parts inherit the parent's translation; attachment projection supplies rotational motion.
            captured.Add(id, new GeoPoseReading(id, label, GeoSource.BirdFootKind, new Variant("foot"),
                position.LatitudeDegrees, position.LongitudeDegrees, position.HeightMeters,
                bird.YawDegrees, bird.PitchDegrees, bird.RollDegrees, bird.Id, bird.Kind, bodyOffset,
                eastNorthUpVelocity: bird.EastNorthUpVelocity, sampleTime: bird.SampleTime));
        }

        private GeoPoseReading CreateReading(double sampleTime, string id, string label, Kind kind, Variant variant,
            double east, double north, double yaw, Double3 velocity, double up = 0.0, double roll = 0.0)
        {
            if (_simulateJitterAndDelay)
            {
                ApplyObservationNoise(sampleTime, id, ref east, ref north, ref yaw);
            }

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
                eastNorthUpVelocity: velocity, sampleTime: Timestamp.FromSeconds(sampleTime));
        }

        private void ApplyObservationNoise(double sampleTime, string id, ref double east, ref double north, ref double yaw)
        {
            // Identity and capture time make repeated runs reproducible without touching Unity's global random state.
            double phase = sampleTime * 61.0;
            for (int index = 0; index < id.Length; index++)
            {
                phase += id[index] * (index + 1);
            }

            // Horizontal error stays inside the configured radius. Feet derive from the same noisy bird pose.
            east += _positionJitter / Math.Sqrt(2.0) * Noise(phase);
            north += _positionJitter / Math.Sqrt(2.0) * Noise(phase + 7.0);
            yaw += _yawJitter * Noise(phase + 19.0);
        }

        private static double Noise(double phase)
        {
            double value = Math.Sin(phase * 12.9898 + 78.233) * 43758.5453;
            return 2.0 * (value - Math.Floor(value)) - 1.0;
        }

        private sealed class Packet
        {
            internal readonly double CaptureTime;
            internal readonly double DeliveryTime;
            internal readonly Dictionary<string, GeoPoseReading> Readings;

            internal Packet(double captureTime, double deliveryTime, Dictionary<string, GeoPoseReading> readings)
            {
                CaptureTime = captureTime;
                DeliveryTime = deliveryTime;
                Readings = readings;
            }
        }
    }
}
