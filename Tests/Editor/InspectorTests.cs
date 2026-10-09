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
        /// Live serialized tuning preserves continuous presentation, matches public setters and converges without changing SDK observations or attachments.
        /// </summary>
        /// <returns>An iterator that advances moving geographic observations and held prediction samples in Play Mode.</returns>
        [UnityTest]
        public IEnumerator SerializedLiveTuning_MatchesPropertyEditsAndPreservesSharedPresentation()
        {
            yield return new EnterPlayMode();
            using (Realm realm = new Realm())
            {
                LiveTuningDetector source = new LiveTuningDetector();
                realm.ReferenceFrame = new ReferenceFrame
                {
                    Space = ReferenceSpace.Geographic,
                    FollowedGhost = new Key("live tuning", TestKind, "serialized"),
                    UnityPosition = new Vector3(7, 2, -3),
                    UnityRotation = Quaternion.Euler(0, 15, 0)
                };
                realm.GetOrCreateAnchor("live tuning", source);
                double started = Time.realtimeSinceStartupAsDouble;
                for (int index = 0; index < 3; index++)
                {
                    yield return null;
                    source.Publish(Time.realtimeSinceStartupAsDouble - started);
                    AssertLiveTuningUpdate(realm, source, "Moving before tuning");
                }

                // Hold a corrected sample past its cap so normal motion cannot disguise a configuration jump.
                source.Publish(Time.realtimeSinceStartupAsDouble - started, 8, 45);
                AssertLiveTuningUpdate(realm, source, "Corrected observation");
                yield return HoldLiveTuningSample(realm, source, 0.18);
                AssertContinuousLiveTuning(realm, source, () =>
                {
                    EditLiveTuning(source, "_positionHalfLife", 0.1f);
                    EditLiveTuning(source, "_positionHalfLife", 0.4f);
                }, "Coalescing edits restored before projection");
                AssertContinuousLiveTuning(realm, source, () => EditLiveTuning(source, "_positionHalfLife", 0.08f),
                    "Changing positive position half-life");
                yield return HoldLiveTuningSample(realm, source, 0.25 + 8 * 0.08);
                AssertLiveTuningTarget(realm, source, 0.15f, false);

                AssertContinuousLiveTuning(realm, source, () => EditLiveTuning(source, "_maximumExtrapolation", 0.03f),
                    "Reducing the capped prediction horizon");
                yield return HoldLiveTuningSample(realm, source, 0.25 + 8 * 0.08);
                AssertLiveTuningTarget(realm, source, 0.03f, false);
                AssertContinuousLiveTuning(realm, source, () => EditLiveTuning(source, "_rotationHalfLife", 0.06f),
                    "Changing positive rotation half-life");
                yield return HoldLiveTuningSample(realm, source, 0.25 + 8 * 0.06);
                AssertLiveTuningTarget(realm, source, 0.03f);

                // A later corrected packet restores independent position and rotation error before testing direct modes.
                source.Publish(source.ObservedTime.ElapsedSince(default(Timestamp)) + 0.01, 16, 90);
                AssertLiveTuningUpdate(realm, source, "Fresh correction before direct modes");
                AssertContinuousLiveTuning(realm, source, () =>
                {
                    EditLiveTuning(source, "_positionHalfLife", 0);
                    EditLiveTuning(source, "_rotationHalfLife", 0);
                }, "Zero half-lives preserve the displayed pose before settling");
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, 0.03f);
                AssertContinuousLiveTuning(realm, source, () => EditLiveTuning(source, "_maximumExtrapolation", 0),
                    "Zero prediction horizon");
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, 0);

                AssertContinuousLiveTuning(realm, source, () =>
                {
                    EditLiveTuning(source, "_positionHalfLife", 0.4f);
                    EditLiveTuning(source, "_rotationHalfLife", 0.4f);
                    EditLiveTuning(source, "_maximumExtrapolation", 0.25f);
                }, "Restoring positive settings");
                yield return HoldLiveTuningSample(realm, source, 0.3);
                AssertContinuousLiveTuning(realm, source, () => SetLiveTuningEnabled(source, false, false),
                    "Disabling smoothing with correction history");
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, 0.25f);
                AssertContinuousLiveTuning(realm, source, () => SetLiveTuningEnabled(source, true, false),
                    "Disabling prediction");
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, 0);
                AssertContinuousLiveTuning(realm, source, () => SetLiveTuningEnabled(source, true, true),
                    "Re-enabling prediction");
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, 0.25f);

                yield return DragLiveTuningWhileMoving(realm, source);
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, source.Control.GetComponent<Prediction>().MaximumExtrapolation);
                AssertContinuousLiveTuning(realm, source, () => SetLiveTuningEnabled(source, false, true),
                    "Re-enabling smoothing");
                yield return HoldLiveTuningSample(realm, source, 0.35);
                AssertLiveTuningTarget(realm, source, source.Control.GetComponent<Prediction>().MaximumExtrapolation);
            }
        }

        /// <summary>Rejects tuning jumps while allowing the old filter's ordinary progress during the elapsed projection interval.</summary>
        /// <param name="realm">The realm whose followed reference exposes absolute presentation.</param>
        /// <param name="source">The paired source holding an observation past its previous prediction cap.</param>
        /// <param name="edit">The equivalent serialized and public configuration edits.</param>
        /// <param name="context">The configuration change described by assertion failures.</param>
        private static void AssertContinuousLiveTuning(Realm realm, LiveTuningDetector source, Action edit, string context)
        {
            ReferenceFrame frame = realm.ReferenceFrame;
            Double3 position = frame.Position;
            Quaternion rotation = frame.Rotation;
            double previousProjection = source.LastProjectionStarted;
            Smoothing smoothing = source.Control.GetComponent<Smoothing>();
            Prediction prediction = source.Control.GetComponent<Prediction>();
            float positionHalfLife = smoothing.enabled ? smoothing.PositionHalfLife : 0;
            float rotationHalfLife = smoothing.enabled ? smoothing.RotationHalfLife : 0;
            Double3 target = source.ObservedPosition + source.ObservedVelocity * (prediction.enabled ? prediction.MaximumExtrapolation : 0);
            double positionError = Double3.Distance(position, target);
            double rotationError = Quaternion.Angle(rotation, source.ObservedRotation);

            edit();
            AssertLiveTuningUpdate(realm, source, context);
            double elapsed = Time.realtimeSinceStartupAsDouble - previousProjection;
            // Exponential decay cannot advance farther than its initial error times its initial decay rate.
            double positionProgress = positionHalfLife > 0 ? positionError * Math.Log(2) * elapsed / positionHalfLife : 0;
            double rotationProgress = rotationHalfLife > 0 ? rotationError * Math.Log(2) * elapsed / rotationHalfLife : 0;
            Assert.That(Double3.Distance(frame.Position, position), Is.LessThanOrEqualTo(0.01 + positionProgress),
                context + " must not jump the absolute displayed position.");
            Assert.That(Quaternion.Angle(frame.Rotation, rotation), Is.LessThanOrEqualTo(0.1 + rotationProgress),
                context + " must not jump the absolute displayed rotation.");
        }

        /// <summary>Checks convergence to the held observation's requested cap after filter and tuning-transition time has elapsed.</summary>
        /// <param name="realm">The realm whose followed reference exposes absolute presentation.</param>
        /// <param name="source">The source whose original observation remains unchanged.</param>
        /// <param name="horizon">The expected capped prediction age, or zero for disabled prediction.</param>
        /// <param name="checkRotation">Whether the independent rotation channel has also been allowed to settle.</param>
        private static void AssertLiveTuningTarget(Realm realm, LiveTuningDetector source, float horizon, bool checkRotation = true)
        {
            Double3 target = source.ObservedPosition + source.ObservedVelocity * horizon;
            Assert.That(Double3.Distance(realm.ReferenceFrame.Position, target), Is.LessThan(0.1),
                "Live settings must converge to their new prediction behavior instead of retaining the previous offset.");
            if (checkRotation)
            {
                Assert.That(Quaternion.Angle(realm.ReferenceFrame.Rotation, source.ObservedRotation), Is.LessThan(0.1),
                    "Live rotation settings must converge without retaining a permanent presentation offset.");
            }
        }

        /// <summary>Checks that repeated horizon edits cannot freeze a reference receiving fresh 100 metre-per-second observations.</summary>
        /// <param name="realm">The isolated realm advanced once for each new observation.</param>
        /// <param name="source">The paired moving roots with smoothing disabled for this part of the scenario.</param>
        /// <returns>An iterator that keeps the slider moving across at least 120 milliseconds of source motion.</returns>
        private static IEnumerator DragLiveTuningWhileMoving(Realm realm, LiveTuningDetector source)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            double sampleStarted = source.ObservedTime.ElapsedSince(default(Timestamp));
            Double3 position = realm.ReferenceFrame.Position;
            Double3 observed = source.ObservedPosition;
            int index = 0;
            do
            {
                yield return null;
                source.Publish(sampleStarted + Time.realtimeSinceStartupAsDouble - started, 16, 90);
                EditLiveTuning(source, "_maximumExtrapolation", index++ % 2 == 0 ? 0.24f : 0.25f);
                AssertLiveTuningUpdate(realm, source, "Continuously dragging the horizon while moving");
            }
            while (Time.realtimeSinceStartupAsDouble - started < 0.12);

            Assert.That(Double3.Distance(realm.ReferenceFrame.Position, position),
                Is.GreaterThan(Double3.Distance(source.ObservedPosition, observed) * 0.5),
                "Repeated configuration edits must preserve ordinary movement instead of pinning each update to its previous pose.");
        }

        /// <summary>Compares serialized float controls with their corresponding public property setters.</summary>
        /// <param name="source">The paired ghosts receiving identical timestamped observations.</param>
        /// <param name="property">The serialized position, rotation or prediction setting to change.</param>
        /// <param name="value">The new setting applied to both authoring paths.</param>
        private static void EditLiveTuning(LiveTuningDetector source, string property, float value)
        {
            bool prediction = property == "_maximumExtrapolation";
            Trait edited = prediction ? (Trait)source.Edited.GetComponent<Prediction>() : source.Edited.GetComponent<Smoothing>();
            using (SerializedObject settings = new SerializedObject(edited))
            {
                settings.FindProperty(property).floatValue = value;
                settings.ApplyModifiedProperties();
            }

            if (prediction)
            {
                source.Control.GetComponent<Prediction>().MaximumExtrapolation = value;
            }
            else if (property == "_positionHalfLife")
            {
                source.Control.GetComponent<Smoothing>().PositionHalfLife = value;
            }
            else
            {
                source.Control.GetComponent<Smoothing>().RotationHalfLife = value;
            }
        }

        /// <summary>Compares the Inspector enabled toggle with the equivalent public component toggle.</summary>
        /// <param name="source">The paired ghosts being tuned.</param>
        /// <param name="prediction">True for Prediction; false for Smoothing.</param>
        /// <param name="enabled">The enabled state applied to both traits.</param>
        private static void SetLiveTuningEnabled(LiveTuningDetector source, bool prediction, bool enabled)
        {
            Trait edited = prediction ? (Trait)source.Edited.GetComponent<Prediction>() : source.Edited.GetComponent<Smoothing>();
            Trait control = prediction ? (Trait)source.Control.GetComponent<Prediction>() : source.Control.GetComponent<Smoothing>();
            using (SerializedObject settings = new SerializedObject(edited))
            {
                settings.FindProperty("m_Enabled").boolValue = enabled;
                settings.ApplyModifiedProperties();
            }

            control.enabled = enabled;
        }

        /// <summary>Advances one shared projection and checks parity, raw observation preservation and attachment coherence.</summary>
        /// <param name="realm">The isolated realm providing one projection timestamp for both ghosts.</param>
        /// <param name="source">The geographic source and its paired presentation roots.</param>
        /// <param name="context">The consumer action described if an assertion fails.</param>
        private static void AssertLiveTuningUpdate(Realm realm, LiveTuningDetector source, string context)
        {
            source.LastProjectionStarted = Time.realtimeSinceStartupAsDouble;
            realm.Update();
            foreach (Ghost ghost in new[] { source.Edited, source.Control })
            {
                Spatial spatial = ghost.GetComponent<Spatial>();
                Prediction prediction = ghost.GetComponent<Prediction>();
                Assert.That(spatial.Position, Is.EqualTo(source.ObservedPosition), context);
                Assert.That(spatial.Rotation, Is.EqualTo(source.ObservedRotation), context);
                Assert.That(spatial.PositionTime, Is.EqualTo(source.ObservedTime), context);
                Assert.That(spatial.RotationTime, Is.EqualTo(source.ObservedTime), context);
                Assert.That(prediction.Velocity, Is.EqualTo(source.ObservedVelocity), context);
                Assert.That(prediction.HasVelocity, Is.True, context);
            }

            ReferenceFrame frame = realm.ReferenceFrame;
            Assert.That(frame.IsReferenceAvailable, Is.True, context);
            Assert.That(Vector3.Distance(source.Edited.transform.position, frame.UnityPosition), Is.LessThan(0.001f), context);
            Assert.That(Quaternion.Angle(source.Edited.transform.rotation, frame.UnityRotation), Is.LessThan(0.05f), context);
            Assert.That(Double3.Distance(frame.Position, source.ObservedPosition), Is.LessThan(50), context);
            Vector3 attachment = source.Edited.transform.position + source.Edited.transform.rotation * LiveTuningDetector.AttachmentOffset;
            Assert.That(Vector3.Distance(source.Attachment.transform.position, attachment), Is.LessThan(0.001f), context);
            Assert.That(Quaternion.Angle(source.Attachment.transform.rotation, source.Edited.transform.rotation), Is.LessThan(0.05f), context);
            Assert.That(Vector3.Distance(source.Control.transform.position, source.Edited.transform.position), Is.LessThan(0.001f), context);
            Assert.That(Quaternion.Angle(source.Control.transform.rotation, source.Edited.transform.rotation), Is.LessThan(0.05f), context);
        }

        /// <summary>Checks bounded presentation while the SDK holds an unchanged timestamped sample.</summary>
        /// <param name="realm">The isolated realm to advance.</param>
        /// <param name="source">The source whose latest observation remains unchanged.</param>
        /// <param name="seconds">The minimum elapsed real time to let prediction reach its cap.</param>
        /// <returns>An iterator that advances public Realm updates across Unity frames.</returns>
        private static IEnumerator HoldLiveTuningSample(Realm realm, LiveTuningDetector source, double seconds)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            do
            {
                yield return null;
                AssertLiveTuningUpdate(realm, source, "Holding a timestamped observation");
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);
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

        /// <summary>Publishes coincident geographic roots so serialized and property tuning share one projection clock.</summary>
        private sealed class LiveTuningDetector : PresenceDetector
        {
            internal static readonly Vector3 AttachmentOffset = new Vector3(2, 0.5f, -1);
            internal Ghost Edited;
            internal Ghost Control;
            internal Ghost Attachment;
            internal Double3 ObservedPosition;
            internal Double3 ObservedVelocity;
            internal Quaternion ObservedRotation;
            internal Timestamp ObservedTime;
            internal double LastProjectionStarted;
            private double _sampleSeconds = -1;

            /// <summary>Creates the edited origin, its property-controlled twin and a parent-local attached part.</summary>
            protected override void OnStart()
            {
                Edited = CreateMovingGhost("serialized");
                Control = CreateMovingGhost("property");
                Attachment = GetOrCreate<Ghost>("attachment", TestKind);
                Attachment.gameObject.AddComponent<Spatial>().Attach(Edited.Key, AttachmentOffset);
                Publish(0);
            }

            /// <summary>Supplies identical WGS84 observations travelling east at 100 metres per second.</summary>
            /// <param name="seconds">Elapsed SDK time, advanced by at least one microsecond for each new sample.</param>
            /// <param name="correction">An optional positional observation correction in metres.</param>
            /// <param name="headingCorrection">An optional heading correction in degrees.</param>
            internal void Publish(double seconds, double correction = 0, double headingCorrection = 0)
            {
                _sampleSeconds = Math.Max(seconds, _sampleSeconds + 0.000001);
                GeoPosition position = new GeoPosition(0, (100 * _sampleSeconds + correction) / 6378137.0 * 180.0 / Math.PI, 0);
                ObservedPosition = position.ToEarthCentered();
                ObservedTime = Timestamp.FromSeconds(_sampleSeconds);
                foreach (Ghost ghost in new[] { Edited, Control })
                {
                    Spatial spatial = ghost.GetComponent<Spatial>();
                    spatial.SetGeographicPosition(position, ObservedTime);
                    spatial.SetGeographicRotation(30 * _sampleSeconds + headingCorrection, 0, 0, ObservedTime);
                    ghost.GetComponent<Prediction>().SetGeographicVelocity(new Double3(100, 0, 0), position, ObservedTime);
                }

                ObservedRotation = Edited.GetComponent<Spatial>().Rotation;
                ObservedVelocity = Edited.GetComponent<Prediction>().Velocity;
            }

            /// <summary>Authors the same independent position and rotation filters on each comparison root.</summary>
            /// <param name="id">The identity owned by this detector.</param>
            /// <returns>The newly authored moving Ghost.</returns>
            private Ghost CreateMovingGhost(string id)
            {
                Ghost ghost = GetOrCreate<Ghost>(id, TestKind);
                ghost.gameObject.AddComponent<Spatial>();
                Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
                smoothing.PositionHalfLife = 0.4f;
                smoothing.RotationHalfLife = 0.4f;
                ghost.gameObject.AddComponent<Prediction>();
                return ghost;
            }
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class SpatialGhost : Ghost
        {
        }
    }
}
