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
                // Continue from the distance already travelled so changing speed does not move the past path.
                // Keep poses and velocities in existing packets. Skip earlier times that were not captured.
                _speedSegmentNorth = NorthAt(_elapsed);
                _speedSegmentTime = _elapsed;
                _speedMetersPerSecond = speed;
                _nextCapture = Math.Max(_nextCapture, _elapsed);
            }

            double interval = 1.0 / Mathf.Clamp(packetsPerSecond, 1f, 120f);
            // Start a changed sample rate or transport mode at the current SDK time.
            if (_simulateJitterAndDelay != simulateJitterAndDelay || _captureInterval != interval)
            {
                _pending.Clear();
                _nextCapture = _elapsed;
            }

            // Limit transport and noise settings to valid ranges. Existing samples keep their captured values.
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

            // Advance the mock clock by one microsecond for a control event. This makes the changed pose
            // and zero release velocity newer than the previous packet, even while manual time is paused.
            _elapsed += 0.000001;

            if (!attached)
            {
                Dictionary<string, GeoPoseReading> captured = Capture(_elapsed);
                _releasedFeet.Clear();
                foreach (GeoPoseReading reading in captured.Values)
                {
                    if (reading.Kind == GeoSource.BirdFootKind)
                    {
                        // Release at the current SDK pose while keeping the same IDs and absolute motion values.
                        _releasedFeet.Add(reading.Id, new GeoPoseReading(reading.Id, reading.Label, reading.Kind,
                            reading.Variant, reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters,
                            reading.YawDegrees, reading.PitchDegrees, reading.RollDegrees,
                            eastNorthUpVelocity: default(Double3), sampleTime: reading.SampleTime,
                            bodyAngularVelocity: default(Double3)));
                    }
                }
            }

            _birdFeetAttached = attached;
            // Apply controls immediately, even before the first delayed packet arrives.
            // Discard queued attachment samples so an old packet cannot undo a detach or reattach.
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
            // Keep the last delivered set of objects until a newer complete sample arrives.
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
            // Skip old capture times after a pause or large time jump to avoid a long catch-up loop.
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
            // Packets may arrive out of order. Choose the newest capture time, regardless of list order.
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
                // Replace all delivered samples and object membership together so readers see one complete set.
                _current = newest.Readings;
                _lastDelivered = newest.CaptureTime;
            }

            // Varying delay can reorder packets. Ignore old samples so they cannot restore old objects or motion.
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
            // Sample all objects at the same time before adding them to the delivery queue.
            Dictionary<string, GeoPoseReading> captured = new Dictionary<string, GeoPoseReading>();
            double north = NorthAt(sampleTime);
            Add(captured, sampleTime, "origin", "Driving origin", GeoSource.Kind, new Variant("origin"),
                1.5, north, 0.0, new Double3(0.0, _speedMetersPerSecond, 0.0));

            double orbit = sampleTime * OrbitRadiansPerSecond;
            const double birdRollDegrees = -20.0;
            double rollRadians = birdRollDegrees * Math.PI / 180.0;
            // Turning while banked produces both body pitch and yaw rates. Body rates
            // therefore differ from Euler angle rates, even with zero pitch.
            Double3 birdAngularVelocity = new Double3(0,
                -OrbitRadiansPerSecond * Math.Sin(rollRadians), -OrbitRadiansPerSecond * Math.Cos(rollRadians));
            // Heading increases clockwise. The bird's negative heading rate therefore gives a positive
            // world ENU up-axis rate. Both rates are in radians per second.
            GeoPoseReading bird = CreateReading(sampleTime, "bird", "Circling bird", GeoSource.BirdKind, new Variant("bird"),
                1.5 + 4.0 * Math.Cos(orbit), north + 4.0 * Math.Sin(orbit),
                -orbit * 180.0 / Math.PI, new Double3(-4.0 * OrbitRadiansPerSecond * Math.Sin(orbit),
                    _speedMetersPerSecond + 4.0 * OrbitRadiansPerSecond * Math.Cos(orbit), 0.0), 3.2, birdRollDegrees, birdAngularVelocity,
                eastNorthUpAngularVelocity: new Double3(0, 0, OrbitRadiansPerSecond));
            // Report parts before their parent to show that attachment uses an ID and can wait for the parent.
            AddFoot(captured, "bird-left-foot", "Bird left foot", bird, new Vector3(0.02f, -0.085f, 0.09f));
            AddFoot(captured, "bird-right-foot", "Bird right foot", bird, new Vector3(0.02f, 0.085f, 0.09f));
            captured.Add(bird.Id, bird);

            // Read only nearby road objects. Each parking bay keeps the same ID and pose.
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
                // Keep released feet at their release pose, but give each stationary sample a new timestamp.
                GeoPoseReading released = _releasedFeet[id];
                captured.Add(id, new GeoPoseReading(id, label, GeoSource.BirdFootKind, released.Variant,
                    released.LatitudeDegrees, released.LongitudeDegrees, released.AltitudeMeters,
                    released.YawDegrees, released.PitchDegrees, released.RollDegrees,
                    eastNorthUpVelocity: default(Double3), sampleTime: bird.SampleTime,
                    bodyAngularVelocity: default(Double3)));
                return;
            }

            // Supply matching absolute poses and attachment data so Emas already has the pose needed on release.
            Vector3 local = new Vector3(bodyOffset.y, -bodyOffset.z, bodyOffset.x);
            Quaternion attitude = Quaternion.AngleAxis((float)(bird.YawDegrees % 360.0), Vector3.up)
                * Quaternion.AngleAxis(-(float)(bird.PitchDegrees % 360.0), Vector3.right)
                * Quaternion.AngleAxis(-(float)(bird.RollDegrees % 360.0), Vector3.forward);
            Vector3 tangent = attitude * local; // East, up, north at the bird's WGS84 position.
            double latitude = bird.LatitudeDegrees * Math.PI / 180.0;
            // Convert the local offset into ECEF, then find each foot's latitude, longitude and altitude.
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
            // Give attached parts their parent's velocity. Attachment handling adds motion from the parent's rotation.
            captured.Add(id, new GeoPoseReading(id, label, GeoSource.BirdFootKind, new Variant("foot"),
                position.LatitudeDegrees, position.LongitudeDegrees, position.HeightMeters,
                bird.YawDegrees, bird.PitchDegrees, bird.RollDegrees, bird.Id, bird.Kind, bodyOffset,
                eastNorthUpVelocity: bird.EastNorthUpVelocity, sampleTime: bird.SampleTime,
                bodyAngularVelocity: bird.BodyAngularVelocity, eastNorthUpAngularVelocity: bird.EastNorthUpAngularVelocity));
        }

        private GeoPoseReading CreateReading(double sampleTime, string id, string label, Kind kind, Variant variant,
            double east, double north, double yaw, Double3 velocity, double up = 0.0, double roll = 0.0,
            Double3? bodyAngularVelocity = null, Double3? eastNorthUpAngularVelocity = null)
        {
            if (_simulateJitterAndDelay)
            {
                ApplyObservationNoise(sampleTime, id, ref east, ref north, ref yaw);
            }

            // Use this location to start the mock road. Emas itself has no fixed geographic origin.
            const double startLatitude = 52.520008;
            const double startLongitude = 13.404954;
            const double roadHeight = 40.0;
            const double semiMajorAxis = 6378137.0;
            const double flattening = 1.0 / 298.257223563;
            const double eccentricitySquared = flattening * (2.0 - flattening);
            const double radiansPerDegree = Math.PI / 180.0;
            // Use Earth's curvature to estimate short road offsets in the mock SDK. Emas handles projection separately.
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
                eastNorthUpVelocity: velocity, sampleTime: Timestamp.FromSeconds(sampleTime),
                bodyAngularVelocity: bodyAngularVelocity ?? default(Double3), eastNorthUpAngularVelocity: eastNorthUpAngularVelocity);
        }

        private void ApplyObservationNoise(double sampleTime, string id, ref double east, ref double north, ref double yaw)
        {
            // Use the ID and capture time to repeat the same random values without changing Unity's random state.
            double phase = sampleTime * 61.0;
            for (int index = 0; index < id.Length; index++)
            {
                phase += id[index] * (index + 1);
            }

            // Keep horizontal error within the chosen radius. Place the feet from the same noisy bird pose.
            east += _positionJitter / Math.Sqrt(2.0) * Noise(phase);
            north += _positionJitter / Math.Sqrt(2.0) * Noise(phase + 7.0);
            yaw += _yawJitter * Noise(phase + 19.0);
        }

        private static double Noise(double phase)
        {
            double value = Math.Sin(phase * 12.9898 + 78.233) * 43758.5453;
            return 2.0 * (value - Math.Floor(value)) - 1.0;
        }
    }
}
