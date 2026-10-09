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
        /// <returns>An iterator that waits for Unity to finish releasing the scenario assets.</returns>
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
        /// Incomplete and duplicate inline rows identify the offending name or prefab before registration.
        /// </summary>
        [Test]
        public void SerializedBlueprint_RejectsInvalidVariants()
        {
            ManifestationBlueprint blueprint = CreateBlueprint(null);
            GameObject prefab = new GameObject("row prefab");
            _objects.Add(prefab);
            SerializedObject serialized = new SerializedObject(blueprint);
            SerializedProperty rows = serialized.FindProperty("_variants");
            rows.arraySize = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            using (Realm realm = new Realm())
            {
                Assert.That(Assert.Throws<ArgumentException>(() =>
                    realm.RegisterManifestationBlueprint(blueprint)).Message,
                    Does.Contain("index 0").And.Contain("name"));

                rows.GetArrayElementAtIndex(0).FindPropertyRelative("_name").stringValue = "car";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(Assert.Throws<ArgumentException>(() =>
                    realm.RegisterManifestationBlueprint(blueprint)).Message,
                    Does.Contain("car").And.Contain("prefab"));

                rows.GetArrayElementAtIndex(0).FindPropertyRelative("_prefab").objectReferenceValue = prefab;
                rows.arraySize = 2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(Assert.Throws<ArgumentException>(() =>
                    realm.RegisterManifestationBlueprint(blueprint)).Message,
                    Does.Contain("index 1").And.Contain("duplicate"));

                rows.GetArrayElementAtIndex(1).FindPropertyRelative("_name").stringValue = "car_low";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.DoesNotThrow(() => realm.RegisterManifestationBlueprint(blueprint));
            }
        }

        /// <summary>
        /// Inline rows retain their names and prefab references through serialization, reordering, removal and undo.
        /// </summary>
        [Test]
        public void SerializedBlueprint_InlineRowsSupportEditingAndUndo()
        {
            ManifestationBlueprint blueprint = CreateBlueprint(null);
            GameObject full = new GameObject("full");
            GameObject low = new GameObject("low");
            _objects.Add(full);
            _objects.Add(low);
            blueprint.Configure(TestKind, null, new[]
            {
                new ManifestationVariant("car", full),
                new ManifestationVariant("car_low", low)
            }, null);
            ManifestationBlueprint copy = CreateBlueprint(null);
            EditorUtility.CopySerialized(blueprint, copy);
            Assert.That(copy.ResolveViewPrefab(new Variant("car_low")), Is.SameAs(low));

            SerializedObject serialized = new SerializedObject(copy);
            SerializedProperty rows = serialized.FindProperty("_variants");
            rows.MoveArrayElement(1, 0);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(copy.ResolveViewPrefab(new Variant("car")), Is.SameAs(full));
            Assert.That(copy.ResolveViewPrefab(new Variant("car_low")), Is.SameAs(low));

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            rows.DeleteArrayElementAtIndex(0);
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Assert.That(copy.ResolveViewPrefab(new Variant("car_low")), Is.Null);
            Assert.That(copy.ResolveViewPrefab(new Variant("car")), Is.SameAs(full));
            Undo.PerformUndo();
            Assert.That(copy.ResolveViewPrefab(new Variant("car_low")), Is.SameAs(low));
            Undo.PerformRedo();
            Assert.That(copy.ResolveViewPrefab(new Variant("car_low")), Is.Null);
            Undo.ClearUndo(copy);
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
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
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
        /// Authored coordinate axes reject ambiguous mappings before startup and configure projection once corrected.
        /// </summary>
        [Test]
        public void SerializedCoordinates_ValidateAndConfigureReference()
        {
            RealmSetup setup = CreateRealm();
            SerializedObject serialized = new SerializedObject(setup);
            serialized.FindProperty("_useReferenceFrame").boolValue = true;
            SerializedProperty coordinates = serialized.FindProperty("_coordinates");
            coordinates.FindPropertyRelative("_right").intValue = (int)Axis.PositiveY;
            coordinates.FindPropertyRelative("_up").intValue = (int)Axis.NegativeY;
            coordinates.FindPropertyRelative("_forward").intValue = (int)Axis.PositiveX;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            Assert.That(Assert.Throws<InvalidOperationException>(() => setup.StartRealm()).Message,
                Does.Contain("each source axis"));
            Assert.That(setup.Realm, Is.Null);

            coordinates.FindPropertyRelative("_up").intValue = (int)Axis.NegativeZ;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.StartRealm();
            ReferenceFrame frame = setup.Realm.ReferenceFrame;
            Assert.That(frame.TryToUnityPosition(new Double3(2, 3, 5), out Vector3 position), Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(3, -5, 2)));
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(Quaternion.AngleAxis(90, Vector3.forward)),
                Quaternion.AngleAxis(90, Vector3.up)), Is.LessThan(0.05f));
        }

        /// <summary>
        /// A manually authored geographic reference validates WGS84 fields and projects around that location.
        /// </summary>
        [Test]
        public void SerializedGeographicReference_ValidatesAndUsesWgs84Origin()
        {
            RealmSetup setup = CreateRealm();
            SerializedObject serialized = new SerializedObject(setup);
            serialized.FindProperty("_useReferenceFrame").boolValue = true;
            serialized.FindProperty("_referenceSpace").intValue = (int)ReferenceSpace.Geographic;
            SerializedProperty geographic = serialized.FindProperty("_geographicPosition");
            geographic.FindPropertyRelative("_latitudeDegrees").doubleValue = 91;
            geographic.FindPropertyRelative("_longitudeDegrees").doubleValue = 90;
            geographic.FindPropertyRelative("_heightMeters").doubleValue = 100;
            serialized.FindProperty("_unityPosition").vector3Value = new Vector3(3, 4, 5);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            Assert.That(Assert.Throws<InvalidOperationException>(() => setup.StartRealm()).Message, Does.Contain("latitude"));
            Assert.That(setup.Realm, Is.Null);

            geographic.FindPropertyRelative("_latitudeDegrees").doubleValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.StartRealm();
            ReferenceFrame frame = setup.Realm.ReferenceFrame;
            Assert.That(frame.Space, Is.EqualTo(ReferenceSpace.Geographic));
            Assert.That(frame.TryToUnityPosition(new GeoPosition(0, 90, 102), out Vector3 position), Is.True);
            Assert.That(Vector3.Distance(position, new Vector3(3, 6, 5)), Is.LessThan(0.0001f));
        }

        /// <summary>
        /// A followed reference authored on the prefab projects distant double coordinates near the Unity origin.
        /// </summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
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
        /// An empty authored target waits for runtime selection and preserves frame settings and views when the target changes.
        /// </summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator SerializedReferenceFrame_WaitsForRuntimeTarget()
        {
            yield return new EnterPlayMode();
            RealmSetup setup = CreateRealm();
            SetBlueprint(setup, CreateBlueprint("runtime reference view"));
            AnchorSetup anchor = CreateAnchor(setup, "world");
            anchor.GetComponent<InspectorProvider>().Factory = () => new SpatialDetector();
            SerializedObject serialized = new SerializedObject(setup);
            serialized.FindProperty("_useReferenceFrame").boolValue = true;
            serialized.FindProperty("_followGhost").boolValue = true;
            serialized.FindProperty("_unityPosition").vector3Value = new Vector3(5, 6, 7);
            serialized.FindProperty("_unityRotation").quaternionValue = Quaternion.AngleAxis(90, Vector3.up);
            serialized.FindProperty("_followRotation").boolValue = false;
            serialized.FindProperty("_limitDistance").boolValue = true;
            serialized.FindProperty("_maxDistance").doubleValue = 100;
            SerializedProperty coordinates = serialized.FindProperty("_coordinates");
            coordinates.FindPropertyRelative("_right").intValue = (int)Axis.PositiveX;
            coordinates.FindPropertyRelative("_up").intValue = (int)Axis.PositiveZ;
            coordinates.FindPropertyRelative("_forward").intValue = (int)Axis.PositiveY;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            Realm realm = setup.Realm;
            realm.Update();

            ReferenceFrame frame = realm.ReferenceFrame;
            Assert.That(frame.FollowedGhost, Is.Null);
            Assert.That(frame.HasPosition, Is.False);
            Assert.That(frame.IsReferenceAvailable, Is.False);
            Assert.That(realm.Query().Count, Is.EqualTo(2));
            Assert.That(realm.TryGetGhost(new Key("world", TestKind, "ego"), out IGhost ego), Is.True);
            Assert.That(realm.TryGetGhost(new Key("world", TestKind, "traffic"), out IGhost traffic), Is.True);
            Ghost trafficRoot = (Ghost)traffic;
            Assert.That(trafficRoot.GetComponent<Spatial>().IsInRange, Is.False);
            Assert.That(trafficRoot.GetComponentInChildren<View>(true), Is.Null);

            frame.FollowedGhost = ego.Key;
            realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.True);
            Assert.That(Vector3.Distance(trafficRoot.transform.position, new Vector3(5, 6, -13.25f)), Is.LessThan(0.0001f));
            View view = trafficRoot.GetComponentInChildren<View>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(frame.TryToUnityPosition(frame.Position + new Double3(0, 0, 1), out Vector3 up), Is.True);
            Assert.That(Vector3.Distance(up, new Vector3(5, 7, 7)), Is.LessThan(0.0001f));
            Assert.That(frame.TryToUnityPosition(frame.Position + new Double3(101, 0, 0), out Vector3 outside), Is.False);

            frame.FollowedGhost = traffic.Key;
            realm.Update();
            Assert.That(realm.ReferenceFrame, Is.SameAs(frame));
            Assert.That(Vector3.Distance(trafficRoot.transform.position, frame.UnityPosition), Is.LessThan(0.0001f));
            Assert.That(trafficRoot.GetComponentInChildren<View>(true), Is.SameAs(view));

            setup.StopRealm();
            setup.StartRealm();
            setup.Realm.Update();
            Assert.That(setup.Realm.ReferenceFrame.FollowedGhost, Is.Null);
            Assert.That(setup.Realm.ReferenceFrame.HasPosition, Is.False);
        }

        /// <summary>
        /// A nonempty authored target still requires a complete identity and can start after its missing fields are supplied.
        /// </summary>
        [Test]
        public void SerializedReferenceFrame_ConfiguredTargetRequiresAnchorAndKind()
        {
            RealmSetup setup = CreateRealm();
            SerializedObject serialized = new SerializedObject(setup);
            serialized.FindProperty("_useReferenceFrame").boolValue = true;
            serialized.FindProperty("_followGhost").boolValue = true;
            serialized.FindProperty("_referenceEntityId").stringValue = "ego";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.gameObject.SetActive(true);
            Assert.That(Assert.Throws<InvalidOperationException>(() => setup.StartRealm()).Message,
                Does.Contain("anchor ID and kind"));
            Assert.That(setup.Realm, Is.Null);

            serialized.FindProperty("_referenceAnchorId").stringValue = "world";
            serialized.FindProperty("_referenceKind._id").stringValue = TestKind.Id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setup.StartRealm();
            Assert.That(setup.Realm.ReferenceFrame.FollowedGhost, Is.EqualTo(new Key("world", TestKind, "ego")));
            Assert.That(setup.Realm.ReferenceFrame.HasPosition, Is.False);
        }

        /// <summary>
        /// Opening diagnostics and the overlay does not advance an explicitly managed Realm or construct scene objects.
        /// </summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
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
        /// Ghost authoring owns trait visibility while serialized settings and enabled states remain editable with Undo.
        /// </summary>
        /// <returns>An iterator that waits for delayed editor visibility updates after component changes.</returns>
        [UnityTest]
        public IEnumerator GhostAuthoring_HidesOwnedTraitsAndKeepsSettingsEditable()
        {
            GameObject root = new GameObject("ghost authoring");
            _objects.Add(root);
            root.SetActive(false);
            Spatial spatial = root.AddComponent<Spatial>();
            Smoothing smoothing = root.AddComponent<Smoothing>();
            Prediction prediction = root.AddComponent<Prediction>();
            InspectorTrait custom = root.AddComponent<InspectorTrait>();
            smoothing.enabled = false;
            prediction.enabled = false;
            custom.enabled = false;
            smoothing.hideFlags = HideFlags.DontUnloadUnusedAsset;
            yield return WaitForTraitVisibility(root, false);

            InspectorGhost ghost = root.AddComponent<InspectorGhost>();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(ghost);
                AssertGhostAuthoringMetadata(ghost);
                yield return WaitForTraitVisibility(root, true);
                Assert.That(ghost.Traits, Is.EquivalentTo(new Trait[] { smoothing, prediction, custom }));
                Assert.That(spatial.hideFlags & HideFlags.HideInInspector, Is.EqualTo(HideFlags.None));
                Assert.That(smoothing.hideFlags & HideFlags.DontUnloadUnusedAsset, Is.EqualTo(HideFlags.DontUnloadUnusedAsset));
                AssertTraitSettingsSupportUndo(smoothing, prediction, custom);

                Undo.DestroyObjectImmediate(custom);
                Assert.That(ghost.Traits, Is.EquivalentTo(new Trait[] { smoothing, prediction }));
                custom = Undo.AddComponent<InspectorTrait>(root);
                custom.enabled = false;
                yield return WaitForTraitVisibility(root, true);
                Assert.That(ghost.Traits, Has.Member(custom));
                Assert.That(custom.enabled, Is.False);

                UnityEngine.Object.DestroyImmediate(inspector);
                inspector = null;
                Undo.DestroyObjectImmediate(ghost);
                yield return WaitForTraitVisibility(root, false);
                Assert.That(smoothing.hideFlags, Is.EqualTo(HideFlags.DontUnloadUnusedAsset));

                ghost = Undo.AddComponent<InspectorGhost>(root);
                yield return WaitForTraitVisibility(root, true);
                Assert.That(ghost.Traits, Is.EquivalentTo(new Trait[] { smoothing, prediction, custom }));
                Assert.That(smoothing.hideFlags, Is.EqualTo(HideFlags.DontUnloadUnusedAsset | HideFlags.HideInInspector));
                Assert.That(spatial.hideFlags & HideFlags.HideInInspector, Is.EqualTo(HideFlags.None));
                Assert.That(smoothing.enabled, Is.True);
                Assert.That(prediction.enabled, Is.True);
                Assert.That(custom.enabled, Is.False);
            }
            finally
            {
                if (inspector != null)
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                Undo.ClearUndo(root);
                foreach (Component component in root.GetComponents<Component>())
                {
                    Undo.ClearUndo(component);
                }
            }
        }

        /// <summary>
        /// Hidden built-in traits retain serialized settings and enabled states when an authored Ghost prefab is saved and reopened.
        /// </summary>
        [Test]
        public void GhostAuthoring_PersistsTraitSettingsInPrefabs()
        {
            GameObject root = new GameObject("ghost trait prefab");
            _objects.Add(root);
            root.SetActive(false);
            Ghost ghost = root.AddComponent<Ghost>();
            Smoothing smoothing = root.AddComponent<Smoothing>();
            Prediction prediction = root.AddComponent<Prediction>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/EmasTraitAuthoring.prefab");
            GameObject contents = null;
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(ghost);
                SetSerializedTraitSettings(smoothing, prediction, false);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);
                UnityEngine.Object.DestroyImmediate(inspector);
                inspector = null;

                contents = PrefabUtility.LoadPrefabContents(path);
                Ghost restored = contents.GetComponent<Ghost>();
                Smoothing restoredSmoothing = contents.GetComponent<Smoothing>();
                Prediction restoredPrediction = contents.GetComponent<Prediction>();
                inspector = UnityEditor.Editor.CreateEditor(restored);
                Assert.That(restored.Traits, Is.EquivalentTo(new Trait[] { restoredSmoothing, restoredPrediction }));
                Assert.That(restoredSmoothing.PositionHalfLife, Is.EqualTo(0.15f));
                Assert.That(restoredSmoothing.RotationHalfLife, Is.EqualTo(0.03f));
                Assert.That(restoredPrediction.MaximumExtrapolation, Is.EqualTo(0.2f));
                Assert.That(restoredSmoothing.enabled, Is.False);
                Assert.That(restoredPrediction.enabled, Is.False);
                Assert.That(restoredSmoothing.hideFlags & HideFlags.HideInInspector, Is.EqualTo(HideFlags.HideInInspector));
                Assert.That(restoredPrediction.hideFlags & HideFlags.HideInInspector, Is.EqualTo(HideFlags.HideInInspector));
            }
            finally
            {
                if (inspector != null)
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                if (contents != null)
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                Undo.ClearUndo(smoothing);
                Undo.ClearUndo(prediction);
                AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>Waits for Unity's delayed editor callbacks to apply the observable component visibility policy.</summary>
        /// <param name="root">The GameObject whose trait cards are checked.</param>
        /// <param name="hidden">Whether every trait should be hidden from the ordinary component Inspector.</param>
        /// <returns>An iterator that fails if visibility does not settle within five seconds.</returns>
        private static IEnumerator WaitForTraitVisibility(GameObject root, bool hidden)
        {
            double deadline = EditorApplication.timeSinceStartup + 5.0;
            while (true)
            {
                bool ready = true;
                foreach (Trait trait in root.GetComponents<Trait>())
                {
                    ready &= ((trait.hideFlags & HideFlags.HideInInspector) != 0) == hidden;
                }

                if (ready)
                {
                    yield break;
                }

                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline),
                    "Trait component visibility did not follow its current Ghost ownership.");
                yield return null;
            }
        }

        /// <summary>Checks that authored Ghost fields remain visible while runtime-owned metadata stays hidden.</summary>
        /// <param name="ghost">The application-defined Ghost inspected through Unity serialization.</param>
        private static void AssertGhostAuthoringMetadata(Ghost ghost)
        {
            using (SerializedObject serialized = new SerializedObject(ghost))
            {
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
        }

        /// <summary>Edits hidden traits through the same serialized controls used by the Ghost Inspector.</summary>
        /// <param name="smoothing">The trait receiving position and rotation half-lives.</param>
        /// <param name="prediction">The trait receiving its extrapolation horizon.</param>
        /// <param name="enabled">The enabled state authored for both traits.</param>
        private static void SetSerializedTraitSettings(Smoothing smoothing, Prediction prediction, bool enabled)
        {
            using (SerializedObject settings = new SerializedObject(smoothing))
            {
                settings.FindProperty("_positionHalfLife").floatValue = 0.15f;
                settings.FindProperty("_rotationHalfLife").floatValue = 0.03f;
                settings.FindProperty("m_Enabled").boolValue = enabled;
                settings.ApplyModifiedProperties();
            }

            using (SerializedObject settings = new SerializedObject(prediction))
            {
                settings.FindProperty("_maximumExtrapolation").floatValue = 0.2f;
                settings.FindProperty("m_Enabled").boolValue = enabled;
                settings.ApplyModifiedProperties();
            }
        }

        /// <summary>Verifies that one serialized edit group restores built-in and application trait settings and toggles.</summary>
        /// <param name="smoothing">The initially disabled smoothing trait with default settings.</param>
        /// <param name="prediction">The initially disabled prediction trait with default settings.</param>
        /// <param name="custom">The initially disabled application trait with its default value.</param>
        private static void AssertTraitSettingsSupportUndo(Smoothing smoothing, Prediction prediction, InspectorTrait custom)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                SetSerializedTraitSettings(smoothing, prediction, true);
                using (SerializedObject settings = new SerializedObject(custom))
                {
                    settings.FindProperty("_customValue").intValue = 23;
                    settings.FindProperty("m_Enabled").boolValue = true;
                    settings.ApplyModifiedProperties();
                }

                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Undo.PerformUndo();
                Assert.That(smoothing.PositionHalfLife, Is.EqualTo(0.08f));
                Assert.That(smoothing.RotationHalfLife, Is.Zero);
                Assert.That(prediction.MaximumExtrapolation, Is.EqualTo(0.15f));
                Assert.That(custom.Value, Is.EqualTo(7));
                Assert.That(smoothing.enabled, Is.False);
                Assert.That(prediction.enabled, Is.False);
                Assert.That(custom.enabled, Is.False);

                Undo.PerformRedo();
                Assert.That(smoothing.PositionHalfLife, Is.EqualTo(0.15f));
                Assert.That(smoothing.RotationHalfLife, Is.EqualTo(0.03f));
                Assert.That(prediction.MaximumExtrapolation, Is.EqualTo(0.2f));
                Assert.That(custom.Value, Is.EqualTo(23));
                Assert.That(smoothing.enabled, Is.True);
                Assert.That(prediction.enabled, Is.True);
                Assert.That(custom.enabled, Is.True);
            }
            finally
            {
                Undo.ClearUndo(smoothing);
                Undo.ClearUndo(prediction);
                Undo.ClearUndo(custom);
            }
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
            /// <returns>The detector created by the configured test factory.</returns>
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

        /// <summary>Represents an application-owned trait with an ordinary serialized setting.</summary>
        private sealed class InspectorTrait : Trait<int>
        {
            [SerializeField]
            private int _customValue = 7;

            /// <summary>Gets the setting authored through Unity serialization.</summary>
            /// <value>The application value retained independently of the trait's enabled state.</value>
            public int Value => _customValue;

            /// <summary>Applies an application value without depending on editor visibility.</summary>
            /// <param name="data">The new application value.</param>
            public override void Apply(int data)
            {
                _customValue = data;
            }
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
                ego.GetComponent<Spatial>().SetCartesianPosition(new Double3(1000000000.125, 0.0, 1000000000.375));
                SpatialGhost traffic = GetOrCreate<SpatialGhost>("traffic", TestKind);
                traffic.GetComponent<Spatial>().SetCartesianPosition(new Double3(1000000020.375, 0.0, 1000000000.375));
            }
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class SpatialGhost : Ghost
        {
        }
    }
}
