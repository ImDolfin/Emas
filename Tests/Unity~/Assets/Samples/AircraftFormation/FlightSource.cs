using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.AircraftFormation
{
    /// <summary>Simulates three equal-speed aircraft with independent timestamped observation streams on Unity's main thread.</summary>
    [AddComponentMenu("Emas/Examples/Flight Source")]
    public sealed class FlightSource : PresenceDetectorComponent
    {
        private sealed class Packet
        {
            internal int Aircraft;
            internal double Arrival;
            internal FlightReading Reading;
        }

        /// <summary>The aircraft category mapped by this sample's blueprint.</summary>
        public static readonly Kind Kind = new Kind("flight.aircraft");

        [Header("Simulation")]
        [Tooltip("Advance the simulated SDK clock in real time. Disable when stepping the source through code.")]
        [SerializeField] private bool _automaticAdvance = true;
        [Tooltip("Apply the configured latency and measurement noise. Disable to compare clean, immediate observations.")]
        [SerializeField] private bool _simulateJitterAndDelay = true;
        [Tooltip("Independent reference, left and right capture rates in Hz. The streams share an SDK clock, but not capture times.")]
        [SerializeField] private Vector3 _updatesPerSecond = new Vector3(60, 47, 53);
        [Header("Transport")]
        [Tooltip("Base delivery latency in seconds. Each wingman adds 15 ms to make transport differences visible.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _packetDelay = 0.08f;
        [Tooltip("Independent uniform delivery variation in seconds; delivery can reorder packets.")]
        [Range(0, 0.2f)]
        [SerializeField] private float _delayJitter = 0.05f;
        [Header("Measurement noise")]
        [Tooltip("Maximum three-dimensional position error per observation, in metres.")]
        [Range(0, 5)]
        [SerializeField] private float _positionJitter = 2;
        [Tooltip("Maximum independent pitch, yaw and roll measurement error per axis, in degrees.")]
        [Range(0, 2)]
        [SerializeField] private float _attitudeJitter = 0.35f;
        private readonly List<Packet> _pending = new List<Packet>();
        private readonly FlightReading[] _latest = new FlightReading[3];
        private readonly bool[] _hasReading = new bool[3];
        private readonly double[] _nextCapture = new double[3];
        private readonly System.Random[] _random = new System.Random[3];
        private Double3 _position;
        private double _motionTime;
        private double _elapsed;
        private double _lastRealtime;
        private bool _appliedAutomatic;
        private bool _appliedImpairments;

        /// <summary>Gets or sets real-time SDK advancement; disable for deterministic manual stepping.</summary>
        /// <value>True by default. Switching modes preserves travel and rebases the sample-owned Realm clock on its next update.</value>
        public bool AutomaticAdvance
        {
            get => _automaticAdvance;
            set => _automaticAdvance = value;
        }

        /// <summary>Gets or sets independent capture timing, measurement noise and delayed delivery.</summary>
        /// <value>True by default; false supplies simultaneous, clean observations immediately. Changes discard queued packets on the next advance.</value>
        public bool SimulateJitterAndDelay
        {
            get => _simulateJitterAndDelay;
            set => _simulateJitterAndDelay = value;
        }

        /// <summary>Gets elapsed SDK time since this detector started.</summary>
        /// <value>Simulation seconds, unaffected by Unity time scale.</value>
        public double ElapsedSeconds => _elapsed;

        /// <summary>Advances flight and delivers due observations; call only on Unity's main thread after disabling automatic advancement.</summary>
        /// <param name="seconds">Finite, nonnegative SDK seconds to advance. Before attachment, validates input and otherwise does nothing.</param>
        /// <remarks>Missed captures beyond the most recent quarter-second are skipped. Integration work is bounded for large jumps.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The duration is negative, non-finite, or overflows the SDK clock.</exception>
        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 1e9 - _elapsed)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            if (Realm == null)
            {
                return;
            }

            // Apply transport setting changes before taking more samples so queued packets keep the correct settings.
            _elapsed += seconds;
            if (_appliedImpairments != _simulateJitterAndDelay)
            {
                _appliedImpairments = _simulateJitterAndDelay;
                _pending.Clear();
                for (int index = 0; index < 3; index++)
                {
                    _nextCapture[index] = _elapsed + index * 0.007;
                }
            }

            // Without network errors, sample all aircraft together. Otherwise, schedule each capture and arrival separately.
            if (!_simulateJitterAndDelay)
            {
                IntegrateTo(_elapsed);
                for (int index = 0; index < 3; index++)
                {
                    _latest[index] = Capture(index, _elapsed, false);
                    _hasReading[index] = true;
                }
            }
            else
            {
                CapturePending();
                DeliverPending();
            }

            // Report an aircraft only after a complete sample has arrived for its readers.
            for (int index = 0; index < 3; index++)
            {
                if (_hasReading[index])
                {
                    string id = index == 0 ? "reference" : index == 1 ? "left" : "right";
                    Detect(id, Kind, variant: new Variant(index == 0 ? "reference" : id), source: this);
                }
            }
        }

        internal FlightReading Read(string id)
        {
            return _latest[id == "reference" ? 0 : id == "left" ? 1 : 2];
        }

        /// <summary>Restarts the deterministic flight, independent stream seeds and sample-owned SDK clock.</summary>
        protected override void OnStart()
        {
            // On restart, reset time, queued packets and random numbers together.
            Realm.ResetSpatialTime();
            _pending.Clear();
            _position = new Double3(10000000, 20000000, 30000000);
            _motionTime = 0;
            _elapsed = 0;
            _appliedAutomatic = _automaticAdvance;
            _appliedImpairments = _simulateJitterAndDelay;
            _lastRealtime = Time.realtimeSinceStartupAsDouble;
            for (int index = 0; index < 3; index++)
            {
                _random[index] = new System.Random(173 + index * 101);
                _nextCapture[index] = index * 0.007;
                _hasReading[index] = false;
            }
            Advance(0);
        }

        /// <summary>Advances using unscaled, unclamped elapsed time and handles clock mode changes.</summary>
        protected override void OnUpdate()
        {
            // Reset the clock's starting point when switching between manual steps and real time.
            double realtime = Time.realtimeSinceStartupAsDouble;
            if (_appliedAutomatic != _automaticAdvance)
            {
                _appliedAutomatic = _automaticAdvance;
                _lastRealtime = realtime;
                Realm.ResetSpatialTime();
            }
            double seconds = _automaticAdvance ? realtime - _lastRealtime : 0;
            _lastRealtime = realtime;
            Advance(seconds);
        }

        private void CapturePending()
        {
            // Limit catch-up work after a slow frame, then process the three capture schedules in time order.
            for (int index = 0; index < 3; index++)
            {
                _nextCapture[index] = Math.Max(_nextCapture[index], _elapsed - 0.25);
            }
            while (true)
            {
                // Advance the flight to the next capture time so all packets follow the same flight path.
                int index = _nextCapture[0] <= _nextCapture[1] ? 0 : 1;
                index = _nextCapture[index] <= _nextCapture[2] ? index : 2;
                double time = _nextCapture[index];
                if (time > _elapsed)
                {
                    break;
                }
                IntegrateTo(time);
                FlightReading reading = Capture(index, time, true);
                double delay = Math.Max(0, _packetDelay + index * 0.015 + Noise(index) * _delayJitter);
                _pending.Add(new Packet { Aircraft = index, Arrival = time + delay, Reading = reading });
                _nextCapture[index] += 1.0 / Mathf.Clamp(_updatesPerSecond[index], 1, 120);
            }
        }

        private FlightReading Capture(int index, double time, bool noisy)
        {
            // Add error to this captured sample. Leave the SDK's actual motion values unchanged.
            Double3 position = _position + FlightMotion.Offset(index);
            Quaternion rotation = FlightMotion.Rotation(time);
            if (noisy)
            {
                Vector3 error = new Vector3(Noise(index), Noise(index), Noise(index));
                error = Vector3.ClampMagnitude(error, 1) * _positionJitter;
                position += new Double3(error.x, error.y, error.z);
                rotation *= Quaternion.Euler(Noise(index) * _attitudeJitter,
                    Noise(index) * _attitudeJitter, Noise(index) * _attitudeJitter);
            }
            return new FlightReading(position, rotation, FlightMotion.Velocity(time), FlightMotion.AngularVelocity(time), time);
        }

        private void DeliverPending()
        {
            // For each aircraft, deliver the newest sample that has arrived. Ignore older packets that arrive late.
            for (int index = _pending.Count - 1; index >= 0; index--)
            {
                Packet packet = _pending[index];
                if (packet.Arrival > _elapsed)
                {
                    continue;
                }
                int aircraft = packet.Aircraft;
                if (!_hasReading[aircraft] || packet.Reading.SampleTime.CompareTo(_latest[aircraft].SampleTime) > 0)
                {
                    _latest[aircraft] = packet.Reading;
                    _hasReading[aircraft] = true;
                }
                _pending.RemoveAt(index);
            }
        }

        private void IntegrateTo(double time)
        {
            // Use the heading at each step's midpoint to follow turns. Limit the number of steps for large time jumps.
            double duration = time - _motionTime;
            int steps = (int)Math.Min(4096, Math.Max(1, Math.Ceiling(duration * 240)));
            double step = duration / steps;
            for (int index = 0; index < steps; index++)
            {
                _position += FlightMotion.Velocity(_motionTime + (index + 0.5) * step) * step;
            }
            _motionTime = time;
        }

        private float Noise(int index)
        {
            return (float)(_random[index].NextDouble() * 2 - 1);
        }
    }
}
