using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies actionable mapping errors and atomic blueprint configuration.
    /// </summary>
    public sealed class BlueprintTests
    {
        /// <summary>
        /// Invalid entries report their index and cause without replacing the current configuration.
        /// </summary>
        [TestCase("prefab", "requires a non-null prefab")]
        [TestCase("level", "requires a positive detail level")]
        [TestCase("duplicate", "duplicates index 0")]
        public void Configure_IdentifiesInvalidMappingAndRetainsConfiguration(string failure, string reason)
        {
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            GameObject prefab = new GameObject("original view");
            GameObject replacement = new GameObject("replacement view");
            try
            {
                Kind kind = new Kind("cars");
                Variant variant = new Variant("small");
                blueprint.Configure(kind, null, new[]
                {
                    new Blueprint.ViewMapping(variant, DetailLevel.Full, prefab)
                }, prefab);
                Blueprint.ViewMapping invalid = new Blueprint.ViewMapping(variant,
                    failure == "level" ? DetailLevel.None : DetailLevel.Minimal,
                    failure == "prefab" ? null : replacement);
                ArgumentException error = Assert.Throws<ArgumentException>(() => blueprint.Configure(kind, null, new[]
                {
                    new Blueprint.ViewMapping(variant, DetailLevel.Minimal, replacement),
                    invalid
                }, replacement));
                Assert.That(error.ParamName, Is.EqualTo("views"));
                Assert.That(error.Message, Does.Contain("index 1"));
                Assert.That(error.Message, Does.Contain("variant 'small'"));
                Assert.That(error.Message, Does.Contain("detail level " + invalid.DetailLevel.Level));
                Assert.That(error.Message, Does.Contain(reason));
                Assert.That(blueprint.ResolveViewPrefab(variant, DetailLevel.Full), Is.SameAs(prefab));
                Assert.That(blueprint.FallbackViewPrefab, Is.SameAs(prefab));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }
    }
}
