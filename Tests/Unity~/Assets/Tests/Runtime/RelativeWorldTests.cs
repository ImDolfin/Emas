using System;
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
        /// Advancing geodetic poses keeps the followed car at the Unity origin while the target view moves relative to it.
        /// </summary>
        [UnityTest]
        public IEnumerator RelativeWorld_ProjectsTargetAroundMovingReference()
        {
            const string path = "Assets/Samples/RelativeWorld/RelativeWorld.unity";
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(path);
            GameObject host = Array.Find(_scene.GetRootGameObjects(),
                root => root.GetComponent<Emas.RelativeWorld.RelativeWorld>() != null);
            Assert.That(host, Is.Not.Null);
            Emas.RelativeWorld.RelativeWorld demo = host.GetComponent<Emas.RelativeWorld.RelativeWorld>();
            Realm realm = demo.Realm;
            Assert.That(realm, Is.Not.Null);

            IGhost originGhost;
            IGhost targetGhost;
            Assert.That(realm.TryGetGhost(new Key("relative-world", CarKind, "origin"), out originGhost), Is.True);
            Assert.That(realm.TryGetGhost(new Key("relative-world", CarKind, "target"), out targetGhost), Is.True);
            Ghost origin = (Ghost)originGhost;
            Ghost target = (Ghost)targetGhost;
            Spatial originSpatial = origin.GetComponent<Spatial>();
            Spatial targetSpatial = target.GetComponent<Spatial>();
            Assert.That(originSpatial, Is.Not.Null);
            Assert.That(targetSpatial, Is.Not.Null);
            Assert.That(targetSpatial.Position.X, Is.InRange(4.5, 5.2));
            Assert.That(targetSpatial.Position.Y, Is.InRange(1.0, 1.3));
            Assert.That(targetSpatial.Position.Z, Is.InRange(17.3, 18.3));
            Assert.That(realm.ReferenceFrame.FollowedGhost, Is.EqualTo(originGhost.Key));
            AssertOriginPose(origin);
            AssertProjectedTarget(realm.ReferenceFrame, target, targetSpatial);
            View targetView = target.GetComponentInChildren<View>();
            Assert.That(targetView, Is.Not.Null);
            Double3 previousOrigin = originSpatial.Position;
            Vector3 previousTarget = target.transform.position;

            demo.Advance(5.0);

            Assert.That(Double3.Distance(originSpatial.Position, previousOrigin), Is.GreaterThan(0.01));
            AssertOriginPose(origin);
            AssertProjectedTarget(realm.ReferenceFrame, target, targetSpatial);
            Assert.That(Vector3.Distance(target.transform.position, previousTarget), Is.GreaterThan(0.01f));
            Assert.That(target.GetComponentInChildren<View>(), Is.SameAs(targetView));
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
            Assert.That(Quaternion.Angle(target.transform.rotation, frame.ToUnityRotation(spatial.Rotation)),
                Is.LessThan(0.01f));
        }
    }
}
