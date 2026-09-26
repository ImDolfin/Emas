using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies that presentation failures leave tracking healthy and allow deliberate recovery.
    /// </summary>
    public sealed class ViewFailureTests
    {
        private static readonly Kind Kind = new Kind("tests.views");
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private Realm _realm;

        /// <summary>
        /// Creates an isolated realm for each failure scenario.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
        }

        /// <summary>
        /// Releases tracking and temporary blueprint assets.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
            foreach (UnityEngine.Object asset in _assets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            _assets.Clear();
        }

        /// <summary>
        /// Immediate and deferred view failures preserve the source, both ghosts and healthy views,
        /// avoid retries on ordinary publication, and recover through an explicit request or registration.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RefreshFailure_IsolatedFromTrackingAndRecoverable(bool deferred, bool registerAgain)
        {
            GameObject prefab = new GameObject("working view");
            prefab.SetActive(false);
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(Kind, null, new ManifestationVariant[0], prefab);
            _assets.Add(prefab);
            _assets.Add(blueprint);
            _realm.RegisterManifestationBlueprint(blueprint);

            TestSource source = new TestSource();
            _realm.GetOrCreateAnchor("view-tests", source);
            TestGhost broken = source.Publish("broken", 10);
            TestGhost healthy = source.Publish("healthy", 20);
            broken.RejectStaging = true;
            List<IGhost> arrivals = new List<IGhost>();
            List<Key> departures = new List<Key>();
            _realm.Query().Observe(arrivals.Add, departures.Add);

            View originalHealthyView;
            if (deferred)
            {
                Assert.That(_realm.Manifest(broken, DetailLevel.Reduced), Is.Null);
                Assert.That(_realm.Manifest(healthy), Is.Null);
                ExpectedErrors.Verify(_realm.Update, "view refresh.*view-tests:tests.views:broken");
                originalHealthyView = healthy.GetComponentInChildren<View>();
            }
            else
            {
                _realm.Update();
                originalHealthyView = _realm.Manifest(healthy);
                ExpectedErrors.Verify(() =>
                    Assert.That(_realm.Manifest(broken, DetailLevel.Reduced), Is.Null),
                    "view refresh.*view-tests:tests.views:broken");
            }

            Assert.That(source.IsActive, Is.True);
            Assert.That(source.StopCount, Is.Zero);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(broken.IsAvailable && healthy.IsAvailable, Is.True);
            Assert.That(broken.gameObject.activeInHierarchy && healthy.gameObject.activeInHierarchy, Is.True);
            Assert.That(arrivals, Is.EquivalentTo(new IGhost[] { broken, healthy }));
            Assert.That(departures, Is.Empty);
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            Assert.That(broken.GetComponentInChildren<View>(true), Is.Null);
            Assert.That(broken.transform.childCount, Is.Zero);
            Assert.That(broken.FailedAttempts, Is.EqualTo(1));
            Assert.That(originalHealthyView, Is.Not.Null);
            Assert.That(originalHealthyView.gameObject.activeInHierarchy, Is.True);

            source.Updating = () =>
            {
                source.Publish("broken", 11);
                source.Publish("healthy", 21);
            };
            ExpectedErrors.Verify(() =>
            {
                _realm.Update();
                _realm.Update();
            });
            Assert.That(broken.Value, Is.EqualTo(11));
            Assert.That(healthy.Value, Is.EqualTo(21));
            Assert.That(broken.FailedAttempts, Is.EqualTo(1));
            Assert.That(healthy.GetComponentInChildren<View>(), Is.SameAs(originalHealthyView));
            Assert.That(arrivals.Count, Is.EqualTo(2));
            Assert.That(departures, Is.Empty);

            broken.RejectStaging = false;
            View recovered;
            if (registerAgain)
            {
                _realm.RegisterManifestationBlueprint(blueprint);
                _realm.Update();
                recovered = broken.GetComponentInChildren<View>();
            }
            else
            {
                recovered = _realm.Manifest(broken);
            }

            Assert.That(recovered, Is.Not.Null);
            Assert.That(recovered.Ghost, Is.SameAs(broken));
            Assert.That(recovered.RequestedDetailLevel, Is.EqualTo(DetailLevel.Reduced));
            Assert.That(recovered.gameObject.activeInHierarchy, Is.True);
            Assert.That(healthy.GetComponentInChildren<View>(), Is.SameAs(originalHealthyView));
            Assert.That(source.IsActive, Is.True);
            Assert.That(source.StopCount, Is.Zero);
            Assert.That(source.LastError, Is.Null);
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            Assert.That(departures, Is.Empty);
        }

        private sealed class TestGhost : Ghost
        {
            internal bool RejectStaging;
            internal int FailedAttempts;
            internal int Value;

            private void OnTransformChildrenChanged()
            {
                if (!RejectStaging)
                {
                    return;
                }

                for (int index = 0; index < transform.childCount; index++)
                {
                    GameObject child = transform.GetChild(index).gameObject;
                    if (child.name == "[Emas View Staging]")
                    {
                        // A real Unity callback invalidates the object while view creation is in progress.
                        // The subsequent access throws in ViewManager, rather than inside a Unity callback.
                        FailedAttempts++;
                        UnityEngine.Object.DestroyImmediate(child);
                        return;
                    }
                }
            }
        }

        private sealed class TestSource : PresenceDetector
        {
            internal Action Updating;
            internal int StopCount;

            internal TestGhost Publish(string id, int value)
            {
                TestGhost ghost = GetOrCreate<TestGhost>(id, Kind);
                ghost.Value = value;
                return ghost;
            }

            protected override void OnUpdate()
            {
                if (Updating != null)
                {
                    Updating();
                }
            }

            protected override void OnStop()
            {
                StopCount++;
            }
        }
    }
}
