using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies appearance identifiers survive Unity serialization used by assets.
    /// </summary>
    public sealed class VariantTests
    {
        /// <summary>
        /// Unity serialization must retain exact appearance identifiers and represent an omitted value as None.
        /// </summary>
        [Test]
        public void Serialization_RoundTrips()
        {
            Variant value = new Variant("cars.truck");
            Variant copy = JsonUtility.FromJson<Variant>(JsonUtility.ToJson(value));

            Assert.That(copy, Is.EqualTo(value));
            Assert.That(JsonUtility.FromJson<Variant>("{}"), Is.EqualTo(Variant.None));
        }

    }
}
