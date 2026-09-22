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
    /// <summary>Runs the shipped quick-start scenes in the Editor and standalone test players.</summary>
    public sealed class QuickStartTests
    {
        private Scene _scene;
        private float _timeScale;

        /// <summary>Pauses sample simulation so event timing is controlled by each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
            Realm.Default.Dispose();
        }

        /// <summary>Unloads the sample and releases its default realm, even after a failed assertion.</summary>
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

        /// <summary>Inspector-configured polling creates a view and restarts after disable cleanup.</summary>
        [UnityTest]
        public IEnumerator Polling_CreatesViewAndRestarts()
        {
            yield return Load("Assets/Samples/Minimal/QuickStart.unity");
            var setup = Find<SceneSetup>();
            AssertView("quick-start");

            setup.gameObject.SetActive(false);
            Assert.That(Population("quick-start").Count, Is.Zero);

            setup.gameObject.SetActive(true);
            yield return null;
            yield return null;
            AssertView("quick-start");
        }

        /// <summary>SDK events defer updates, preserve identity, remove ghosts and cleanly resubscribe.</summary>
        [UnityTest]
        public IEnumerator Callbacks_PublishRemoveAndUnsubscribeOnDisable()
        {
            const string anchor = "callback-quick-start";
            yield return Load("Assets/Samples/Callbacks/Callbacks.unity");
            var setup = Find<SceneSetup>();
            var bootstrap = Find<Bootstrap>();
            bootstrap.enabled = false;
            var feed = FeedOf(bootstrap);
            var ghost = AssertView(anchor);
            var position = ghost.transform.localPosition;
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
            var restarted = FeedOf(bootstrap);
            Assert.That(restarted, Is.Not.SameAs(feed));
            AssertListeners(restarted, 1);
            AssertListeners(feed, 0);
            AssertView(anchor);

            setup.gameObject.SetActive(false);
            AssertListeners(restarted, 0);
            Assert.That(Population(anchor).Count, Is.Zero);
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
            foreach (var root in _scene.GetRootGameObjects())
            {
                var component = root.GetComponentInChildren<T>();
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
            var query = Population(anchor);
            Assert.That(query.Count, Is.EqualTo(1));
            var ghost = (Ghost)query.Single();
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
            foreach (var name in new[] { "Changed", "Removed" })
            {
                var handler = (Delegate)typeof(SimulatedFeed)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feed);
                Assert.That(handler == null ? 0 : handler.GetInvocationList().Length,
                    Is.EqualTo(expected), name);
            }
        }
    }
}
