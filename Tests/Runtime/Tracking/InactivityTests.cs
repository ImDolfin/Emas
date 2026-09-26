using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Specifies expiry and recovery visible to applications using detector timeout settings.
    /// Tests use the public Unity clock and wait for observable state changes rather than exact timestamps.
    /// </summary>
    public sealed class InactivityTests
    {
        private static readonly Kind TrackedKind = new Kind("tests.expiry");
        private Realm _realm;

        /// <summary>Creates a public realm with an application-defined root type.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) => { });
        }

        /// <summary>Releases the detector population and its scene objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Expiry settings reject invalid durations and cannot change during an active attachment.
        /// A detached detector can be configured again before reuse.
        /// </summary>
        [Test]
        public void ExpirySettings_RequireValidDurationsAndDetachedDetector()
        {
            Probe detector = new Probe();
            Assert.Throws<ArgumentOutOfRangeException>(() => detector.InactivityTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => detector.DisappearanceGracePeriod = TimeSpan.FromTicks(-1));
            detector.InactivityTimeout = TimeSpan.FromSeconds(2);
            detector.DisappearanceGracePeriod = TimeSpan.FromSeconds(1);
            Anchor anchor = _realm.GetOrCreateAnchor("sdk", detector);
            Assert.Throws<InvalidOperationException>(() => detector.InactivityTimeout = null);
            Assert.Throws<InvalidOperationException>(() => detector.DisappearanceGracePeriod = TimeSpan.Zero);
            anchor.RemoveDetector(detector);
            detector.InactivityTimeout = null;
            detector.DisappearanceGracePeriod = TimeSpan.Zero;
            Assert.That(detector.InactivityTimeout, Is.Null);
            Assert.That(detector.DisappearanceGracePeriod, Is.EqualTo(TimeSpan.Zero));
        }

        /// <summary>
        /// Pausing Unity game time does not retain stale SDK data. A detector without an inactivity timeout
        /// keeps its population while a timed detector's silent entity is removed.
        /// </summary>
        [UnityTest]
        public IEnumerator Inactivity_UsesUnscaledTimeAndLeavesUntimedEntitiesAvailable()
        {
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                Probe timed = new Probe { InactivityTimeout = TimeSpan.FromMilliseconds(100) };
                Probe untimed = new Probe();
                _realm.GetOrCreateAnchor("sdk", timed, untimed);
                Presence stale = timed.Publish("stale");
                Presence retained = untimed.Publish("retained");
                _realm.Update();
                Assert.That(stale.IsAvailable, Is.True);

                yield return AdvanceUntil(() => stale.IsRemoved);

                Assert.That(_realm.TryGetPresence(stale.Key, out Presence ignored), Is.False);
                Assert.That(_realm.Query().Single(), Is.SameAs(retained.Root));
                Assert.That(timed.IsActive, Is.True);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }
        }

        /// <summary>
        /// Applications updating a cached root can resume it during grace without replacing its identity.
        /// Observers receive a departure followed by a new arrival with the updated application data.
        /// </summary>
        [UnityTest]
        public IEnumerator CachedPublication_RestoresPresenceDuringGrace()
        {
            Probe detector = new Probe
            {
                InactivityTimeout = TimeSpan.FromMilliseconds(100),
                DisappearanceGracePeriod = TimeSpan.FromSeconds(2)
            };
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.Publish("one");
            ProbeGhost root = (ProbeGhost)presence.Root;
            _realm.Update();
            List<string> events = new List<string>();
            using (_realm.Query().Observe(ghost => events.Add("enter"), key => events.Add("leave")))
            {
                yield return AdvanceUntil(() => !presence.IsAvailable);
                Assert.That(presence.IsRemoved, Is.False);
                Assert.That(root.gameObject.activeSelf, Is.False);

                root.Value = 7;
                detector.PublishCached(root);
                _realm.Update();

                Assert.That(_realm.TryGetPresence(presence.Key, out Presence recovered), Is.True);
                Assert.That(recovered, Is.SameAs(presence));
                Assert.That(_realm.Query().Single(), Is.SameAs(root));
                Assert.That(root.Value, Is.EqualTo(7));
                Assert.That(events, Is.EqualTo(new[] { "enter", "leave", "enter" }));
            }
        }

        /// <summary>
        /// A disappearing entity remains addressable during grace and can be detected again with the same handle.
        /// If it stays missing, the public lookup and handle eventually report final removal.
        /// </summary>
        [UnityTest]
        public IEnumerator DisappearanceGrace_AllowsRediscoveryThenRemovesUnreportedEntity()
        {
            Probe detector = new Probe { DisappearanceGracePeriod = TimeSpan.FromMilliseconds(250) };
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.Publish("one");
            Ghost root = presence.Root;
            _realm.Update();
            detector.Lose("one");
            Assert.That(presence.IsAvailable, Is.False);
            Assert.That(presence.IsRemoved, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(_realm.TryGetPresence(presence.Key, out Presence missing), Is.True);
            Assert.That(missing, Is.SameAs(presence));

            Assert.That(detector.Publish("one"), Is.SameAs(presence));
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(root));
            detector.Lose("one");
            yield return AdvanceUntil(() => presence.IsRemoved);

            Assert.That(presence.Root, Is.Null);
            Assert.That(_realm.TryGetPresence(presence.Key, out Presence ignored), Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        private IEnumerator AdvanceUntil(Func<bool> condition)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                _realm.Update();
            }

            Assert.That(condition(), Is.True, "The public expiry transition did not complete within five seconds.");
        }

        private sealed class Probe : PresenceDetector
        {
            internal Presence Publish(string id)
            {
                return Detect(id, TrackedKind);
            }

            internal void PublishCached(IGhost ghost)
            {
                MarkPublished(ghost);
            }

            internal void Lose(string id)
            {
                Disappear(TrackedKind, id);
            }
        }

        private sealed class ProbeGhost : Ghost
        {
            internal int Value;
        }
    }
}
