using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies extensible appearance values and their query and blueprint integration.
    /// </summary>
    public sealed class VariantTests
    {
        /// <summary>
        /// Ensures independent declarations compare by exact identifier.
        /// </summary>
        [Test]
        public void Equality_IsOrdinalAndWorksInCollections()
        {
            Variant first = new Variant("cars.small");
            Variant same = new Variant("cars.small");
            Variant different = new Variant("Cars.Small");

            Assert.That(first == same, Is.True);
            Assert.That(first != different, Is.True);
            Assert.That(first.Equals((object)same), Is.True);
            Assert.That(first.Equals("cars.small"), Is.False);
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first.Id, Is.EqualTo("cars.small"));
            Assert.That(first.ToString(), Is.EqualTo(first.Id));
            Assert.That(first.IsNone, Is.False);
        }

        /// <summary>
        /// Ensures absent appearances have a consistent default value.
        /// </summary>
        [Test]
        public void Default_IsUnspecified()
        {
            Variant value = default(Variant);

            Assert.That(value, Is.EqualTo(Variant.None));
            Assert.That(value.Id, Is.Empty);
            Assert.That(value.IsNone, Is.True);
            Assert.That(value.GetHashCode(), Is.EqualTo(Variant.None.GetHashCode()));
        }

        /// <summary>
        /// Rejects accidental empty declarations while allowing an explicit None value.
        /// </summary>
        [Test]
        public void Constructor_RejectsMissingIds()
        {
            Assert.Throws<ArgumentException>(() => new Variant(null));
            Assert.Throws<ArgumentException>(() => new Variant(""));
            Assert.Throws<ArgumentException>(() => new Variant(" "));
        }

        /// <summary>
        /// Ensures serialization preserves exact identifiers and the default.
        /// </summary>
        [Test]
        public void Serialization_RoundTrips()
        {
            Variant value = new Variant("cars.truck");
            Variant copy = JsonUtility.FromJson<Variant>(JsonUtility.ToJson(value));

            Assert.That(copy, Is.EqualTo(value));
            Assert.That(JsonUtility.FromJson<Variant>("{}"), Is.EqualTo(Variant.None));
        }

        /// <summary>
        /// Ensures appearance filters are typed, exact and independent of query copies.
        /// </summary>
        [Test]
        public void Query_MatchesTypedAppearanceAndNone()
        {
            using (Realm realm = new Realm())
            {
                Query original = realm.Query();
                Query selected = original.WithVariant(new Variant("cars.small"));
                FakeGhost small = new FakeGhost(new Variant("cars.small"));
                FakeGhost large = new FakeGhost(new Variant("cars.large"));

                Assert.That(original.Matches(large), Is.True);
                Assert.That(selected.Matches(small), Is.True);
                Assert.That(selected.Matches(large), Is.False);
                Assert.That(selected.Matches(new FakeGhost(new Variant("Cars.Small"))), Is.False);
                Assert.That(original.WithVariant(Variant.None).Matches(new FakeGhost(Variant.None)), Is.True);
            }
        }

        /// <summary>
        /// Ensures blueprint selection uses typed appearances for exact, lower and fallback views.
        /// </summary>
        [Test]
        public void Blueprint_SelectsTypedVariants()
        {
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            GameObject full = new GameObject("full");
            GameObject minimal = new GameObject("minimal");
            GameObject fallback = new GameObject("fallback");
            try
            {
                Variant variant = new Variant("cars.small");
                blueprint.Configure(new Kind("cars"), null, new[]
                {
                    new Blueprint.ViewMapping(variant, DetailLevel.Full, full),
                    new Blueprint.ViewMapping(variant, DetailLevel.Minimal, minimal)
                }, fallback);

                Assert.That(blueprint.ResolveViewPrefab(variant, DetailLevel.Full), Is.SameAs(full));
                Assert.That(blueprint.ResolveViewPrefab(variant, DetailLevel.Reduced), Is.SameAs(minimal));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("cars.large"), DetailLevel.Full), Is.SameAs(fallback));
                Assert.That(blueprint.ResolveViewPrefab(Variant.None, DetailLevel.Full), Is.SameAs(fallback));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(full);
                UnityEngine.Object.DestroyImmediate(minimal);
                UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        /// <summary>
        /// Appearance APIs require Variant values without implicit kind or string conversions.
        /// </summary>
        [Test]
        public void Api_RequiresTypedVariants()
        {
            Assert.That(typeof(IGhost).GetProperty(nameof(IGhost.Variant)).PropertyType, Is.EqualTo(typeof(Variant)));
            Assert.That(typeof(Query).GetMethod(nameof(Query.WithVariant)).GetParameters()[0].ParameterType,
                Is.EqualTo(typeof(Variant)));
            Assert.That(typeof(Blueprint).GetMethod(nameof(Blueprint.ResolveViewPrefab)).GetParameters()[0].ParameterType,
                Is.EqualTo(typeof(Variant)));
            Assert.That(typeof(Realm).GetMethod(nameof(Realm.Prepare)).GetParameters()[3].ParameterType,
                Is.EqualTo(typeof(Variant?)));
            Assert.That(typeof(PresenceSource).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(method => method.Name == "GetOrCreate" && method.GetParameters().Length == 3
                    && method.GetParameters()[2].ParameterType == typeof(Variant?)), Is.True);
            foreach (Type type in new[] { typeof(Variant), typeof(Kind) })
            {
                Assert.That(type.GetMethods().Any(method => method.Name == "op_Implicit"), Is.False,
                    type.Name + " must require explicit construction.");
            }
        }

        private sealed class FakeGhost : IGhost
        {
            internal FakeGhost(Variant variant)
            {
                Variant = variant;
            }

            /// <inheritdoc />
            public Key Key
            {
                get
                {
                    return new Key("test", new Kind("cars"), "1");
                }
            }

            /// <inheritdoc />
            public string Name
            {
                get
                {
                    return "Car";
                }
            }

            /// <inheritdoc />
            public Variant Variant
            {
                get;
            }

            /// <inheritdoc />
            public bool IsAvailable
            {
                get
                {
                    return true;
                }
            }

            /// <inheritdoc />
            public bool TryGet<T>(out T part) where T : class
            {
                part = null;
                return false;
            }
        }
    }
}
