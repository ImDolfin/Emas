using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Connects aircraft discovery and Ghost module inputs to the sample SDK.</summary>
    [RequireComponent(typeof(AnchorSetup))]
    public sealed class AircraftSource : PresenceDetectorComponent
    {
        private readonly SimulatedAircraftFeed _feed = new SimulatedAircraftFeed();

        /// <inheritdoc />
        protected override void OnStart()
        {
            foreach (SimulatedAircraftProxy proxy in _feed.ReadAircraft(Time.time))
            {
                Detect(proxy.Identifier, SampleKinds.Aircraft, "Aircraft " + proxy.Identifier,
                    AircraftVariants.Trainer, source: _feed);
            }
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            _feed.ReadAircraft(Time.time);
        }
    }
}
