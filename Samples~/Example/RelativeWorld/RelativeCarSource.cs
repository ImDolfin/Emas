using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Publishes nearby cars at large simulation coordinates without converting them to floats.
    /// </summary>
    internal sealed class RelativeCarSource : PresenceSource
    {
        internal static readonly Double3 Origin = new Double3(1000000000.125, 0.0, 1000000000.375);

        protected override void OnStart()
        {
            PublishEgoPosition(0.0);
            PublishEgoRotation(Quaternion.identity);
            PublishTraffic("parked", 3.0, 20.25);
            PublishTraffic("distant", -3.0, 65.0);
        }

        internal void PublishEgoPosition(double distance)
        {
            RelativeCar car = GetOrCreate<RelativeCar>("ego", RelativeCar.Kind, RelativeCar.Ego);
            car.GetComponent<Spatial>().SetPosition(new Double3(Origin.X, Origin.Y, Origin.Z + distance));
        }

        internal void PublishEgoRotation(Quaternion rotation)
        {
            RelativeCar car = GetOrCreate<RelativeCar>("ego", RelativeCar.Kind, RelativeCar.Ego);
            car.GetComponent<Spatial>().SetRotation(rotation);
        }

        private void PublishTraffic(string id, double x, double z)
        {
            RelativeCar car = GetOrCreate<RelativeCar>(id, RelativeCar.Kind, RelativeCar.Traffic);
            car.GetComponent<Spatial>().SetPosition(new Double3(Origin.X + x, Origin.Y, Origin.Z + z));
            car.GetComponent<Spatial>().SetRotation(Quaternion.identity);
        }
    }
}
