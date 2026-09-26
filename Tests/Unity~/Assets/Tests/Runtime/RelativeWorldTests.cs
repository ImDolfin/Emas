using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests.Samples
{
    /// <summary>
    /// Runs the imported geodetic relative-world sample with its real detector and entity modules.
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
        /// Releases the sample realm and generated Unity objects.
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
        /// GeoPose updates pass through modules for both cars while the frame follows the origin.
        /// </summary>
        [UnityTest]
        public IEnumerator RelativeWorld_ProjectsOriginAndTargetGeoPoses()
        {
            int defaultAnchorCount = Realm.Default.Anchors.Count;
            const string path = "Assets/Samples/RelativeWorld/RelativeWorld.unity";
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(path);
            Assert.That(_scene.IsValid() && _scene.isLoaded, Is.True);

            GameObject host = Array.Find(_scene.GetRootGameObjects(),
                root => root.GetComponent<Emas.RelativeWorld.RelativeWorld>() != null);
            Assert.That(host, Is.Not.Null);
            Emas.RelativeWorld.RelativeWorld demo = host.GetComponent<Emas.RelativeWorld.RelativeWorld>();
            Realm realm = demo.Realm;
            Assert.That(realm, Is.Not.Null);
            Assert.That(realm.Query().Count, Is.EqualTo(2));

            Key originKey = new Key("relative-world", CarKind, "origin");
            Key targetKey = new Key("relative-world", CarKind, "target");
            Assert.That(realm.TryGetPresence(originKey, out Presence originPresence), Is.True);
            Assert.That(realm.TryGetPresence(targetKey, out Presence targetPresence), Is.True);
            Assert.That(originPresence.IsAvailable, Is.True);
            Assert.That(targetPresence.IsAvailable, Is.True);
            Assert.That(originPresence.Modules.Count, Is.EqualTo(2));
            Assert.That(targetPresence.Modules.Count, Is.EqualTo(2));
            Assert.That(originPresence.TryGetModule<Emas.RelativeWorld.GeoPositionModule>(out var originPositionModule), Is.True);
            Assert.That(originPresence.TryGetModule<Emas.RelativeWorld.GeoOrientationModule>(out var originOrientationModule), Is.True);
            Assert.That(targetPresence.TryGetModule<Emas.RelativeWorld.GeoPositionModule>(out var targetPositionModule), Is.True);
            Assert.That(targetPresence.TryGetModule<Emas.RelativeWorld.GeoOrientationModule>(out var targetOrientationModule), Is.True);
            Assert.That(originPositionModule.Presence, Is.SameAs(originPresence));
            Assert.That(originOrientationModule.Presence, Is.SameAs(originPresence));
            Assert.That(targetPositionModule.Presence, Is.SameAs(targetPresence));
            Assert.That(targetOrientationModule.Presence, Is.SameAs(targetPresence));

            Ghost origin = originPresence.Root;
            Ghost target = targetPresence.Root;
            Spatial originSpatial = origin.GetComponent<Spatial>();
            Spatial targetSpatial = target.GetComponent<Spatial>();
            Assert.That(originSpatial, Is.Not.Null);
            Assert.That(targetSpatial, Is.Not.Null);
            Assert.That(originSpatial.HasPosition && originSpatial.HasRotation, Is.True);
            Assert.That(targetSpatial.HasPosition && targetSpatial.HasRotation, Is.True);
            Assert.That(Math.Abs(originSpatial.Position.X), Is.LessThan(0.00001));
            Assert.That(Math.Abs(originSpatial.Position.Y), Is.LessThan(0.00001));
            Assert.That(Math.Abs(originSpatial.Position.Z), Is.LessThan(0.00001));
            Assert.That(targetSpatial.Position.X, Is.InRange(4.5, 5.2));
            Assert.That(targetSpatial.Position.Y, Is.InRange(1.0, 1.3));
            Assert.That(targetSpatial.Position.Z, Is.InRange(17.3, 18.3));
            Vector3 targetForward = targetSpatial.Rotation * Vector3.forward;
            Vector3 targetRight = targetSpatial.Rotation * Vector3.right;
            Assert.That(targetForward.x, Is.GreaterThan(0.4f)); // Clockwise yaw turns north toward east.
            Assert.That(targetForward.y, Is.GreaterThan(0.02f)); // Positive pitch raises the nose.
            Assert.That(targetRight.y, Is.LessThan(-0.04f)); // Positive roll lowers the right wing.
            ReferenceFrame frame = realm.ReferenceFrame;
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.FollowedGhost.HasValue, Is.True);
            Assert.That(frame.FollowedGhost.Value, Is.EqualTo(originKey));
            Assert.That(frame.IsReferenceAvailable, Is.True);
            Assert.That(frame.Position, Is.EqualTo(originSpatial.Position));
            Assert.That(Quaternion.Angle(frame.Rotation, originSpatial.Rotation), Is.LessThan(0.01f));
            AssertOriginPose(origin);
            AssertProjectedTarget(frame, target, targetSpatial);
            View targetView = target.GetComponentInChildren<View>();
            Assert.That(targetView, Is.Not.Null);

            Double3 originPosition = originSpatial.Position;
            Double3 targetPosition = targetSpatial.Position;
            Quaternion originRotation = originSpatial.Rotation;
            Quaternion targetRotation = targetSpatial.Rotation;
            Vector3 targetWorldPosition = target.transform.position;

            demo.Advance(5.0);

            Assert.That(realm.TryGetPresence(originKey, out Presence updatedOrigin), Is.True);
            Assert.That(realm.TryGetPresence(targetKey, out Presence updatedTarget), Is.True);
            Assert.That(updatedOrigin, Is.SameAs(originPresence));
            Assert.That(updatedTarget, Is.SameAs(targetPresence));
            Assert.That(updatedOrigin.Root, Is.SameAs(origin));
            Assert.That(updatedTarget.Root, Is.SameAs(target));
            Assert.That(updatedOrigin.Modules.Count, Is.GreaterThan(0));
            Assert.That(updatedTarget.Modules.Count, Is.GreaterThan(0));
            AssertPositionChangedInAllAxes(originPosition, originSpatial.Position);
            AssertPositionChangedInAllAxes(targetPosition, targetSpatial.Position);
            AssertOrientationChangedInAllAxes(originRotation, originSpatial.Rotation);
            AssertOrientationChangedInAllAxes(targetRotation, targetSpatial.Rotation);
            Assert.That(frame.Position, Is.EqualTo(originSpatial.Position));
            Assert.That(Quaternion.Angle(frame.Rotation, originSpatial.Rotation), Is.LessThan(0.01f));
            AssertOriginPose(origin);
            AssertProjectedTarget(frame, target, targetSpatial);
            Assert.That(Vector3.Distance(target.transform.position, targetWorldPosition), Is.GreaterThan(0.01f));
            Assert.That(target.GetComponentInChildren<View>(), Is.SameAs(targetView));
            Assert.That(realm.Query().Count, Is.EqualTo(2));
            Assert.That(Realm.Default.Anchors.Count, Is.EqualTo(defaultAnchorCount));

            host.SetActive(false);
            Assert.That(demo.Realm, Is.Null);
            Assert.That(originPresence.IsRemoved && targetPresence.IsRemoved, Is.True);
            Assert.That(realm.Query().Count, Is.Zero);
            yield return null;
            Assert.That(origin == null && target == null, Is.True);
        }

        private static void AssertOriginPose(Ghost origin)
        {
            Assert.That(origin.transform.position.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(Quaternion.Angle(origin.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        private static void AssertProjectedTarget(ReferenceFrame frame, Ghost target, Spatial spatial)
        {
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(frame.TryToUnityPosition(spatial.Position, out Vector3 expectedPosition), Is.True);
            Assert.That(Vector3.Distance(target.transform.position, expectedPosition), Is.LessThan(0.001f));
            Quaternion expectedRotation = frame.ToUnityRotation(spatial.Rotation);
            Assert.That(Quaternion.Angle(target.transform.rotation, expectedRotation), Is.LessThan(0.01f));
        }

        private static void AssertPositionChangedInAllAxes(Double3 before, Double3 after)
        {
            Assert.That(Math.Abs(after.X - before.X), Is.GreaterThan(0.00001));
            Assert.That(Math.Abs(after.Y - before.Y), Is.GreaterThan(0.00001));
            Assert.That(Math.Abs(after.Z - before.Z), Is.GreaterThan(0.00001));
        }

        private static void AssertOrientationChangedInAllAxes(Quaternion before, Quaternion after)
        {
            Vector3 first = before.eulerAngles;
            Vector3 second = after.eulerAngles;
            Assert.That(Math.Abs(Mathf.DeltaAngle(first.x, second.x)), Is.GreaterThan(0.001f));
            Assert.That(Math.Abs(Mathf.DeltaAngle(first.y, second.y)), Is.GreaterThan(0.001f));
            Assert.That(Math.Abs(Mathf.DeltaAngle(first.z, second.z)), Is.GreaterThan(0.001f));
        }
    }
}
