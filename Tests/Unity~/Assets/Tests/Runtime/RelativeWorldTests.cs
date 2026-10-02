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
        /// Stationary roadside cars pass a steadily moving origin on alternating sides, then leave the population.
        /// </summary>
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

            setup.gameObject.SetActive(false);
            Assert.That(realm.Query().Count, Is.Zero);
            setup.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.That(setup.Realm, Is.Not.SameAs(realm));
            Assert.That(setup.Realm.Query().OfKind(CarKind).Count, Is.EqualTo(2));
            Assert.That(Car(setup.Realm, "parked-0").transform.position.z, Is.EqualTo(24f).Within(0.001f));
        }

        /// <summary>Road markings follow actual reference travel, and large time jumps keep a bounded, level population.</summary>
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

        /// <summary>The bird and its feet follow the moving reference, with module-driven detach preserving the release pose and reattach retaining identities.</summary>
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

        private IEnumerator Load()
        {
            const string path = "Assets/Samples/RelativeWorld/RelativeWorld.unity";
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(path);
            yield return null;
            yield return null;
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
