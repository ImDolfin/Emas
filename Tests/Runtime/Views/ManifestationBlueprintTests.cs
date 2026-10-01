using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies named appearance selection and atomic blueprint configuration.
    /// </summary>
    public sealed class ManifestationBlueprintTests
    {
        /// <summary>
        /// Exact names select independent appearances, including LOD variants; unknown or unspecified names use the fallback.
        /// </summary>
        [Test]
        public void ResolveViewPrefab_UsesExactNameAndFallback()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            GameObject full = new GameObject("car");
            GameObject low = new GameObject("car low");
            GameObject fallback = new GameObject("fallback");
            try
            {
                ManifestationVariant car = new ManifestationVariant("small_car", full);
                ManifestationVariant carLow = new ManifestationVariant("small_car_low", low);
                blueprint.Configure(new Kind("cars"), null, new[] { car, carLow }, fallback);

                Assert.That(blueprint.ResolveViewPrefab(car.Variant), Is.SameAs(car.Prefab));
                Assert.That(blueprint.ResolveViewPrefab(new Variant(carLow.Name)), Is.SameAs(low));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("SMALL_CAR")), Is.SameAs(fallback));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("unknown")), Is.SameAs(fallback));
                Assert.That(blueprint.ResolveViewPrefab(Variant.None), Is.SameAs(fallback));

                blueprint.Configure(new Kind("cars"), null, new[] { car }, null);
                Assert.That(blueprint.ResolveViewPrefab(carLow.Variant), Is.Null);
                Assert.That(blueprint.ResolveViewPrefab(Variant.None), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(full);
                UnityEngine.Object.DestroyImmediate(low);
                UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        /// <summary>
        /// Invalid names and missing prefabs are rejected when constructing a named appearance in code.
        /// </summary>
        [Test]
        public void ManifestationVariant_RequiresNameAndPrefab()
        {
            GameObject prefab = new GameObject("view");
            try
            {
                foreach (string name in new[] { null, "", "  " })
                {
                    Assert.Throws<ArgumentException>(() => new ManifestationVariant(name, prefab));
                }

                Assert.Throws<ArgumentNullException>(() => new ManifestationVariant("car", null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// Duplicate names and unconfigured rows leave the blueprint's previous mapping usable.
        /// </summary>
        [Test]
        public void Configure_RejectsInvalidRowsAtomically()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            GameObject original = new GameObject("original");
            GameObject replacement = new GameObject("replacement");
            try
            {
                ManifestationVariant first = new ManifestationVariant("car", original);
                ManifestationVariant duplicate = new ManifestationVariant("car", replacement);
                Kind kind = new Kind("cars");
                blueprint.Configure(kind, null, new[] { first }, null);
                Assert.That(Assert.Throws<ArgumentException>(() => blueprint.Configure(kind, null,
                    new[] { first, duplicate }, null)).Message, Does.Contain("duplicate").And.Contain("index 0"));
                Assert.That(Assert.Throws<ArgumentException>(() => blueprint.Configure(kind, null,
                    new[] { default(ManifestationVariant) }, null)).Message, Does.Contain("name"));
                Assert.That(blueprint.ResolveViewPrefab(first.Variant), Is.SameAs(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }

        /// <summary>
        /// Mutating a caller-owned row collection does not silently reconfigure a blueprint.
        /// </summary>
        [Test]
        public void Configure_CopiesRows()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            GameObject original = new GameObject("original");
            GameObject replacement = new GameObject("replacement");
            try
            {
                ManifestationVariant[] rows = { new ManifestationVariant("car", original) };
                blueprint.Configure(new Kind("cars"), null, rows, null);
                rows[0] = new ManifestationVariant("car", replacement);
                Assert.That(blueprint.ResolveViewPrefab(new Variant("car")), Is.SameAs(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }
    }
}
