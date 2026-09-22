using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Publishes the same car ghosts from the second SDK shape.
    /// </summary>

    public sealed class SdkTwoCarSource : PresenceSource
    {
        private readonly SdkTwoVehicleFeed _feed;

        /// <summary>
        /// Creates a source with the default SDK Two feed.
        /// </summary>
        public SdkTwoCarSource()
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
        public SdkTwoCarSource(SdkTwoVehicleFeed feed)
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
            foreach (SdkTwoVehicleProxy proxy in _feed.ReadVehicles(elapsedSeconds))
            {
                Publish(proxy);
            }
        }

        private void Publish(SdkTwoVehicleProxy proxy)
        {
            CarGhost car = GetOrCreate<CarGhost>(
                proxy.Id.ToString(),
                SampleKinds.Car,
                MapVariant(proxy.ModelCode),
                "Car " + proxy.Id);
            car.SetPosition(proxy.Coordinates);
            car.SetArticulation(proxy.WheelAngle);
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
