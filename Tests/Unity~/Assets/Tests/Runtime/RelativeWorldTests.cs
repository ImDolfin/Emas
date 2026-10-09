using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests.Samples
{
    /// <summary>
    /// Verifies the moving-reference behavior demonstrated by the imported geodetic sample.
    /// </summary>
    public sealed class RelativeWorldTests
    {
        private static readonly Kind CarKind = new Kind("relative.car");
        private Scene _scene;
        private float _timeScale;

        /// <summary>
        /// Leaves sample advancement under test control.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        /// <summary>
        /// Unloads the sample and its owned realm after the scenario.
        /// </summary>
        /// <returns>An iterator that waits for Unity to finish releasing the scenario assets.</returns>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }

            Time.timeScale = _timeScale;
        }

        /// <summary>
        /// Stationary roadside cars pass on alternating sides; changing driving speed preserves distance and supplies matching velocity.
        /// </summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator RelativeWorld_PassesStationaryCarsOnBothSides()
        {
            yield return Load();
            RealmSetup setup = Find<RealmSetup>();
            Emas.RelativeWorld.GeoSource source = Find<Emas.RelativeWorld.GeoSource>();
            Realm realm = setup.Realm;
            Ghost origin = Car(realm, "origin");
            Ghost parked = Car(realm, "parked-0");
            Spatial originSpatial = origin.GetComponent<Spatial>();
            Spatial parkedSpatial = parked.GetComponent<Spatial>();
            Double3 parkedPosition = parkedSpatial.Position;
            Double3 initialOrigin = originSpatial.Position;
            View parkedView = parked.GetComponentInChildren<View>();
            Assert.That(parkedView, Is.Not.Null);
            Assert.That(parked.transform.position.x, Is.EqualTo(-6f).Within(0.001f));
            Assert.That(parked.transform.position.z, Is.EqualTo(24f).Within(0.001f));
            Assert.That(parked.transform.position.y, Is.EqualTo(0f).Within(0.001f));
            AssertOriginPose(origin);

            source.Advance(5.0);
            realm.Update();
            AssertOriginPose(origin);
            Assert.That(Double3.Distance(initialOrigin, originSpatial.Position), Is.EqualTo(40.0).Within(0.001));
            Assert.That(Double3.Distance(parkedSpatial.Position, parkedPosition), Is.LessThan(0.000001));
            Assert.That(parked.transform.position.z, Is.EqualTo(-16f).Within(0.001f));
            Assert.That(parked.transform.position.x, Is.EqualTo(-6f).Within(0.001f));
            AssertProjectedTarget(realm.ReferenceFrame, parked, parkedSpatial);
            Assert.That(parked.GetComponentInChildren<View>(), Is.SameAs(parkedView));

            source.Advance(1.0);
            realm.Update();
            Assert.That(realm.TryGetGhost(new Key("relative-world", CarKind, "parked-0"), out IGhost removed), Is.False);
            Assert.That(realm.Query().OfKind(CarKind).Count, Is.EqualTo(1));
            source.Advance(3.0);
            realm.Update();
            Ghost right = Car(realm, "parked-1");
            Assert.That(right.transform.position.x, Is.EqualTo(3f).Within(0.001f));
            Assert.That(right.transform.position.z, Is.EqualTo(32f).Within(0.001f));
            Double3 rightPosition = right.GetComponent<Spatial>().Position;
            source.Advance(5.0);
            realm.Update();
            Assert.That(right.transform.position.z, Is.EqualTo(-8f).Within(0.001f));
            Assert.That(Double3.Distance(right.GetComponent<Spatial>().Position, rightPosition), Is.LessThan(0.000001));
            AssertOriginPose(origin);

            Double3 beforeSpeedChange = originSpatial.Position;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => source.SpeedKilometersPerHour = -1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => source.SpeedKilometersPerHour = float.NaN);
            source.SpeedKilometersPerHour = 360;
            realm.Update();
            Assert.That(originSpatial.Position, Is.EqualTo(beforeSpeedChange), "Changing speed must not reinterpret past travel.");
            source.Advance(0.25);
            realm.Update();
            Assert.That(Double3.Distance(beforeSpeedChange, originSpatial.Position), Is.EqualTo(25).Within(0.001));
            Assert.That(Double3.Distance(origin.GetRequired<Prediction>().Velocity, default(Double3)), Is.EqualTo(100).Within(0.001));
            Double3 stoppedPosition = originSpatial.Position;
            source.SpeedKilometersPerHour = 0;
            source.Advance(0.25);
            realm.Update();
            Assert.That(originSpatial.Position, Is.EqualTo(stoppedPosition));
            Assert.That(origin.GetRequired<Prediction>().Velocity, Is.EqualTo(default(Double3)));
            source.SpeedKilometersPerHour = 28.8f;

            setup.gameObject.SetActive(false);
            Assert.That(realm.Query().Count, Is.Zero);
            setup.gameObject.SetActive(true);
            yield return null;
            yield return null;
            UseDirectPresentation(setup.Realm);
            Assert.That(setup.Realm, Is.Not.SameAs(realm));
            Assert.That(setup.Realm.Query().OfKind(CarKind).Count, Is.EqualTo(2));
            Assert.That(Car(setup.Realm, "parked-0").transform.position.z, Is.EqualTo(24f).Within(0.001f));
        }

        /// <summary>Road markings follow actual reference travel, and large time jumps keep a bounded, level population.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator RelativeWorld_RoadTracksTravelWithoutAccumulatingPassedCars()
        {
            yield return Load();
            RealmSetup setup = Find<RealmSetup>();
            Emas.RelativeWorld.GeoSource source = Find<Emas.RelativeWorld.GeoSource>();
            Emas.RelativeWorld.RoadMotion road = Find<Emas.RelativeWorld.RoadMotion>();
            Transform marking = road.transform.Find("Distance Marker 0 m");
            Assert.That(marking, Is.Not.Null);
            source.Advance(0.25);
            setup.Realm.Update();
            yield return null;
            Assert.That(marking.localPosition.z, Is.EqualTo(-2f).Within(0.001f));
            source.Advance(3600.0);
            setup.Realm.Update();
            Assert.That(setup.Realm.Query().OfKind(CarKind).Count, Is.InRange(1, 2));
            Assert.That(setup.Realm.TryGetGhost(new Key("relative-world", CarKind, "parked-0"), out IGhost removed), Is.False);
            AssertOriginPose(Car(setup.Realm, "origin"));
            foreach (IGhost ghost in setup.Realm.Query().OfKind(CarKind))
            {
                Assert.That(((Ghost)ghost).transform.position.y,
                    Is.EqualTo(0f).Within(0.001f));
            }
        }

        /// <summary>The bird and its feet follow the moving reference, with trait-driven detach preserving the release pose and reattach retaining identities.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator RelativeWorld_BirdAndFeetMoveTogetherAndDetachToWorldPoses()
        {
            yield return Load();
            Realm realm = Find<RealmSetup>().Realm;
            Emas.RelativeWorld.GeoSource source = Find<Emas.RelativeWorld.GeoSource>();
            Assert.That(realm.TryGetGhost(new Key("relative-world", Emas.RelativeWorld.GeoSource.BirdKind, "bird"),
                out IGhost entity), Is.True);
            Ghost bird = (Ghost)entity;
            Spatial spatial = bird.GetComponent<Spatial>();
            View view = bird.GetComponentInChildren<View>();
            Ghost leftFoot = Foot(realm, "bird-left-foot");
            Ghost rightFoot = Foot(realm, "bird-right-foot");
            View leftView = leftFoot.GetComponentInChildren<View>();
            View rightView = rightFoot.GetComponentInChildren<View>();
            Assert.That(leftView, Is.Not.Null);
            Assert.That(rightView, Is.Not.Null);
            AssertFeetAttached(bird, leftFoot, rightFoot);
            Assert.That(view, Is.Not.Null);
            Assert.That(Vector3.Distance(bird.transform.position, new Vector3(4f, 3.2f, 0f)), Is.LessThan(0.001f));
            Quaternion heading = bird.transform.rotation;
            Double3 worldPosition = spatial.Position;
            source.Advance(2.0);
            realm.Update();
            Assert.That(Vector3.Distance(bird.transform.position, new Vector3(0f, 3.2f, 4f)), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(heading, bird.transform.rotation), Is.EqualTo(90f).Within(0.01f));
            Assert.That(Vector3.Dot(bird.transform.forward, Vector3.left), Is.GreaterThan(0.999f));
            AssertProjectedTarget(realm.ReferenceFrame, bird, spatial);
            AssertFeetAttached(bird, leftFoot, rightFoot);
            AssertOriginPose(Car(realm, "origin"));
            source.Advance(6.0);
            realm.Update();
            Assert.That(Vector3.Distance(bird.transform.position, new Vector3(4f, 3.2f, 0f)), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(heading, bird.transform.rotation), Is.LessThan(0.01f));
            Assert.That(Double3.Distance(spatial.Position, worldPosition), Is.EqualTo(64.0).Within(0.001));
            Assert.That(bird.GetComponentInChildren<View>(), Is.SameAs(view));
            AssertFeetAttached(bird, leftFoot, rightFoot);

            Vector3 releasePosition = leftFoot.transform.position;
            source.SetBirdFeetAttached(false);
            realm.Update();
            Spatial leftSpatial = leftFoot.GetComponent<Spatial>();
            Spatial rightSpatial = rightFoot.GetComponent<Spatial>();
            Assert.That(leftSpatial.AttachedTo, Is.Null);
            Assert.That(rightSpatial.AttachedTo, Is.Null);
            Assert.That(Vector3.Distance(leftFoot.transform.position, releasePosition), Is.LessThan(0.001f));
            Double3 releasedWorldPosition = leftSpatial.Position;
            AssertProjectedTarget(realm.ReferenceFrame, leftFoot, leftSpatial);
            AssertProjectedTarget(realm.ReferenceFrame, rightFoot, rightSpatial);

            source.Advance(1.0);
            realm.Update();
            Assert.That(leftSpatial.Position, Is.EqualTo(releasedWorldPosition));
            Assert.That(Vector3.Distance(leftFoot.transform.position, releasePosition), Is.EqualTo(8f).Within(0.001f));
            Assert.That(Vector3.Distance(leftFoot.transform.position, bird.transform.position), Is.GreaterThan(2f));
            AssertProjectedTarget(realm.ReferenceFrame, leftFoot, leftSpatial);
            source.SetBirdFeetAttached(true);
            realm.Update();
            Assert.That(Foot(realm, "bird-left-foot"), Is.SameAs(leftFoot));
            Assert.That(Foot(realm, "bird-right-foot"), Is.SameAs(rightFoot));
            Assert.That(leftFoot.GetComponentInChildren<View>(), Is.SameAs(leftView));
            Assert.That(rightFoot.GetComponentInChildren<View>(), Is.SameAs(rightView));
            AssertFeetAttached(bird, leftFoot, rightFoot);
        }

        /// <summary>Authored prediction and smoothing handle metre-scale noise at 360 km/h, preserve source identity through packet gaps and recover from control changes.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator RelativeWorld_PredictsAndSmoothsDelayedNoisyPackets()
        {
            yield return Load(true);
            Realm realm = Find<RealmSetup>().Realm;
            Emas.RelativeWorld.GeoSource source = Find<Emas.RelativeWorld.GeoSource>();
            Assert.That(source.SimulateJitterAndDelay, Is.True);
            Assert.That(source.SpeedKilometersPerHour, Is.EqualTo(360));
            double started = Time.realtimeSinceStartupAsDouble;
            double previousTime = started;
            double elapsed = 0;
            Timestamp? previousSample = null;
            Double3 previousParked = default(Double3);
            string previousParkedId = null;
            Ghost firstOrigin = null;
            bool sawMeasurementJitter = false;
            bool sawDelayedObservation = false;
            bool sawProcessedPresentation = false;
            do
            {
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                double step = now - previousTime;
                elapsed += step;
                previousTime = now;
                source.Advance(step);
                realm.Update();
                if (!realm.TryGetGhost(new Key("relative-world", CarKind, "origin"), out IGhost entity))
                {
                    continue;
                }

                Ghost origin = (Ghost)entity;
                Ghost parked = null;
                foreach (IGhost car in realm.Query().OfKind(CarKind))
                {
                    if (car.Key.EntityId != "origin")
                    {
                        parked = (Ghost)car;
                        break;
                    }
                }
                Ghost bird = (Ghost)realm.Query().OfKind(Emas.RelativeWorld.GeoSource.BirdKind).FirstOrDefault();
                Assert.That(bird, Is.Not.Null);
                Spatial spatial = origin.GetRequired<Spatial>();
                Spatial parkedSpatial = parked == null ? null : parked.GetRequired<Spatial>();
                Assert.That(spatial.PositionTime.HasValue, Is.True);
                Timestamp sample = spatial.PositionTime.Value;
                sawDelayedObservation |= elapsed - sample.ElapsedSince(new Timestamp(0, 0)) > 0.025;
                Assert.That(origin.GetRequired<Prediction>().HasVelocity, Is.True);
                Assert.That(Double3.Distance(origin.GetRequired<Prediction>().Velocity, default(Double3)), Is.EqualTo(100).Within(0.001));
                Assert.That(origin.GetRequired<Smoothing>().enabled, Is.True);
                Assert.That(origin.GetRequired<Smoothing>().RotationHalfLife, Is.GreaterThan(0));
                Assert.That(bird.GetRequired<Prediction>().HasVelocity, Is.True);
                AssertOriginPose(origin);
                AssertFeetAttached(bird, Foot(realm, "bird-left-foot"), Foot(realm, "bird-right-foot"));
                Assert.That(realm.ReferenceFrame.TryToUnityPosition(bird.GetRequired<Spatial>().Position,
                    out Vector3 rawBirdPosition), Is.True);
                sawProcessedPresentation |= Vector3.Distance(rawBirdPosition, bird.transform.position) > 0.001f;
                if (previousSample.HasValue)
                {
                    Assert.That(origin, Is.SameAs(firstOrigin), "Packet gaps must retain membership and the Ghost.");
                    Assert.That(sample.CompareTo(previousSample.Value), Is.GreaterThanOrEqualTo(0));
                    if (!sample.Equals(previousSample.Value) && parked != null && parked.Key.EntityId == previousParkedId)
                    {
                        double displacement = Double3.Distance(previousParked, parkedSpatial.Position);
                        Assert.That(displacement, Is.LessThanOrEqualTo(4.01), "Two bounded 2 m errors cannot move a stationary car by more than 4 m.");
                        sawMeasurementJitter |= displacement > 1.0;
                    }
                }

                firstOrigin = origin;
                previousSample = sample;
                previousParkedId = parked == null ? null : parked.Key.EntityId;
                previousParked = parkedSpatial == null ? default(Double3) : parkedSpatial.Position;
            }
            while (elapsed < 1.2);

            Assert.That(firstOrigin, Is.Not.Null);
            Assert.That(sawDelayedObservation, Is.True, "Capture timestamps must survive delivery delay.");
            Assert.That(sawMeasurementJitter, Is.True, "The parked cars must receive metre-scale measurement noise.");
            Assert.That(sawProcessedPresentation, Is.True, "The saved behaviors process raw SDK observations.");
            Timestamp heldSample = firstOrigin.GetRequired<Spatial>().PositionTime.Value;
            Double3 beforeHold = realm.ReferenceFrame.Position;
            // Hold the SDK explicitly, so this check also works when rendering is slower than the packet rate.
            yield return null;
            realm.Update();
            Assert.That(firstOrigin.GetRequired<Spatial>().PositionTime.Value, Is.EqualTo(heldSample));
            Assert.That(Double3.Distance(beforeHold, realm.ReferenceFrame.Position), Is.GreaterThan(0.00001),
                "Presentation continues between packets without refreshing the raw timestamp.");
            source.SimulateJitterAndDelay = false;
            realm.Update();
            Assert.That(firstOrigin.GetRequired<Spatial>().PositionTime.Value.ElapsedSince(new Timestamp(0, 0)),
                Is.EqualTo(elapsed).Within(0.000001), "Clean mode immediately exposes the latest observation.");

            Ghost left = Foot(realm, "bird-left-foot");
            Timestamp beforeDetach = left.GetRequired<Spatial>().PositionTime.Value;
            source.SetBirdFeetAttached(false);
            realm.Update();
            Assert.That(left.GetRequired<Spatial>().AttachedTo, Is.Null);
            Assert.That(left.GetRequired<Spatial>().PositionTime.Value.CompareTo(beforeDetach), Is.GreaterThan(0));
            Assert.That(left.GetRequired<Prediction>().HasVelocity, Is.True);
            Assert.That(left.GetRequired<Prediction>().Velocity, Is.EqualTo(default(Double3)),
                "A paused manual detach must replace the attached bird velocity immediately.");
            source.SetBirdFeetAttached(true);
            realm.Update();
            Ghost attachedBird = (Ghost)realm.Query().OfKind(Emas.RelativeWorld.GeoSource.BirdKind).FirstOrDefault();
            AssertFeetAttached(attachedBird, left, Foot(realm, "bird-right-foot"));

            source.enabled = false;
            source.enabled = true;
            realm.Update();
            Assert.That(Find<RealmSetup>().Realm, Is.SameAs(realm));
            Ghost restarted = Car(realm, "origin");
            Spatial restartedSpatial = restarted.GetRequired<Spatial>();
            Assert.That(restartedSpatial.PositionTime, Is.EqualTo(new Timestamp(0, 0)));
            double predictionRange = source.SpeedKilometersPerHour / 3.6 * restarted.GetRequired<Prediction>().MaximumExtrapolation;
            Assert.That(Double3.Distance(realm.ReferenceFrame.Position, restartedSpatial.Position), Is.LessThan(predictionRange * 0.25),
                "Restarting just the source resets its clock; the origin must not immediately extrapolate by the full horizon.");
        }

        private IEnumerator Load(bool withImpairments = false)
        {
            const string path = "Assets/Samples/RelativeWorld/RelativeWorld.unity";
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(path);
            yield return null;
            yield return null;
            if (!withImpairments)
            {
                Emas.RelativeWorld.GeoSource source = Find<Emas.RelativeWorld.GeoSource>();
                source.SpeedKilometersPerHour = 28.8f;
                source.SimulateJitterAndDelay = false;
                UseDirectPresentation(Find<RealmSetup>().Realm);
                yield return null; // Let RoadMotion capture the initial reference before manually advancing the SDK.
            }
        }

        private static void UseDirectPresentation(Realm realm)
        {
            // These exact mapping scenarios advance SDK time in jumps rather than at the presentation clock's rate.
            foreach (IGhost entity in realm.Ghosts)
            {
                Ghost ghost = (Ghost)entity;
                ghost.GetRequired<Prediction>().enabled = false;
                ghost.GetRequired<Smoothing>().enabled = false;
            }
            realm.Update();
        }

        private T Find<T>() where T : Component
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>();
                if (component != null)
                {
                    return component;
                }
            }
            Assert.Fail("Missing authored sample component: " + typeof(T).Name);
            return null;
        }

        /// <summary>Separate optional motion traits convert the same packet's ENU basis independently of pose-reader order and clear unavailable channels.</summary>
        [Test]
        public void GeoMotionReaders_ApplyOptionalEnuVelocityAndAccelerationThroughRealmTraitUpdates()
        {
            GameObject prefab = new GameObject("optional velocity root");
            prefab.SetActive(false);
            Ghost root = prefab.AddComponent<Ghost>();
            prefab.AddComponent<Emas.RelativeWorld.GeoVelocityTrait>();
            prefab.AddComponent<Emas.RelativeWorld.GeoAccelerationTrait>();
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                using (Realm realm = new Realm())
                {
                    blueprint.Configure(CarKind, root, null, null);
                    realm.RegisterManifestationBlueprint(blueprint);
                    Emas.RelativeWorld.GeoPoseReading reading = new Emas.RelativeWorld.GeoPoseReading(
                        "optional", "Optional motion", CarKind, default, 0, 90, 40, 0, 0, 0);
                    realm.RegisterPresenceInitializer<Ghost>(CarKind, (presence, ghost) =>
                    {
                        ghost.GetRequired<Emas.RelativeWorld.GeoVelocityTrait>().Bind(() => reading);
                        ghost.GetRequired<Emas.RelativeWorld.GeoAccelerationTrait>().Bind(() => reading);
                    });
                    VelocityDetector detector = new VelocityDetector();
                    realm.GetOrCreateAnchor("sdk", detector);
                    Ghost ghost = detector.Publish();
                    Spatial spatial = ghost.GetRequired<Spatial>();
                    GeoPosition position = new GeoPosition(52, 13, 40);
                    spatial.SetGeographicPosition(position);
                    ghost.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.1f;
                    Prediction prediction = ghost.GetRequired<Prediction>();
                    realm.ReferenceFrame = new ReferenceFrame
                    {
                        Space = ReferenceSpace.Geographic,
                        FollowedGhost = ghost.Key
                    };
                    realm.Update();
                    Assert.That(prediction.HasVelocity, Is.False);
                    Assert.That(prediction.HasAcceleration, Is.False);
                    reading = new Emas.RelativeWorld.GeoPoseReading("optional", "Optional motion", CarKind,
                        default, 0, 90, 40, 0, 0, 0, eastNorthUpVelocity: new Double3(1, 2, 3),
                        eastNorthUpAcceleration: new Double3(4, 5, 6));
                    realm.Update();
                    Assert.That(prediction.HasVelocity, Is.True);
                    Assert.That(Double3.Distance(prediction.Velocity, new Double3(-1, 3, 2)), Is.LessThan(1e-12));
                    Assert.That(prediction.HasAcceleration, Is.True);
                    Assert.That(Double3.Distance(prediction.Acceleration, new Double3(-4, 6, 5)), Is.LessThan(1e-12));
                    Assert.That(spatial.Position, Is.EqualTo(position.ToEarthCentered()), "Motion traits do not overwrite pose or use its location implicitly.");
                    prediction.SetEarthCenteredAcceleration(new Double3(7, 8, 9));
                    Assert.That(prediction.Acceleration, Is.EqualTo(new Double3(7, 8, 9)));
                    reading = null;
                    realm.Update();
                    Assert.That(prediction.HasVelocity, Is.False);
                    Assert.That(prediction.HasAcceleration, Is.False);
                    Assert.That(spatial.IsInRange, Is.True);
                    AssertOriginPose(ghost);
                }
            }
            finally
            {
                Object.DestroyImmediate(blueprint);
                Object.DestroyImmediate(prefab);
            }
        }

        private sealed class VelocityDetector : PresenceDetector
        {
            internal Ghost Publish()
            {
                return Detect("optional", CarKind).Root;
            }
        }

        private static Ghost Car(Realm realm, string id)
        {
            Assert.That(realm.TryGetGhost(new Key("relative-world", CarKind, id), out IGhost ghost), Is.True, id);
            return (Ghost)ghost;
        }

        private static Ghost Foot(Realm realm, string id)
        {
            Assert.That(realm.TryGetGhost(new Key("relative-world", Emas.RelativeWorld.GeoSource.BirdFootKind, id),
                out IGhost ghost), Is.True, id);
            return (Ghost)ghost;
        }

        private static void AssertFeetAttached(Ghost bird, Ghost left, Ghost right)
        {
            Ghost[] feet = { left, right };
            for (int index = 0; index < feet.Length; index++)
            {
                Ghost foot = feet[index];
                Assert.That(foot.GetComponent<Spatial>().AttachedTo, Is.EqualTo(bird.Key));
                Vector3 offset = Quaternion.Inverse(bird.transform.rotation) * (foot.transform.position - bird.transform.position);
                Assert.That(Vector3.Distance(offset, new Vector3(index == 0 ? -0.085f : 0.085f, -0.09f, 0.02f)),
                    Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(foot.transform.rotation, bird.transform.rotation), Is.LessThan(0.01f));
                Assert.That(foot.transform.parent, Is.SameAs(bird.transform.parent));
            }
        }

        private static void AssertOriginPose(Ghost origin)
        {
            Assert.That(origin.transform.position.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(Quaternion.Angle(origin.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        private static void AssertProjectedTarget(ReferenceFrame frame, Ghost target, Spatial spatial)
        {
            Assert.That(frame.IsReferenceAvailable, Is.True);
            Assert.That(frame.TryToUnityPosition(spatial.Position, out Vector3 expectedPosition), Is.True);
            Assert.That(Vector3.Distance(target.transform.position, expectedPosition), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(target.transform.rotation, frame.ToUnityRotation(spatial.Rotation, GeoPosition.FromEarthCentered(spatial.Position))),
                Is.LessThan(0.01f));
        }
    }
}
