using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Publishes car ghosts from the first SDK shape.</summary>

    public sealed class SdkOneCarCoordinator : Coordinator
    {
        private readonly SdkOneVehicleFeed _feed;

        /// <summary>Creates a coordinator with the default SDK One feed.</summary>
        public SdkOneCarCoordinator()
            : this(new SdkOneVehicleFeed())
        {
        }

        /// <summary>Creates a coordinator with a supplied SDK One feed.</summary>
        /// <param name="feed">The source feed to poll.</param>
        /// <exception cref="ArgumentNullException">Thrown when the feed is null.</exception>
        public SdkOneCarCoordinator(SdkOneVehicleFeed feed)
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
            foreach (var proxy in _feed.ReadVehicles(elapsedSeconds))
            {
                Publish(proxy);
            }
        }

        private void Publish(SdkOneVehicleProxy proxy)
        {
            var car = GetOrCreate<CarGhost>(
                proxy.Identifier,
                SampleKinds.Car,
                MapVariant(proxy.TypeCode),
                "Car " + proxy.Identifier);
            car.SetPosition(new Vector3(proxy.PositionX, proxy.PositionY, proxy.PositionZ));
            car.SetArticulation(proxy.Steering);
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
