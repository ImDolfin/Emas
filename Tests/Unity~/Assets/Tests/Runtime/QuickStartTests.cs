using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests.Samples
{
    /// <summary>
    /// Exercises the public behavior demonstrated by the shipped quick-start scenes in Editor and player runs.
    /// </summary>
    public sealed class QuickStartTests
    {
        private Scene _scene;
        private float _timeScale;

        /// <summary>
        /// Keeps sample positions stable while verifying startup and source replacement.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
            Realm.Default.Dispose();
        }

        /// <summary>
        /// Unloads the sample and releases its default realm after each scenario.
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
        /// The minimal prefab starts without application orchestration and recreates its visible population after reactivation.
        /// </summary>
        [UnityTest]
        public IEnumerator MinimalPrefab_AutomaticallyStartsAndRestarts()
        {
            yield return Load("Assets/Samples/Minimal/QuickStart.unity");
            RealmSetup setup = Find<RealmSetup>();
            AnchorSetup anchor = Find<AnchorSetup>();
            Realm originalRealm = setup.Realm;
            AssertView(originalRealm, "quick-start");

            setup.gameObject.SetActive(false);
            Assert.That(anchor.Anchor, Is.Null);
            Assert.That(originalRealm.Query().Count, Is.Zero);

            setup.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.That(setup.Realm, Is.Not.SameAs(originalRealm));
            AssertView(setup.Realm, "quick-start");
        }

        /// <summary>
        /// The callback sample consumes an initial SDK publication and reuses its visible entity when the detector reconnects.
        /// </summary>
        [UnityTest]
        public IEnumerator CallbackSample_ReconnectPreservesEntityAndView()
        {
            yield return Load("Assets/Samples/Callbacks/Callbacks.unity");
            RealmSetup setup = Find<RealmSetup>();
            Anchor anchor = Find<AnchorSetup>().Anchor;
            Ghost ghost = AssertView(setup.Realm, "callback-quick-start");
            View view = ghost.GetComponentInChildren<View>();

            anchor.RestartDetector(anchor.Detectors[0]);
            yield return null;
            yield return null;

            Assert.That(AssertView(setup.Realm, "callback-quick-start"), Is.SameAs(ghost));
            Assert.That(ghost.GetComponentInChildren<View>(), Is.SameAs(view));
        }

        /// <summary>
        /// Switching the example's SDK source keeps entity identities and the position/articulation contracts used by its views.
        /// </summary>
        [UnityTest]
        public IEnumerator Example_SourceReplacementPreservesConsumerContracts()
        {
            yield return Load("Assets/Samples/Example/Scenes/Example.unity");
            Emas.Sample.Bootstrap bootstrap = Find<Emas.Sample.Bootstrap>();
            Query cars = Realm.Default.Query().InAnchor("sample").OfKind(Emas.Sample.SampleKinds.Car);
            Dictionary<Key, IGhost> roots = new Dictionary<Key, IGhost>();
            foreach (IGhost ghost in cars)
            {
                roots.Add(ghost.Key, ghost);
                AssertCarView(ghost);
            }

            Assert.That(roots.Count, Is.GreaterThan(0));
            bootstrap.ReplaceCarSource();
            yield return null;
            yield return null;

            Assert.That(cars.Count, Is.EqualTo(roots.Count));
            foreach (IGhost ghost in cars)
            {
                Assert.That(ghost, Is.SameAs(roots[ghost.Key]));
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

        private static Ghost AssertView(Realm realm, string anchor)
        {
            Assert.That(realm, Is.Not.Null);
            Ghost ghost = (Ghost)realm.Query().InAnchor(anchor).Single();
            Assert.That(ghost.GetComponentInChildren<View>(), Is.Not.Null);
            return ghost;
        }
    }
}
