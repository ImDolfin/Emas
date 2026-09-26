using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Editor.Tests
{
    /// <summary>
    /// Verifies shared authoring validation and passive diagnostics.
    /// </summary>
    public sealed class InspectorTests
    {
        /// <summary>
        /// Optional prefabs are quiet while invalid variant assets are rejected by authoring and runtime.
        /// </summary>
        [Test]
        public void ManifestationBlueprint_UsesSharedValidationAndOptionalDefaults()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant variant = ScriptableObject.CreateInstance<ManifestationVariant>();
            UnityEditor.Editor blueprintEditor = null;
            UnityEditor.Editor variantEditor = null;
            try
            {
                Assert.That(blueprint.GetConfigurationError(), Does.Contain("non-empty kind ID"));
                blueprint.Configure(new Kind("inspector"), null, null, null);
                Assert.That(blueprint.GetConfigurationError(), Is.Null);
                blueprintEditor = UnityEditor.Editor.CreateEditor(blueprint);
                variantEditor = UnityEditor.Editor.CreateEditor(variant);
                Assert.That(blueprintEditor, Is.TypeOf<ManifestationBlueprintInspector>());
                Assert.That(variantEditor, Is.TypeOf<ManifestationVariantInspector>());

                SerializedObject serialized = new SerializedObject(blueprint);
                SerializedProperty variants = serialized.FindProperty("_variants");
                variants.arraySize = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string error = blueprint.GetConfigurationError();
                Assert.That(error, Does.Contain("index 0").And.Contain("null"));
                using (Realm realm = new Realm())
                {
                    Assert.That(Assert.Throws<ArgumentException>(() =>
                        realm.RegisterManifestationBlueprint(blueprint)).Message, Does.Contain(error));
                }

                variants.GetArrayElementAtIndex(0).objectReferenceValue = variant;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(blueprint.GetConfigurationError(), Is.Null);
                SerializedObject serializedVariant = new SerializedObject(variant);
                serializedVariant.FindProperty("_details").arraySize = 1;
                serializedVariant.ApplyModifiedPropertiesWithoutUndo();
                error = variant.GetConfigurationError();
                Assert.That(error, Does.Contain("index 0").And.Contain("prefab"));
                Assert.That(blueprint.GetConfigurationError(), Does.Contain(error));
                using (Realm realm = new Realm())
                {
                    Assert.That(Assert.Throws<ArgumentException>(() =>
                        realm.RegisterManifestationBlueprint(blueprint)).Message, Does.Contain(error));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprintEditor);
                UnityEngine.Object.DestroyImmediate(variantEditor);
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(variant);
            }
        }

        /// <summary>
        /// Serialized whitespace-only variants are rejected because code cannot construct them.
        /// </summary>
        [Test]
        public void ManifestationBlueprint_RejectsWhitespaceOnlySerializedVariant()
        {
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant variant = ScriptableObject.CreateInstance<ManifestationVariant>();
            GameObject prefab = new GameObject("view prefab");
            try
            {
                variant.Configure(new Variant("valid"), new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, prefab)
                });
                blueprint.Configure(new Kind("inspector.variant"), null, new[] { variant }, null);
                SerializedObject serialized = new SerializedObject(variant);
                SerializedProperty variantId = serialized.FindProperty("_variant").FindPropertyRelative("_id");
                Assert.That(variantId, Is.Not.Null);
                variantId.stringValue = "   ";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string error = variant.GetConfigurationError();
                Assert.That(error, Does.Contain("whitespace-only variant ID"));
                Assert.That(blueprint.GetConfigurationError(), Does.Contain(error));
                using (Realm realm = new Realm())
                {
                    Assert.That(Assert.Throws<ArgumentException>(() =>
                        realm.RegisterManifestationBlueprint(blueprint)).Message, Does.Contain(error));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// Prefab realm and anchor Inspectors report configuration errors before starting a realm.
        /// </summary>
        [Test]
        public void RealmSetup_ValidatesPrefabConfiguration()
        {
            GameObject go = new GameObject("setup validation");
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            UnityEditor.Editor realmEditor = null;
            UnityEditor.Editor anchorEditor = null;
            try
            {
                go.SetActive(false);
                RealmSetup realm = go.AddComponent<RealmSetup>();
                AnchorSetup anchor = go.AddComponent<AnchorSetup>();
                go.AddComponent<InspectorProvider>();
                go.SetActive(true);
                realmEditor = UnityEditor.Editor.CreateEditor(realm);
                anchorEditor = UnityEditor.Editor.CreateEditor(anchor);
                Assert.That(realmEditor, Is.TypeOf<RealmSetupInspector>());
                Assert.That(anchorEditor, Is.TypeOf<AnchorSetupInspector>());
                Assert.That(realm.GetConfigurationError(), Is.Null);

                SerializedObject serialized = new SerializedObject(anchor);
                serialized.FindProperty("_anchorId").stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(realm.GetConfigurationError(), Does.Contain("anchor ID"));
                Assert.That(Assert.Throws<InvalidOperationException>(() => realm.StartRealm()).Message,
                    Is.EqualTo(realm.GetConfigurationError()));

                serialized.FindProperty("_anchorId").stringValue = "valid";
                SerializedProperty entries = serialized.FindProperty("_blueprints");
                entries.arraySize = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(realm.GetConfigurationError(), Does.Contain("index 0 is null"));

                blueprint.Configure(new Kind("test"), null, null, null);
                entries.arraySize = 2;
                entries.GetArrayElementAtIndex(0).objectReferenceValue = blueprint;
                entries.GetArrayElementAtIndex(1).objectReferenceValue = blueprint;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(realm.GetConfigurationError(), Does.Contain("duplicates kind"));

                entries.arraySize = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(realm.GetConfigurationError(), Is.Null);
                Assert.That(realm.Realm, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(realmEditor);
                UnityEngine.Object.DestroyImmediate(anchorEditor);
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(blueprint);
            }
        }

        /// <summary>
        /// Ghost authoring keeps application fields visible and Emas-owned metadata serialized but hidden.
        /// </summary>
        [Test]
        public void GhostInspector_HidesOwnedFieldsAndKeepsApplicationFields()
        {
            GameObject root = new GameObject("ghost authoring");
            UnityEditor.Editor editor = null;
            try
            {
                InspectorGhost ghost = root.AddComponent<InspectorGhost>();
                ghost.Initialize(new Key("simulation", new Kind("vehicles.car"), "42"), "Car 42", Variant.None);
                editor = UnityEditor.Editor.CreateEditor(ghost);
                Assert.That(editor, Is.TypeOf<GhostInspector>());

                SerializedObject serialized = new SerializedObject(ghost);
                Assert.That(serialized.FindProperty("_anchorId"), Is.Not.Null);
                Assert.That(serialized.FindProperty("_isAvailable"), Is.Not.Null);
                List<string> visible = new List<string>();
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    visible.Add(property.propertyPath);
                    enterChildren = false;
                }

                Assert.That(visible, Does.Contain("_customValue"));
                Assert.That(ghost.CustomValue, Is.EqualTo(7));
                Assert.That(visible, Does.Not.Contain("_anchorId"));
                Assert.That(visible, Does.Not.Contain("_entityId"));
                Assert.That(visible, Does.Not.Contain("_kindId"));
                Assert.That(visible, Does.Not.Contain("_name"));
                Assert.That(visible, Does.Not.Contain("_variant"));
                Assert.That(visible, Does.Not.Contain("_isAvailable"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Returns to Edit Mode if an assertion interrupts a Play Mode inspection.
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        /// <summary>
        /// Repeated diagnostics snapshots neither construct a realm nor tick an existing source.
        /// </summary>
        [UnityTest]
        public IEnumerator Diagnostics_ArePassiveAndReportSourceFailure()
        {
            FieldInfo field = typeof(DefaultRuntime).GetField("_realm", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = field.GetValue(null);
            try
            {
                field.SetValue(null, null);
                Assert.That(DiagnosticsWindow.Capture(), Is.Null);
                Realm observed;
                Assert.That(DefaultRuntime.TryGetRealm(out observed), Is.False);
            }
            finally
            {
                field.SetValue(null, previous);
            }

            yield return new EnterPlayMode();
            Realm realm = Realm.Default;
            try
            {
                FailedSource source = new FailedSource(true) { Name = "Vehicle feed" };
                Anchor anchor = realm.GetOrCreateAnchor("diagnostic", new FailedSource());
                Assert.Throws<InvalidOperationException>(() => anchor.ReplaceSource(anchor.Sources[0], source));
                System.Collections.Generic.IReadOnlyList<DiagnosticsWindow.AnchorStatus> first = DiagnosticsWindow.Capture();
                System.Collections.Generic.IReadOnlyList<DiagnosticsWindow.AnchorStatus> second = DiagnosticsWindow.Capture();
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(first[0].Id, Is.EqualTo("diagnostic"));
                Assert.That(first[0].Available, Is.Zero);
                Assert.That(first[0].Sources[0].Status, Is.EqualTo("Stopped (attached)"));
                Assert.That(first[0].Sources[0].Error, Is.SameAs(source.LastError));
                Assert.That(first[0].Sources[0].Name, Is.EqualTo("Vehicle feed"));
                Assert.That(first[0].Sources[0].ErrorContext, Is.EqualTo(source.LastErrorContext));
                Assert.That(first[0].Sources[0].ErrorContext, Does.Contain("diagnostic").And.Contain("OnStart"));
                Assert.That(second[0].Sources[0].Error.Message, Is.EqualTo("diagnostic failure"));
                Assert.That(source.Updates, Is.Zero);
                realm.Dispose();
                Assert.That(DiagnosticsWindow.Capture(), Is.Null);
            }
            finally
            {
                realm.Dispose();
            }

            yield return new ExitPlayMode();
        }

        private sealed class InspectorProvider : MonoBehaviour, ISourceProvider
        {
            /// <summary>
            /// Returns no source because this test only inspects configuration.
            /// </summary>
            public PresenceSource CreateSource()
            {
                return null;
            }
        }

        private sealed class InspectorGhost : Ghost
        {
            [SerializeField]
            private int _customValue = 7;

            internal int CustomValue
            {
                get
                {
                    return _customValue;
                }
            }
        }

        private sealed class FailedSource : PresenceSource
        {
            internal int Updates;
            private readonly bool _fail;

            internal FailedSource(bool fail = false)
            {
                _fail = fail;
            }

            protected override void OnStart()
            {
                if (_fail)
                {
                    throw new InvalidOperationException("diagnostic failure");
                }
            }

            protected override void OnUpdate()
            {
                Updates++;
            }
        }
    }
}
