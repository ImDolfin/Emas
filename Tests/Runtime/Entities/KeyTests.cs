using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>Verifies that an entity identity includes its kind as well as its source ID.</summary>
    public sealed class KeyTests
    {
        /// <summary>Two kinds can reuse a source ID in the same anchor without becoming one entity.</summary>
        [Test]
        public void Equality_DistinguishesKindsWithinAnAnchor()
        {
            var car = new Key("anchor", new Kind("cars"), "one");
            var sameCar = new Key("anchor", new Kind("cars"), "one");
            var aircraft = new Key("anchor", new Kind("aircraft"), "one");

            Assert.That(car, Is.EqualTo(sameCar));
            Assert.That(car.GetHashCode(), Is.EqualTo(sameCar.GetHashCode()));
            Assert.That(car, Is.Not.EqualTo(aircraft));
            Assert.That(car == aircraft, Is.False);
        }
    }
}
