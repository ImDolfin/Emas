using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Emas.Tests.Samples
{
    /// <summary>Prevents the imported test samples from drifting away from the shipped package.</summary>
    public sealed class SampleAssetsTests
    {
        /// <summary>Every imported sample file matches the package, including scene and prefab metadata.</summary>
        [TestCase("Minimal")]
        [TestCase("Callbacks")]
        [TestCase("Example")]
        public void ImportedSample_MatchesPackage(string sample)
        {
            var package = PackageInfo.FindForAssembly(typeof(Realm).Assembly);
            Assert.That(package, Is.Not.Null);
            var source = Path.Combine(package.resolvedPath, "Samples~", sample);
            var imported = Path.Combine(Application.dataPath, "Samples", sample);
            Assert.That(Directory.Exists(imported), Is.True, imported);
            var expected = RelativeFiles(source);
            Assert.That(RelativeFiles(imported), Is.EquivalentTo(expected), "Reimport the updated sample.");
            foreach (var relative in expected)
            {
                // Text comparison allows Git's platform-specific line endings.
                Assert.That(File.ReadAllText(Path.Combine(imported, relative)).Replace("\r\n", "\n"),
                    Is.EqualTo(File.ReadAllText(Path.Combine(source, relative)).Replace("\r\n", "\n")),
                    sample + "/" + relative + ": reimport the updated sample.");
            }
        }

        private static string[] RelativeFiles(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(directory.Length + 1)).ToArray();
        }
    }
}
