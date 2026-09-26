using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests.Samples
{
    /// <summary>
    /// Runs the imported relative-world demonstration against its actual source and generated views.
    /// </summary>
    public sealed class RelativeWorldTests
    {
        private Scene _scene;
        private float _timeScale;

        /// <summary>
        /// Leaves simulation advancement under test control.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        /// <summary>
        /// Releases the demo realm and generated Unity objects.
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
        /// Driving changes relative traffic placement without republishing traffic and restores views on range entry.
        /// </summary>
        [UnityTest]
        public IEnumerator RelativeWorld_KeepsEgoFixedAndProjectsUnchangedTraffic()
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
            Assert.That(demo, Is.Not.Null);
            Realm realm = demo.Realm;
            Ghost ego = GetCar(realm, "ego");
            Ghost parked = GetCar(realm, "parked");
            Ghost distant = GetCar(realm, "distant");
            Double3 parkedNetworkPosition = parked.GetComponent<Spatial>().Position;
            View parkedView = parked.GetComponentInChildren<View>();

            Assert.That(realm.Query().Count, Is.EqualTo(3));
            Assert.That(ego.transform.position.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(Vector3.Distance(parked.transform.position, new Vector3(3f, 0f, 20.25f)), Is.LessThan(0.00001f));
            Assert.That(parkedView, Is.Not.Null);
            Assert.That(distant.GetComponentInChildren<View>(), Is.Null);
            Assert.That(distant.GetComponent<Spatial>().IsInRange, Is.False);
            Assert.That(realm.ReferenceFrame.Position.X, Is.GreaterThan(1000000000.0));

            demo.Advance(5.0);
            float travel = (float)(Math.Sin(0.75) * 30.0);
            Quaternion rotation = Quaternion.Euler(0f, (float)Math.Sin(0.5) * 12f, 0f);
            Vector3 expected = Quaternion.Inverse(rotation) * new Vector3(3f, 0f, 20.25f - travel);
            Assert.That(ego.transform.position.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(Quaternion.Angle(ego.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(parked.transform.position, expected), Is.LessThan(0.00001f));
            Assert.That(parked.GetComponent<Spatial>().Position, Is.EqualTo(parkedNetworkPosition));
            Assert.That(parked.GetComponentInChildren<View>(), Is.SameAs(parkedView));
            Assert.That(distant.GetComponent<Spatial>().IsInRange, Is.True);
            Assert.That(distant.GetComponentInChildren<View>(), Is.Not.Null);
            Assert.That(Realm.Default.Anchors.Count, Is.EqualTo(defaultAnchorCount));

            host.SetActive(false);
            Assert.That(demo.Realm, Is.Null);
            Assert.That(realm.Query().Count, Is.Zero);
            yield return null;
            Assert.That(ego == null && parked == null && distant == null, Is.True);
        }

        private static Ghost GetCar(Realm realm, string id)
        {
            IGhost ghost;
            Assert.That(realm.TryGetGhost(new Key("relative-world", new Kind("relative.car"), id), out ghost), Is.True);
            return (Ghost)ghost;
        }
    }
}
