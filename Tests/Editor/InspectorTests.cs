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
        /// An authored anchor inherits the realm view unless its blueprint replaces it or intentionally supplies no view.
        /// </summary>
        [UnityTest]
        public IEnumerator SerializedBlueprints_ControlAnchorViewOverrides()
        {
            yield return new EnterPlayMode();
            RealmSetup setup = CreateRealm();
            SetBlueprint(setup, CreateBlueprint("realm view"));
            CreateAnchor(setup, "inherited");
            SetBlueprint(CreateAnchor(setup, "custom"), CreateBlueprint("custom view"));
            SetBlueprint(CreateAnchor(setup, "silent"), CreateBlueprint(null));
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            setup.Realm.Update();

            Ghost inherited = (Ghost)setup.Realm.Query().InAnchor("inherited").Single();
            Ghost custom = (Ghost)setup.Realm.Query().InAnchor("custom").Single();
            Ghost silent = (Ghost)setup.Realm.Query().InAnchor("silent").Single();
            Assert.That(inherited.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("realm view"));
            Assert.That(custom.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("custom view"));
            Assert.That(silent.IsAvailable, Is.True);
            Assert.That(silent.GetComponentInChildren<View>(true), Is.Null);
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
