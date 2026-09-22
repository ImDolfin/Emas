using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Publishes an independently moving aircraft population.
    /// </summary>

    public sealed class SimulatedAircraftSource : PresenceSource
    {
        private readonly SimulatedAircraftFeed _feed;

        /// <summary>
        /// Creates a source with the default aircraft feed.
        /// </summary>
        public SimulatedAircraftSource()
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
        public SimulatedAircraftSource(SimulatedAircraftFeed feed)
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
            PublishAll(Time.time);
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            PublishAll(Time.time);
        }

        private void PublishAll(float elapsedSeconds)
        {
            foreach (SimulatedAircraftProxy proxy in _feed.ReadAircraft(elapsedSeconds))
            {
                AircraftGhost ghost = GetOrCreate<AircraftGhost>(
                    proxy.Identifier,
                    SampleKinds.Aircraft,
                    AircraftVariants.Trainer,
                    "Aircraft " + proxy.Identifier);
                ghost.SetPosition(proxy.Position);
            }
        }
    }
}
