using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Editor.Tests
{
    /// <summary>
    /// Verifies that Unity-authored settings produce the documented public runtime behavior.
    /// </summary>
    public sealed class InspectorTests
    {
        private static readonly Kind TestKind = new Kind("authoring.entity");
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        /// <summary>
        /// Releases authored objects and returns to Edit Mode even when a runtime assertion fails.
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (UnityEngine.Object value in _objects)
            {
                if (value != null)
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }

            _objects.Clear();
            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        /// <summary>
        /// Registration rejects an unassigned variant asset with an actionable array index for its author.
        /// </summary>
        [Test]
        public void SerializedBlueprint_RejectsUnassignedVariant()
        {
            ManifestationBlueprint blueprint = CreateBlueprint(null);
            SerializedObject serialized = new SerializedObject(blueprint);
            serialized.FindProperty("_variants").arraySize = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            using (Realm realm = new Realm())
            {
                ArgumentException error = Assert.Throws<ArgumentException>(() =>
                    realm.RegisterManifestationBlueprint(blueprint));
                Assert.That(error.Message, Does.Contain("index 0").And.Contain("null"));
            }
        }

        /// <summary>
        /// Prefab startup rejects an anchor without a provider before exposing a partially configured realm.
        /// </summary>
        [Test]
        public void AnchorWithoutProvider_FailsBeforeRealmCreation()
        {
            RealmSetup setup = CreateRealm();
            AnchorSetup anchor = setup.gameObject.AddComponent<AnchorSetup>();
            setup.gameObject.SetActive(true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => setup.StartRealm());
            Assert.That(error.Message, Does.Contain("IDetectorProvider"));
            Assert.That(setup.Realm, Is.Null);
            Assert.That(anchor.Anchor, Is.Null);
        }

        /// <summary>
        /// Authored anchors use the realm blueprint, while automatic view requests remain optional per anchor.
        /// </summary>
        [UnityTest]
        public IEnumerator SerializedRealmBlueprint_AppliesAcrossAnchors()
        {
            yield return new EnterPlayMode();
            RealmSetup setup = CreateRealm();
            SetBlueprint(setup, CreateBlueprint("realm view"));
            CreateAnchor(setup, "first");
            CreateAnchor(setup, "second");
            AnchorSetup manual = CreateAnchor(setup, "manual");
            SerializedObject serialized = new SerializedObject(manual);
            serialized.FindProperty("_automaticViews").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            setup.Realm.Update();

            foreach (string id in new[] { "first", "second" })
            {
                Ghost ghost = (Ghost)setup.Realm.Query().InAnchor(id).Single();
                Assert.That(ghost.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("realm view"));
            }

            Ghost manualGhost = (Ghost)setup.Realm.Query().InAnchor("manual").Single();
            Assert.That(manualGhost.IsAvailable, Is.True);
            Assert.That(manualGhost.GetComponentInChildren<View>(true), Is.Null);
            Assert.That(setup.Realm.Manifest(manualGhost).gameObject.name, Is.EqualTo("realm view"));
        }

        /// <summary>
        /// A followed reference authored on the prefab projects distant double coordinates near the Unity origin.
        /// </summary>
        [UnityTest]
        public IEnumerator SerializedReferenceFrame_FollowsConfiguredGhost()
        {
            yield return new EnterPlayMode();
            RealmSetup setup = CreateRealm();
            AnchorSetup anchor = CreateAnchor(setup, "world");
            anchor.GetComponent<InspectorProvider>().Factory = () => new SpatialDetector();
            SerializedObject serialized = new SerializedObject(setup);
            serialized.FindProperty("_useReferenceFrame").boolValue = true;
            serialized.FindProperty("_followGhost").boolValue = true;
            serialized.FindProperty("_referenceAnchorId").stringValue = "world";
            serialized.FindProperty("_referenceKind").FindPropertyRelative("_id").stringValue = TestKind.Id;
            serialized.FindProperty("_referenceEntityId").stringValue = "ego";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            setup.Realm.Update();

            IGhost ego;
            IGhost traffic;
            Assert.That(setup.Realm.TryGetGhost(new Key("world", TestKind, "ego"), out ego), Is.True);
            Assert.That(setup.Realm.TryGetGhost(new Key("world", TestKind, "traffic"), out traffic), Is.True);
            Assert.That(setup.Realm.ReferenceFrame.FollowedGhost, Is.EqualTo(ego.Key));
            Assert.That(setup.Realm.ReferenceFrame.IsReferenceAvailable, Is.True);
            Assert.That(((Ghost)ego).transform.position.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(((Ghost)traffic).transform.position.x, Is.EqualTo(20.25f).Within(0.0001f));
        }

        /// <summary>
        /// Opening diagnostics and the overlay does not advance an explicitly managed Realm or construct scene objects.
        /// </summary>
        [UnityTest]
        public IEnumerator DiagnosticsAndOverlay_ObserveWithoutAdvancingTracking()
        {
            yield return new EnterPlayMode();
            using (Realm realm = new Realm())
            {
                DiagnosticsProbe detector = new DiagnosticsProbe();
                realm.GetOrCreateAnchor("passive", detector);
                int sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                Emas.Editor.DiagnosticsWindow window = ScriptableObject.CreateInstance<Emas.Editor.DiagnosticsWindow>();
                try
                {
                    Emas.Editor.RealmOverlay overlay = new Emas.Editor.RealmOverlay();
                    window.rootVisualElement.Add(overlay.CreatePanelContent());
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        window.ShowUtility();
                        window.Repaint();
                    }
                    yield return null;
                    yield return null;
                    Assert.That(detector.Updates, Is.Zero);
                    Assert.That(realm.Anchors.Count, Is.EqualTo(1));
                    Assert.That(Resources.FindObjectsOfTypeAll<GameObject>().Length, Is.EqualTo(sceneObjects));
                }
                finally
                {
                    if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        UnityEngine.Object.DestroyImmediate(window);
                    }
                    else
                    {
                        window.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Authored visibility radii reject nonpositive or nonfinite values before any Realm starts.
        /// </summary>
        [Test]
        public void SerializedVisibilityRange_RequiresPositiveFiniteSharedUnits()
        {
            RealmSetup setup = CreateRealm();
            SerializedObject settings = new SerializedObject(setup);
            settings.FindProperty("_useReferenceFrame").boolValue = true;
            settings.FindProperty("_limitDistance").boolValue = true;
            settings.FindProperty("_maxDistance").doubleValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            Assert.That(Assert.Throws<InvalidOperationException>(() => setup.StartRealm()).Message,
                Does.Contain("positive and finite"));
            Assert.That(setup.Realm, Is.Null);

            settings.Update();
            settings.FindProperty("_maxDistance").doubleValue = double.PositiveInfinity;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Assert.Throws<InvalidOperationException>(() => setup.StartRealm()).Message,
                Does.Contain("positive and finite"));
            Assert.That(setup.Realm, Is.Null);
        }

        /// <summary>
        /// Ghost authoring exposes application fields while keeping runtime-owned identity and availability out of the Inspector.
        /// </summary>
        [Test]
        public void GhostAuthoring_ExposesApplicationFieldsAndHidesRuntimeMetadata()
        {
            GameObject root = new GameObject("ghost authoring");
            _objects.Add(root);
            InspectorGhost ghost = root.AddComponent<InspectorGhost>();
            SerializedObject serialized = new SerializedObject(ghost);
            List<string> visible = new List<string>();
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                visible.Add(property.propertyPath);
                enterChildren = false;
            }

            Assert.That(visible, Does.Contain("_customValue"));
            Assert.That(visible, Does.Not.Contain("_anchorId"));
            Assert.That(visible, Does.Not.Contain("_entityId"));
            Assert.That(visible, Does.Not.Contain("_kindId"));
            Assert.That(visible, Does.Not.Contain("_name"));
            Assert.That(visible, Does.Not.Contain("_variant"));
            Assert.That(visible, Does.Not.Contain("_isAvailable"));
        }

        private RealmSetup CreateRealm()
        {
            GameObject root = new GameObject("authored realm");
            _objects.Add(root);
            root.SetActive(false);
            return root.AddComponent<RealmSetup>();
        }

        private static AnchorSetup CreateAnchor(RealmSetup realm, string id)
        {
            GameObject owner = new GameObject(id);
            owner.transform.SetParent(realm.transform, false);
            InspectorProvider provider = owner.AddComponent<InspectorProvider>();
            provider.Factory = () => new PublishingDetector();
            AnchorSetup anchor = owner.AddComponent<AnchorSetup>();
            SerializedObject serialized = new SerializedObject(anchor);
            serialized.FindProperty("_anchorId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return anchor;
        }

        private ManifestationBlueprint CreateBlueprint(string viewName)
        {
            GameObject prefab = null;
            if (viewName != null)
            {
                prefab = new GameObject(viewName);
                prefab.SetActive(false);
                _objects.Add(prefab);
            }

            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(TestKind, null, null, prefab);
            _objects.Add(blueprint);
            return blueprint;
        }

        private static void SetBlueprint(UnityEngine.Object target, ManifestationBlueprint blueprint)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty entries = serialized.FindProperty("_blueprints");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).objectReferenceValue = blueprint;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class DiagnosticsProbe : PresenceDetector
        {
            internal int Updates;

            /// <inheritdoc />
            protected override void OnUpdate()
            {
                Updates++;
            }
        }

        private sealed class InspectorProvider : MonoBehaviour, IDetectorProvider
        {
            internal Func<PresenceDetector> Factory;

            /// <summary>
            /// Creates the detector used by the authored prefab.
            /// </summary>
            public PresenceDetector CreateDetector()
            {
                return Factory();
            }
        }

        private sealed class InspectorGhost : Ghost
        {
            [SerializeField]
            private int _customValue = 7;
        }

        private sealed class PublishingDetector : PresenceDetector
        {
            /// <inheritdoc />
            protected override void OnStart()
            {
                GetOrCreate<InspectorGhost>("one", TestKind);
            }
        }

        private sealed class SpatialDetector : PresenceDetector
        {
            /// <inheritdoc />
            protected override void OnStart()
            {
                SpatialGhost ego = GetOrCreate<SpatialGhost>("ego", TestKind);
                ego.GetComponent<Spatial>().SetPosition(new Double3(1000000000.125, 0.0, 1000000000.375));
                SpatialGhost traffic = GetOrCreate<SpatialGhost>("traffic", TestKind);
                traffic.GetComponent<Spatial>().SetPosition(new Double3(1000000020.375, 0.0, 1000000000.375));
            }
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class SpatialGhost : Ghost
        {
        }
    }
}
