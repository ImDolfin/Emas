using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Detects the same permanent car population in SDK Two.
    /// </summary>

    public sealed class SdkTwoCarDetector : PresenceDetector
    {
        private readonly SdkTwoVehicleFeed _feed;

        /// <summary>
        /// Creates a source with the default SDK Two feed.
        /// </summary>
        public SdkTwoCarDetector()
            : this(new SdkTwoVehicleFeed())
        {
        }

        /// <summary>
        /// Creates a source with a supplied SDK Two feed.
        /// </summary>
        /// <param name="feed">
        /// The source feed to poll.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the feed is null.
        /// </exception>
        public SdkTwoCarDetector(SdkTwoVehicleFeed feed)
        {
            if (feed == null)
            {
                throw new ArgumentNullException(nameof(feed));
            }

            _feed = feed;
        }

        /// <summary>Publishes the second SDK's car identities so compatible existing Ghosts can be adopted.</summary>
        protected override void OnStart()
        {
            DetectAll(Time.time);
        }

        private void DetectAll(float elapsedSeconds)
        {
            foreach (SdkTwoVehicleProxy proxy in _feed.ReadVehicles(elapsedSeconds))
            {
                DetectProxy(proxy);
            }
        }

        private void DetectProxy(SdkTwoVehicleProxy proxy)
        {
            Detect(proxy.Id.ToString(), SampleKinds.Car, "Car " + proxy.Id, MapVariant(proxy.ModelCode), source: _feed);
        }

        private static Variant MapVariant(int modelCode)
        {
            switch (modelCode)
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
