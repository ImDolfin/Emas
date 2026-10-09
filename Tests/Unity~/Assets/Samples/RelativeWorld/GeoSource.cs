using System;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Connects the simulated geodetic SDK to the Inspector-configured anchor.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Examples/Geo Source")]
    public sealed class GeoSource : PresenceDetectorComponent
    {
        /// <summary>The entity category configured by this sample.</summary>
        public static readonly Kind Kind = new Kind("relative.car");

        /// <summary>The circling bird's category, with its own blueprint.</summary>
        public static readonly Kind BirdKind = new Kind("relative.bird");

        /// <summary>The bird's independently tracked, attachable feet.</summary>
        public static readonly Kind BirdFootKind = new Kind("relative.bird-foot");

        [Tooltip("Advance the mock SDK using real elapsed time, independently of Unity time scale and frame-duration clamping. Disable for manual Advance calls.")]
        [SerializeField] private bool _automaticAdvance = true;
        [Tooltip("Simulate noisy observations and delayed packets. Disable to compare immediate, clean SDK input.")]
        [SerializeField] private bool _simulateJitterAndDelay = true;
        [Tooltip("Northward travel speed in kilometres per hour. Changing speed preserves the current position; subsequent observations use the new velocity.")]
        [Min(0)]
        [SerializeField] private float _speedKilometersPerHour = 360f;
        [Tooltip("SDK observations captured per simulated second, independently of Realm updates.")]
        [Range(1, 120)]
        [SerializeField] private float _sourcePacketsPerSecond = 60f;
        [Tooltip("Base packet delivery delay in seconds. Observation timestamps remain the original capture times.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _packetDelay = 0.08f;
        [Tooltip("Maximum deterministic variation around the delivery delay, in seconds. Older arriving packets are discarded.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _packetDelayJitter = 0.04f;
        [Tooltip("Maximum horizontal observation error in metres. Noise is applied once per captured packet.")]
        [Range(0, 2)]
        [SerializeField] private float _positionJitter = 2f;
        [Tooltip("Maximum heading observation error in degrees, applied once per captured packet.")]
        [Range(0, 5)]
        [SerializeField] private float _yawJitter = 1f;
        private SimulatedGeoSdk _sdk;
        private bool _appliedAutomaticAdvance;
        private double _lastArrivalTime;

        /// <summary>Gets or sets whether Realm updates advance the mock SDK using real elapsed time.</summary>
        /// <value>True by default; false leaves simulated time under explicit <see cref="Advance"/> calls.</value>
        /// <remarks>
        /// Automatic time is independent of Unity time scale and frame-duration clamping. Changing modes preserves
        /// SDK positions and queued observation timestamps, but rebases this sample's shared Realm clock so a manual
        /// pause or jump cannot leave resumed packets permanently older than the prediction clock.
        /// </remarks>
        public bool AutomaticAdvance
        {
            get => _automaticAdvance;
            set
            {
                _automaticAdvance = value;
                if (_sdk != null && Realm != null)
                {
                    SynchronizeClockMode(Time.realtimeSinceStartupAsDouble);
                    Advance(0.0);
                }
            }
        }

        /// <summary>Enables deterministic observation noise and packet delay; disabling immediately restores clean input.</summary>
        /// <value>True for packet simulation, or false for immediate clean observations; defaults to true.</value>
        /// <remarks>Changing this setting discards pending simulated packets. Before the first attachment, it only updates configuration.</remarks>
        public bool SimulateJitterAndDelay
        {
            get => _simulateJitterAndDelay;
            set
            {
                _simulateJitterAndDelay = value;
                Advance(0.0);
            }
        }

        /// <summary>Gets or sets northward travel speed in kilometres per hour without changing the current position.</summary>
        /// <value>A finite, nonnegative speed; zero stops northward travel while the bird continues its orbit.</value>
        /// <remarks>Defaults to 360 km/h (100 m/s). Subsequent observations carry the new velocity; queued packets retain their captured data.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The speed is negative or non-finite.</exception>
        public float SpeedKilometersPerHour
        {
            get => _speedKilometersPerHour;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The speed must be finite and nonnegative.");
                }
                _speedKilometersPerHour = value;
                Advance(0.0);
            }
        }

        /// <summary>Restarts the sample's shared SDK clock and initializes the configured packet simulation.</summary>
        protected override void OnStart()
        {
            // This sample owns the Realm's shared SDK clock, which restarts when the source starts again.
            Realm.ResetSpatialTime();
            _appliedAutomaticAdvance = _automaticAdvance;
            _lastArrivalTime = Time.realtimeSinceStartupAsDouble;
            _sdk = new SimulatedGeoSdk();
            Advance(0.0);
        }

        /// <summary>
        /// Advances the simulation and delivers due packets; the next realm update reads the latest delivered snapshot.
        /// </summary>
        /// <param name="seconds">Additional simulated time in seconds.</param>
        /// <remarks>
        /// Disable <see cref="AutomaticAdvance"/> for deterministic manual stepping. Does nothing before attachment.
        /// Large jumps discard missed captures instead of replaying an unbounded backlog.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">An attached source receives a negative or non-finite time step.</exception>
        public void Advance(double seconds)
        {
            if (_sdk != null)
            {
                ConfigureSimulation();
                _sdk.Advance(seconds);
                RefreshPresence();
            }
        }

        /// <summary>Changes the SDK attachment state; the next realm update attaches or detaches both bird feet.</summary>
        /// <param name="attached">True to follow the bird; false to hold each foot's current absolute world pose.</param>
        /// <remarks>State changes publish immediately and discard queued attachment state. They consume one microsecond on the simulated SDK clock.</remarks>
        public void SetBirdFeetAttached(bool attached)
        {
            if (_sdk != null)
            {
                ConfigureSimulation();
                _sdk.SetBirdFeetAttached(attached);
                RefreshPresence();
            }
        }

        [ContextMenu("Attach bird feet")]
        private void AttachBirdFeet()
        {
            SetBirdFeetAttached(true);
        }

        [ContextMenu("Detach bird feet")]
        private void DetachBirdFeet()
        {
            SetBirdFeetAttached(false);
        }

        private void RefreshPresence()
        {
            foreach (GeoPoseReading reading in _sdk.Current.Values)
            {
                Detect(reading.Id, reading.Kind, reading.Label, reading.Variant, source: _sdk);
            }
            foreach (Presence presence in OwnedPresences)
            {
                // Trait reads refresh data but do not signal departure; compare identities with the new SDK population.
                if (!_sdk.Current.ContainsKey(presence.Key.EntityId))
                {
                    Disappear(presence.Key.Kind, presence.Key.EntityId);
                }
            }
        }

        private void ConfigureSimulation()
        {
            _sdk.Configure(_simulateJitterAndDelay, _speedKilometersPerHour, _sourcePacketsPerSecond, _packetDelay,
                _packetDelayJitter, _positionJitter, _yawJitter);
        }

        private void SynchronizeClockMode(double arrivalTime)
        {
            if (_appliedAutomaticAdvance != _automaticAdvance)
            {
                _appliedAutomaticAdvance = _automaticAdvance;
                _lastArrivalTime = arrivalTime;
                // Mode changes intentionally change the source clock's rate. Spatial tuning does not reset it.
                Realm.ResetSpatialTime();
            }
        }

        /// <summary>Advances source capture and packet delivery with real elapsed time, or applies configuration without advancing in manual mode.</summary>
        protected override void OnUpdate()
        {
            double arrivalTime = Time.realtimeSinceStartupAsDouble;
            // Serialized Inspector edits bypass property setters, so reconcile the clock mode here as well.
            SynchronizeClockMode(arrivalTime);
            double seconds = _automaticAdvance ? arrivalTime - _lastArrivalTime : 0.0;
            _lastArrivalTime = arrivalTime;
            // deltaTime is scaled and capped after long frames; SDK observations must share the Realm's real-time rate.
            Advance(seconds);
        }

    }
}
