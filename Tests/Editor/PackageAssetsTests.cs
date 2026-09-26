using System;
using NUnit.Framework;
using UnityEditor;

namespace Emas.Editor.Tests
{
    /// <summary>
    /// Verifies Unity imports the package's component and blueprint scripts.
    /// </summary>
    public sealed class PackageAssetsTests
    {
        /// <summary>
        /// A package script imports as its declared public type.
        /// </summary>
        [TestCase("Runtime/Entities/Ghost.cs", typeof(Ghost))]
        [TestCase("Runtime/Entities/Spatial.cs", typeof(Spatial))]
        [TestCase("Runtime/Unity/RealmSetup.cs", typeof(RealmSetup))]
        [TestCase("Runtime/Unity/AnchorSetup.cs", typeof(AnchorSetup))]
        [TestCase("Runtime/Views/View.cs", typeof(View))]
        [TestCase("Runtime/Views/Blueprint.cs", typeof(Blueprint))]
        public void PackageScript_ResolvesDeclaredType(string relativePath, Type expectedType)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.emas.core/" + relativePath);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.GetClass(), Is.EqualTo(expectedType));
        }
    }
}
