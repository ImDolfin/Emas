using System;
using System.Collections;
using System.Reflection;
using Emas.Callbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests.Samples
{
    /// <summary>
    /// Runs the shipped quick-start scenes in the Editor and standalone test players.
    /// </summary>
    public sealed class QuickStartTests
    {
        private Scene _scene;
        private float _timeScale;

        /// <summary>
        /// Pauses sample simulation so event timing is controlled by each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
            Realm.Default.Dispose();
        }

        /// <summary>
        /// Unloads the sample and releases its default realm, even after a failed assertion.
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }

            Realm.Default.Dispose();
            Time.timeScale = _timeScale;
        }

        /// <summary>
        /// Inspector-configured polling creates a view and restarts after disable cleanup.
        /// </summary>
        [UnityTest]
        public IEnumerator Polling_CreatesViewAndRestarts()
        {
            yield return Load("Assets/Samples/Minimal/QuickStart.unity");
            SceneSetup setup = Find<SceneSetup>();
            AssertView("quick-start");

            setup.gameObject.SetActive(false);
            Assert.That(Population("quick-start").Count, Is.Zero);

            setup.gameObject.SetActive(true);
            yield return null;
            yield return null;
            AssertView("quick-start");
        }

        /// <summary>
        /// SDK events defer updates, preserve identity, remove ghosts and cleanly resubscribe.
        /// </summary>
        [UnityTest]
        public IEnumerator Callbacks_PublishRemoveAndUnsubscribeOnDisable()
        {
            const string anchor = "callback-quick-start";
            yield return Load("Assets/Samples/Callbacks/Callbacks.unity");
            SceneSetup setup = Find<SceneSetup>();
            Bootstrap bootstrap = Find<Bootstrap>();
            bootstrap.enabled = false;
            SimulatedFeed feed = FeedOf(bootstrap);
            Ghost ghost = AssertView(anchor);
            Vector3 position = ghost.transform.localPosition;
            AssertListeners(feed, 1);

            feed.Advance(1f);
            Assert.That(ghost.transform.localPosition, Is.EqualTo(position), "Callbacks must stay deferred.");
            yield return null;
            yield return null;
            Assert.That(Population(anchor).Single(), Is.SameAs(ghost));
            Assert.That(ghost.transform.localPosition, Is.EqualTo(feed.Current.Position));
            Assert.That(ghost.transform.localPosition, Is.Not.EqualTo(position));

            feed.Advance(3.2f);
            yield return null;
            yield return null;
            Assert.That(Population(anchor).Count, Is.Zero, "Explicit removal must remove the ghost.");

            feed.Advance(2f);
            yield return null;
            yield return null;
            AssertView(anchor);

            setup.gameObject.SetActive(false);
            Assert.That(Population(anchor).Count, Is.Zero);
            AssertListeners(feed, 0);
            feed.Advance(1f);

            bootstrap.enabled = true;
            setup.gameObject.SetActive(true);
            yield return null;
            yield return null;
            SimulatedFeed restarted = FeedOf(bootstrap);
            Assert.That(restarted, Is.Not.SameAs(feed));
            AssertListeners(restarted, 1);
            AssertListeners(feed, 0);
            AssertView(anchor);

            setup.gameObject.SetActive(false);
            AssertListeners(restarted, 0);
            Assert.That(Population(anchor).Count, Is.Zero);
        }

        /// <summary>
        /// The larger example consumes read-only contracts and preserves roots across source replacement in players.
        /// </summary>
        [UnityTest]
        public IEnumerator Example_ConsumesContractsReplacesSourceAndDisposesSubscriptions()
        {
            yield return Load("Assets/Samples/Example/Scenes/Example.unity");
            Sample.Bootstrap bootstrap = Find<Emas.Sample.Bootstrap>();
            Query cars = Realm.Default.Query().InAnchor("sample").OfKind(Emas.Sample.SampleKinds.Car);
            System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>();
            System.Collections.Generic.Dictionary<Key, IGhost> roots = new System.Collections.Generic.Dictionary<Key, IGhost>();
            foreach (IGhost ghost in cars)
            {
                roots.Add(ghost.Key, ghost);
                foreach (Renderer renderer in ((Ghost)ghost).GetComponentsInChildren<Renderer>())
                {
                    materials.Add(renderer.sharedMaterial);
                }

                AssertCarView(ghost);
            }

            Assert.That(roots.Count, Is.GreaterThan(0));
            Anchor anchor = Realm.Default.Anchors[0];
            PresenceSource originalSource = anchor.Sources[0];
            bootstrap.ReplaceCarSource();
            yield return null;
            yield return null;
            Assert.That(originalSource.IsAttached || originalSource.IsActive, Is.False);
            Assert.That(anchor.Sources[0], Is.TypeOf<Emas.Sample.SdkTwoCarSource>());
            Assert.That(cars.Count, Is.EqualTo(roots.Count));
            foreach (IGhost ghost in cars)
            {
                Assert.That(ghost, Is.SameAs(roots[ghost.Key]));
                AssertCarView(ghost);
            }

            bootstrap.gameObject.SetActive(false);
            Assert.That(cars.Count, Is.Zero);
            Assert.That(anchor.Sources, Is.Empty);
            foreach (string name in new[] { "_carSubscription", "_aircraftSubscription" })
            {
                Assert.That(typeof(Emas.Sample.Bootstrap).GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap), Is.Null);
            }

            yield return null;
            foreach (Material material in materials)
            {
                Assert.That(material == null, Is.True, "Generated materials must be released on disable.");
            }

            bootstrap.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.That(cars.Count, Is.EqualTo(roots.Count));
            foreach (IGhost ghost in cars)
            {
                Assert.That(ghost, Is.Not.SameAs(roots[ghost.Key]));
                AssertCarView(ghost);
            }
        }

        private static void AssertCarView(IGhost ghost)
        {
            Emas.Sample.I3DPosition position;
            Emas.Sample.IArticulate articulation;
            Assert.That(ghost.TryGet(out position), Is.True);
            Assert.That(ghost.TryGet(out articulation), Is.True);
            Ghost root = (Ghost)ghost;
            Assert.That(root.transform.localPosition, Is.EqualTo(position.Position));
            View view = root.GetComponentInChildren<View>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.Ghost, Is.SameAs(ghost));
            Assert.That(view.GetComponent<Emas.Sample.VehicleLogic>(), Is.Not.Null);
            Assert.That(view.GetComponent<Emas.Sample.ArticulationLogic>(), Is.Not.Null);
            Assert.That(Quaternion.Angle(view.transform.localRotation,
                Quaternion.Euler(0f, articulation.Steering * 12f, 0f)), Is.LessThan(0.01f));
        }

        private IEnumerator Load(string path)
        {
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(path);
            Assert.That(_scene.IsValid(), Is.True, path);
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

            Assert.Fail("Missing sample component: " + typeof(T).Name);
            return null;
        }

        private static Query Population(string anchor)
        {
            return Realm.Default.Query().InAnchor(anchor);
        }

        private static Ghost AssertView(string anchor)
        {
            Query query = Population(anchor);
            Assert.That(query.Count, Is.EqualTo(1));
            Ghost ghost = (Ghost)query.Single();
            Assert.That(ghost.GetComponentInChildren<View>(), Is.Not.Null);
            return ghost;
        }

        private static SimulatedFeed FeedOf(Bootstrap bootstrap)
        {
            return (SimulatedFeed)typeof(Bootstrap)
                .GetField("_feed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap);
        }

        private static void AssertListeners(SimulatedFeed feed, int expected)
        {
            foreach (string name in new[] { "Changed", "Removed" })
            {
                Delegate handler = (Delegate)typeof(SimulatedFeed)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feed);
                Assert.That(handler == null ? 0 : handler.GetInvocationList().Length,
                    Is.EqualTo(expected), name);
            }
        }
    }
}
