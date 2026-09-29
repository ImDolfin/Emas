using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Detects the permanent car population in SDK One.
    /// </summary>

    public sealed class SdkOneCarDetector : PresenceDetector
    {
        private readonly SdkOneVehicleFeed _feed;

        /// <summary>
        /// Creates a source with the default SDK One feed.
        /// </summary>
        public SdkOneCarDetector()
            : this(new SdkOneVehicleFeed())
        {
        }

        /// <summary>
        /// Creates a source with a supplied SDK One feed.
        /// </summary>
        /// <param name="feed">
        /// The source feed to poll.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the feed is null.
        /// </exception>
        public SdkOneCarDetector(SdkOneVehicleFeed feed)
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
            foreach (SdkOneVehicleProxy proxy in _feed.ReadVehicles(elapsedSeconds))
            {
                DetectProxy(proxy);
            }
        }

        private void DetectProxy(SdkOneVehicleProxy proxy)
        {
            Detect(proxy.Identifier, SampleKinds.Car, "Car " + proxy.Identifier, MapVariant(proxy.TypeCode), source: _feed);
        }

        private static Variant MapVariant(int typeCode)
        {
            switch (typeCode)
            {
                case 0:
                    return CarVariants.SmallCar;
                case 1:
                    return CarVariants.LargeCar;
                default:
                    return CarVariants.Truck;
            }
        }
    }
}
