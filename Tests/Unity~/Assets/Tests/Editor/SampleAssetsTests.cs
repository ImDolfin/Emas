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
        /// <param name="sample">The directory name of the sample under the package and imported sample roots.</param>
        [TestCase("Minimal")]
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
        /// <param name="sample">The directory name of the sample under the package and imported sample roots.</param>
        /// <param name="sceneFile">The scene path relative to the sample directory.</param>
        [TestCase("Minimal", "QuickStart.unity")]
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
                SerializedObject serializedSetup = new SerializedObject(setup);
                Assert.That(serializedSetup.FindProperty("_interpolationDelay").floatValue,
                    Is.EqualTo(sample == "RelativeWorld" ? 0.1f : 0f),
                    "RelativeWorld authors a shared playback buffer; the introductory samples retain the immediate default.");

                AnchorSetup[] anchors = setup.GetComponentsInChildren<AnchorSetup>(true);
                Assert.That(anchors, Is.Not.Empty);
                foreach (AnchorSetup anchor in anchors)
                {
                    Assert.That(anchor.GetComponents<MonoBehaviour>().Count(component =>
                        component != null && component.enabled && component is PresenceDetectorComponent), Is.EqualTo(1));
                    Assert.That(anchor.GetComponents<GhostInitializer>().Count(component => component.enabled), Is.EqualTo(1),
                        "The SDK mapping must be authored beside the detector.");
                    Assert.That(new SerializedObject(anchor).FindProperty("_automaticViews").boolValue, Is.True);
                }

                SerializedProperty blueprints = serializedSetup.FindProperty("_blueprints");
                Assert.That(blueprints.arraySize, Is.GreaterThan(0));
                for (int index = 0; index < blueprints.arraySize; index++)
                {
                    ManifestationBlueprint blueprint = blueprints.GetArrayElementAtIndex(index)
                        .objectReferenceValue as ManifestationBlueprint;
                    Assert.That(blueprint, Is.Not.Null);
                    Assert.That(AssetDatabase.GetAssetPath(blueprint), Does.StartWith(directory));
                    Assert.That(blueprint.Kind.IsValid, Is.True);
                    Assert.That(blueprint.GhostPrefab, Is.TypeOf<Ghost>(),
                        "Sample entities should be composed from a plain Ghost and authored traits.");
                    Assert.That(PrefabUtility.IsPartOfPrefabAsset(blueprint.GhostPrefab), Is.True);
                    Assert.That(AssetDatabase.GetAssetPath(blueprint.GhostPrefab), Does.StartWith(directory));
                    Assert.That(blueprint.GhostPrefab.GetComponents<Trait>(), Is.Not.Empty,
                        "Reusable data traits must be saved on the Ghost prefab.");

                    if (sample == "RelativeWorld")
                    {
                        Prediction prediction = blueprint.GhostPrefab.GetRequired<Prediction>();
                        Smoothing smoothing = blueprint.GhostPrefab.GetRequired<Smoothing>();
                        Assert.That(prediction.enabled, Is.True);
                        Assert.That(prediction.MaximumExtrapolation, Is.EqualTo(0.05f));
                        Assert.That(smoothing.enabled, Is.True);
                        Assert.That(smoothing.PositionHalfLife, Is.EqualTo(0.08f));
                        Assert.That(smoothing.RotationHalfLife, Is.EqualTo(0.04f));
                        Assert.That(blueprint.GhostPrefab.GetRequired<Emas.RelativeWorld.GeoVelocityTrait>().enabled, Is.True);
                    }

                    SerializedProperty variants = new SerializedObject(blueprint).FindProperty("_variants");
                    Assert.That(variants.arraySize, Is.GreaterThan(0));
                    for (int variantIndex = 0; variantIndex < variants.arraySize; variantIndex++)
                    {
                        SerializedProperty variant = variants.GetArrayElementAtIndex(variantIndex);
                        Assert.That(variant.FindPropertyRelative("_name").stringValue, Is.Not.Empty);
                        GameObject view = variant.FindPropertyRelative("_prefab").objectReferenceValue as GameObject;
                        Assert.That(view, Is.Not.Null);
                        Assert.That(PrefabUtility.IsPartOfPrefabAsset(view), Is.True);
                        Assert.That(AssetDatabase.GetAssetPath(view), Does.StartWith(directory));
                        Assert.That(view.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
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
