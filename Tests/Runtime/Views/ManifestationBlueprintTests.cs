using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies manifestation selection and atomic asset configuration.
    /// </summary>
    public sealed class ManifestationBlueprintTests
    {
        /// <summary>
        /// None resolves to no manifestation even when the blueprint provides a fallback.
        /// </summary>
        [Test]
        public void ResolveViewPrefab_NoneNeverSelectsFallback()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant variant = ScriptableObject.CreateInstance<ManifestationVariant>();
            GameObject mapped = new GameObject("mapped view");
            GameObject fallback = new GameObject("fallback view");
            try
            {
                variant.Configure(Variant.None, new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Minimal, mapped)
                });
                blueprint.Configure(new Kind("views.none"), null, new[] { variant }, fallback);
                Assert.That(blueprint.ResolveViewPrefab(Variant.None, DetailLevel.None), Is.Null);
                Assert.That(blueprint.ResolveViewPrefab(Variant.None, DetailLevel.Minimal), Is.SameAs(mapped));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(mapped);
                UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        /// <summary>
        /// A variant selects an exact detail, then the closest lower detail, and otherwise falls back.
        /// </summary>
        [Test]
        public void ResolveViewPrefab_UsesVariantDetailAndFallback()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant small = ScriptableObject.CreateInstance<ManifestationVariant>();
            ManifestationVariant large = ScriptableObject.CreateInstance<ManifestationVariant>();
            GameObject minimal = new GameObject("minimal");
            GameObject full = new GameObject("full");
            GameObject largeFull = new GameObject("large full");
            GameObject fallback = new GameObject("fallback");
            try
            {
                small.Configure(new Variant("small"), new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Minimal, minimal),
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, full)
                });
                large.Configure(new Variant("large"), new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, largeFull)
                });
                blueprint.Configure(new Kind("vehicles"), null, new[] { small, large }, fallback);

                Assert.That(blueprint.ResolveViewPrefab(new Variant("small"), DetailLevel.Full), Is.SameAs(full));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("small"), DetailLevel.Reduced), Is.SameAs(minimal));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("large"), DetailLevel.Full), Is.SameAs(largeFull));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("large"), DetailLevel.Minimal), Is.SameAs(fallback));
                Assert.That(blueprint.ResolveViewPrefab(new Variant("unknown"), DetailLevel.Full), Is.SameAs(fallback));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(large);
                UnityEngine.Object.DestroyImmediate(minimal);
                UnityEngine.Object.DestroyImmediate(full);
                UnityEngine.Object.DestroyImmediate(largeFull);
                UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        /// <summary>
        /// Invalid detail entries leave the variant's earlier configuration intact.
        /// </summary>
        [TestCase("prefab", "prefab")]
        [TestCase("level", "positive detail level")]
        [TestCase("duplicate", "duplicate")]
        public void ManifestationVariant_RejectsInvalidDetailsAtomically(string failure, string reason)
        {
            ManifestationVariant variant = ScriptableObject.CreateInstance<ManifestationVariant>();
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            GameObject original = new GameObject("original");
            GameObject replacement = new GameObject("replacement");
            try
            {
                Variant id = new Variant("small");
                variant.Configure(id, new[] { new ManifestationVariant.DetailMapping(DetailLevel.Full, original) });
                ManifestationVariant.DetailMapping invalid = new ManifestationVariant.DetailMapping(
                    failure == "level" ? DetailLevel.None : DetailLevel.Minimal,
                    failure == "prefab" ? null : replacement);
                ArgumentException error = Assert.Throws<ArgumentException>(() => variant.Configure(id, new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Minimal, replacement),
                    invalid
                }));
                Assert.That(error.Message, Does.Contain("index 1").And.Contain(reason));
                blueprint.Configure(new Kind("cars"), null, new[] { variant }, null);
                Assert.That(blueprint.ResolveViewPrefab(id, DetailLevel.Full), Is.SameAs(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }

        /// <summary>
        /// A duplicate variant does not replace the blueprint's prior configuration.
        /// </summary>
        [Test]
        public void Configure_RejectsDuplicateVariantsAtomically()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant first = ScriptableObject.CreateInstance<ManifestationVariant>();
            ManifestationVariant duplicate = ScriptableObject.CreateInstance<ManifestationVariant>();
            GameObject original = new GameObject("original");
            GameObject replacement = new GameObject("replacement");
            try
            {
                Variant id = new Variant("small");
                first.Configure(id, new[] { new ManifestationVariant.DetailMapping(DetailLevel.Full, original) });
                duplicate.Configure(id, new[] { new ManifestationVariant.DetailMapping(DetailLevel.Full, replacement) });
                Kind kind = new Kind("cars");
                blueprint.Configure(kind, null, new[] { first }, null);
                Assert.That(Assert.Throws<ArgumentException>(() => blueprint.Configure(kind, null,
                    new[] { first, duplicate }, null)).Message, Does.Contain("duplicate"));
                Assert.That(blueprint.ResolveViewPrefab(id, DetailLevel.Full), Is.SameAs(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(duplicate);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }
    }
}
