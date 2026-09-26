using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace Emas.Tests.Samples
{
    /// <summary>
    /// Prevents the imported test samples from drifting away from the shipped package.
    /// </summary>
    public sealed class SampleAssetsTests
    {
        /// <summary>
        /// Every imported sample file matches the package, including scene and prefab metadata.
        /// </summary>
        [TestCase("Minimal")]
        [TestCase("Callbacks")]
        [TestCase("Example")]
        [TestCase("RelativeWorld")]
        public void ImportedSample_MatchesPackage(string sample)
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(Realm).Assembly);
            Assert.That(package, Is.Not.Null);
            string source = Path.Combine(package.resolvedPath, "Samples~", sample);
            string imported = Path.Combine(Application.dataPath, "Samples", sample);
            Assert.That(Directory.Exists(imported), Is.True, imported);
            string[] expected = RelativeFiles(source);
            Assert.That(RelativeFiles(imported), Is.EquivalentTo(expected), "Reimport the updated sample.");
            foreach (string relative in expected)
            {
                // Text comparison allows Git's platform-specific line endings.
                Assert.That(File.ReadAllText(Path.Combine(imported, relative)).Replace("\r\n", "\n"),
                    Is.EqualTo(File.ReadAllText(Path.Combine(source, relative)).Replace("\r\n", "\n")),
                    sample + "/" + relative + ": reimport the updated sample.");
            }
        }

        /// <summary>
        /// Each sample opens with a reusable tracking prefab and persistent blueprint, root and variant view assets.
        /// </summary>
        [TestCase("Minimal", "QuickStart.unity")]
        [TestCase("Callbacks", "Callbacks.unity")]
        [TestCase("Example", "Scenes/Example.unity")]
        [TestCase("RelativeWorld", "RelativeWorld.unity")]
        public void SampleScene_UsesAuthoredTrackingAssets(string sample, string sceneFile)
        {
            string directory = "Assets/Samples/" + sample + "/";
            Scene scene = EditorSceneManager.OpenScene(directory + sceneFile, OpenSceneMode.Additive);
            try
            {
                RealmSetup[] setups = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RealmSetup>(true)).ToArray();
                Assert.That(setups, Has.Length.EqualTo(1));
                RealmSetup setup = setups[0];
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(setup), Is.True,
                    "The configured tracking setup should be reusable as a prefab.");
                Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(setup)),
                    Does.StartWith(directory));

                AnchorSetup[] anchors = setup.GetComponentsInChildren<AnchorSetup>(true);
                Assert.That(anchors, Is.Not.Empty);
                foreach (AnchorSetup anchor in anchors)
                {
                    Assert.That(anchor.GetComponents<MonoBehaviour>().Count(component =>
                        component != null && component.enabled && component is IDetectorProvider), Is.EqualTo(1));
                    Assert.That(new SerializedObject(anchor).FindProperty("_automaticViews").boolValue, Is.True);
                }

                SerializedProperty blueprints = new SerializedObject(setup).FindProperty("_blueprints");
                Assert.That(blueprints.arraySize, Is.GreaterThan(0));
                for (int index = 0; index < blueprints.arraySize; index++)
                {
                    ManifestationBlueprint blueprint = blueprints.GetArrayElementAtIndex(index)
                        .objectReferenceValue as ManifestationBlueprint;
                    Assert.That(blueprint, Is.Not.Null);
                    Assert.That(AssetDatabase.GetAssetPath(blueprint), Does.StartWith(directory));
                    Assert.That(blueprint.Kind.IsValid, Is.True);
                    Assert.That(blueprint.GhostPrefab, Is.Not.Null);
                    Assert.That(PrefabUtility.IsPartOfPrefabAsset(blueprint.GhostPrefab), Is.True);
                    Assert.That(AssetDatabase.GetAssetPath(blueprint.GhostPrefab), Does.StartWith(directory));

                    SerializedProperty variants = new SerializedObject(blueprint).FindProperty("_variants");
                    Assert.That(variants.arraySize, Is.GreaterThan(0));
                    for (int variantIndex = 0; variantIndex < variants.arraySize; variantIndex++)
                    {
                        ManifestationVariant variant = variants.GetArrayElementAtIndex(variantIndex)
                            .objectReferenceValue as ManifestationVariant;
                        Assert.That(variant, Is.Not.Null);
                        Assert.That(AssetDatabase.GetAssetPath(variant), Does.StartWith(directory));
                        SerializedProperty details = new SerializedObject(variant).FindProperty("_details");
                        Assert.That(details.arraySize, Is.GreaterThan(0));
                        for (int detailIndex = 0; detailIndex < details.arraySize; detailIndex++)
                        {
                            GameObject view = details.GetArrayElementAtIndex(detailIndex)
                                .FindPropertyRelative("_prefab").objectReferenceValue as GameObject;
                            Assert.That(view, Is.Not.Null);
                            Assert.That(PrefabUtility.IsPartOfPrefabAsset(view), Is.True);
                            Assert.That(AssetDatabase.GetAssetPath(view), Does.StartWith(directory));
                            Assert.That(view.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
                        }
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string[] RelativeFiles(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(directory.Length + 1)).ToArray();
        }
    }
}
