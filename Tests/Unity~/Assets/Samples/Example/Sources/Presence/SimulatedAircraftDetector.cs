using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Detects the permanent aircraft population.
    /// </summary>

    public sealed class SimulatedAircraftDetector : PresenceDetector
    {
        private readonly SimulatedAircraftFeed _feed;

        /// <summary>
        /// Creates a source with the default aircraft feed.
        /// </summary>
        public SimulatedAircraftDetector()
            : this(new SimulatedAircraftFeed())
        {
        }

        /// <summary>
        /// Creates a source with a supplied aircraft feed.
        /// </summary>
        /// <param name="feed">
        /// The source feed to poll.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the feed is null.
        /// </exception>
        public SimulatedAircraftDetector(SimulatedAircraftFeed feed)
        {
            if (feed == null)
            {
                throw new ArgumentNullException(nameof(feed));
            }

            _feed = feed;
        }

        /// <inheritdoc />
        protected override void OnStart()
        {
            DetectAll(Time.time);
        }

        private void DetectAll(float elapsedSeconds)
        {
            foreach (SimulatedAircraftProxy proxy in _feed.ReadAircraft(elapsedSeconds))
            {
                Detect(proxy.Identifier, SampleKinds.Aircraft, "Aircraft " + proxy.Identifier, AircraftVariants.Trainer, source: _feed);
            }
        }
    }
}
