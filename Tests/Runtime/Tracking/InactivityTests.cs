using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies optional inactivity expiry and publication deadlines for individual ghosts.
    /// </summary>
    public sealed class InactivityTests
    {
        private static readonly Kind Kind = new Kind("inactivity");
        private Realm _realm;
        private double _now;

        /// <summary>
        /// Creates a realm with a controllable unscaled clock.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _now = 10;
            _realm = new Realm(() => _now);
        }

        /// <summary>
        /// Releases tracked objects.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Sources do not expire their population unless explicitly configured.
        /// </summary>
        [Test]
        public void DefaultAndNullTimeout_DisableExpiry()
        {
            Probe source = new Probe();
            Assert.That(source.InactivityTimeout, Is.Null);
            source.InactivityTimeout = TimeSpan.FromSeconds(1);
            source.InactivityTimeout = null;
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost ghost = source.Publish("one");
            _realm.Update();
            _now += 10000;
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
        }

        /// <summary>
        /// Timeout configuration requires a positive duration and a detached source.
        /// </summary>
        [Test]
        public void Timeout_ValidatesConfiguration()
        {
            Probe source = ExpiringSource();
            Assert.Throws<ArgumentOutOfRangeException>(() => source.InactivityTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => source.InactivityTimeout = TimeSpan.FromTicks(-1));
            Assert.That(source.InactivityTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            Assert.Throws<InvalidOperationException>(() => source.InactivityTimeout = null);
            anchor.RemoveSource(source);
            Assert.DoesNotThrow(() => source.InactivityTimeout = null);
        }

        /// <summary>
        /// Partial publication refreshes only its entity, while silent entities expire exactly at their deadline.
        /// </summary>
        [Test]
        public void Publication_RefreshesEachGhostIndependently()
        {
            Probe source = ExpiringSource();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost updated = source.Publish("updated");
            TestGhost silent = source.Publish("silent");
            _realm.Update();
            _now = 11.5;
            Assert.That(source.Publish("updated"), Is.SameAs(updated));
            updated.Position = 1;
            _now = 11.999;
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            _now = 12;
            _realm.Update();
            AssertMissing(silent.Key);
            Assert.That(_realm.Query().Single(), Is.SameAs(updated));
            _now = 13.499;
            _realm.Update();
            Assert.That(updated.IsAvailable, Is.True);
            _now = 13.5;
            _realm.Update();
            AssertMissing(updated.Key);
            Assert.That(source.IsActive, Is.True);
        }

        /// <summary>
        /// Each source chooses its timeout independently from neighboring sources on the same anchor.
        /// </summary>
        [Test]
        public void Timeout_IsConfiguredPerSource()
        {
            Probe expiring = ExpiringSource();
            Probe retained = new Probe();
            _realm.GetOrCreateAnchor("anchor", expiring, retained);
            TestGhost shortLived = expiring.Publish("short");
            TestGhost persistent = retained.Publish("persistent");
            _realm.Update();
            _now = 12;
            _realm.Update();
            AssertMissing(shortLived.Key);
            Assert.That(_realm.Query().Single(), Is.SameAs(persistent));
        }

        /// <summary>
        /// Cached data writes can explicitly report activity without publishing unrelated properties.
        /// </summary>
        [Test]
        public void MarkPublished_RefreshesCachedGhostAndRejectsRemovedInstance()
        {
            Probe source = ExpiringSource();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost original = source.Publish("one");
            _realm.Update();
            _now = 11;
            original.Articulation = 3;
            source.RecordPublication(original);
            _now = 12;
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(original));
            Assert.That(original.Position, Is.Zero);
            Assert.That(original.Articulation, Is.EqualTo(3));
            _now = 13;
            _realm.Update();
            AssertMissing(original.Key);
            TestGhost replacement = source.Publish("one");
            Assert.That(replacement, Is.Not.SameAs(original));
            Assert.Throws<ArgumentException>(() => source.RecordPublication(original));
            Assert.DoesNotThrow(() => source.RecordPublication(replacement));
        }

        /// <summary>
        /// Activity cannot be recorded for another source, another realm, or an inactive attachment.
        /// </summary>
        [Test]
        public void MarkPublished_RequiresCurrentOwnership()
        {
            Probe source = ExpiringSource();
            Probe other = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source, other);
            TestGhost ghost = source.Publish("one");
            TestGhost foreign = other.Publish("other");
            Assert.Throws<ArgumentNullException>(() => source.RecordPublication(null));
            Assert.Throws<ArgumentException>(() => source.RecordPublication(foreign));
            TestGhost prepared = _realm.Prepare<TestGhost>("anchor", Kind, "prepared");
            Assert.Throws<ArgumentException>(() => source.RecordPublication(prepared));
            using (Realm otherRealm = new Realm())
            {
                Probe otherSource = new Probe();
                otherRealm.GetOrCreateAnchor("anchor", otherSource);
                TestGhost sameKey = otherSource.Publish("one");
                Assert.Throws<ArgumentException>(() => source.RecordPublication(sameKey));
            }

            anchor.RemoveSource(source);
            Assert.Throws<InvalidOperationException>(() => source.RecordPublication(ghost));
        }

        /// <summary>
        /// Processing a queued update at the deadline refreshes the ghost before expiry runs.
        /// </summary>
        [Test]
        public void CallbackPublication_AtDeadlinePreservesIdentity()
        {
            Action<string> publish = null;
            CallbackPresenceSource<string, TestGhost> source = new CallbackPresenceSource<string, TestGhost>(Kind)
                .IdentifyBy(id => id)
                .Apply((id, ghost) => ghost.Position++)
                .Listen((onPublish, onRemove) =>
                {
                    publish = onPublish;
                    return null;
                });
            source.InactivityTimeout = TimeSpan.FromSeconds(2);
            _realm.GetOrCreateAnchor("anchor", source);
            publish("one");
            _realm.Update();
            IGhost original = _realm.Query().Single();
            _now = 12;
            publish("one");
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(original));
            Assert.That(((TestGhost)original).Position, Is.EqualTo(2));
            _now = 14;
            _realm.Update();
            AssertMissing(original.Key);
        }

        /// <summary>
        /// Scheduled polling refreshes deadlines before expiry, including polls due exactly at the deadline.
        /// </summary>
        [Test]
        public void PollingPublication_AtDeadlinePreservesIdentity()
        {
            PollingPresenceSource<string, TestGhost> source = new PollingPresenceSource<string, TestGhost>(Kind, () => _now)
                .ReadFrom(() => new[] { "one" })
                .IdentifyBy(id => id)
                .Apply((id, ghost) => ghost.Position++)
                .PollEvery(TimeSpan.FromSeconds(2));
            source.InactivityTimeout = TimeSpan.FromSeconds(2);
            _realm.GetOrCreateAnchor("anchor", source);
            IGhost original = _realm.Query().Single();
            _now = 12;
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(original));
            Assert.That(((TestGhost)original).Position, Is.EqualTo(2));
        }

        /// <summary>
        /// Read access, preparation of an existing entity, and presentation do not count as source activity.
        /// </summary>
        [Test]
        public void ConsumerOperations_DoNotExtendLifetime()
        {
            Probe source = ExpiringSource();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost ghost = source.Publish("one");
            TestGhost prepared = _realm.Prepare<TestGhost>("anchor", Kind, "prepared");
            _realm.Update();
            _now = 11;
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(_realm.Prepare<TestGhost>("anchor", Kind, "one"), Is.SameAs(ghost));
            _realm.Manifest(ghost);
            _realm.SetDetailLevel(ghost, DetailLevel.Minimal);
            _now = 12;
            _realm.Update();
            AssertMissing(ghost.Key);
            IGhost found;
            Assert.That(_realm.TryGetGhost(prepared.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(prepared));
            Assert.That(prepared.IsAvailable, Is.False);
        }

        /// <summary>
        /// Expiry notifies departures once and destroys the root together with its instantiated view.
        /// </summary>
        [UnityTest]
        public IEnumerator Expiry_RemovesViewRootAndObservationMembership()
        {
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            GameObject prefab = new GameObject("inactivity view");
            prefab.SetActive(false);
            try
            {
                blueprint.Configure(Kind, null, null, prefab);
                _realm.RegisterBlueprint(blueprint);
                Probe source = ExpiringSource();
                _realm.GetOrCreateAnchor("anchor", source);
                TestGhost ghost = source.Publish("one");
                _realm.Update();
                View view = _realm.Manifest(ghost);
                Assert.That(view, Is.Not.Null);
                List<Key> departures = new List<Key>();
                using (_realm.Query().Observe(item => { }, departures.Add))
                {
                    _now = 12;
                    _realm.Update();
                    AssertMissing(ghost.Key);
                    Assert.That(ghost.IsAvailable, Is.False);
                    Assert.That(ghost.gameObject.activeSelf, Is.False);
                    Assert.That(view.gameObject.activeSelf, Is.False);
                    Assert.That(departures, Is.EqualTo(new[] { ghost.Key }));
                    _realm.Update();
                    Assert.That(departures.Count, Is.EqualTo(1));
                    yield return null;
                    Assert.That(ghost == null, Is.True);
                    Assert.That(view == null, Is.True);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(blueprint);
            }
        }

        /// <summary>
        /// Reentrant scene cleanup can refresh another candidate without losing its newly published data.
        /// </summary>
        [Test]
        public void Expiry_RechecksPublicationsDuringSceneCleanup()
        {
            Probe source = ExpiringSource();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost first = source.Publish("first");
            TestGhost second = source.Publish("second");
            _realm.Update();
            DisableAction firstCleanup = first.gameObject.AddComponent<DisableAction>();
            DisableAction secondCleanup = second.gameObject.AddComponent<DisableAction>();
            firstCleanup.Action = () =>
            {
                secondCleanup.Action = null;
                source.Publish("second");
            };
            secondCleanup.Action = () =>
            {
                firstCleanup.Action = null;
                source.Publish("first");
            };
            _now = 12;
            _realm.Update();
            IGhost remaining = _realm.Query().Single();
            Assert.That(ReferenceEquals(remaining, first) || ReferenceEquals(remaining, second), Is.True);
            AssertMissing(ReferenceEquals(remaining, first) ? second.Key : first.Key);
        }

        /// <summary>
        /// The public realm clock expires inactive ghosts even while scaled simulation time is paused.
        /// </summary>
        [UnityTest]
        public IEnumerator PublicClock_ExpiresWhileTimeScaleIsZero()
        {
            _realm.Dispose();
            _realm = new Realm();
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                Probe source = new Probe { InactivityTimeout = TimeSpan.FromMilliseconds(100) };
                _realm.GetOrCreateAnchor("anchor", source);
                TestGhost ghost = source.Publish("one");
                Key key = ghost.Key;
                _realm.Update();
                double deadline = Time.realtimeSinceStartupAsDouble + 3;
                IGhost found;
                while (_realm.TryGetGhost(key, out found) && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                    _realm.Update();
                }

                AssertMissing(key);
                Assert.That(source.IsActive, Is.True);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }
        }

        private static Probe ExpiringSource()
        {
            return new Probe { InactivityTimeout = TimeSpan.FromSeconds(2) };
        }

        private void AssertMissing(Key key)
        {
            IGhost found;
            Assert.That(_realm.TryGetGhost(key, out found), Is.False);
        }

        private sealed class Probe : PresenceSource
        {
            internal TestGhost Publish(string id)
            {
                return GetOrCreate<TestGhost>(id, Kind);
            }

            internal void RecordPublication(IGhost ghost)
            {
                MarkPublished(ghost);
            }
        }

        private sealed class TestGhost : Ghost
        {
            internal int Position;
            internal int Articulation;
        }

        private sealed class DisableAction : MonoBehaviour
        {
            internal Action Action;

            private void OnDisable()
            {
                Action callback = Action;
                Action = null;
                if (callback != null)
                {
                    callback();
                }
            }
        }
    }
}
