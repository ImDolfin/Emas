using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Specifies when SDK snapshots are refreshed through configured polling intervals.
    /// </summary>
    public sealed class PollingIntervalTests
    {
        private static readonly Kind TrackedKind = new Kind("tests.polling.interval");
        private Realm _realm;

        /// <summary>Creates the consumer-owned realm advanced by each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
        }

        /// <summary>Stops polling and removes its tracked population.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// A polling interval retains the last snapshot between reads; attachment and restart read immediately.
        /// Invalid intervals and attempts to reconfigure an attached detector are rejected.
        /// </summary>
        [Test]
        public void PollingInterval_DefersReadsButRestartReadsImmediately()
        {
            List<string> items = new List<string> { "initial" };
            int reads = 0;
            PollingPresenceDetector<string> detector = new PollingPresenceDetector<string>(TrackedKind)
                .ReadFrom(() =>
                {
                    reads++;
                    return items;
                })
                .IdentifyBy(id => id)
                .PollEvery(TimeSpan.FromDays(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => detector.PollEvery(TimeSpan.FromTicks(-1)));
            Anchor anchor = _realm.GetOrCreateAnchor("sdk", detector);
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("initial"));
            Assert.Throws<InvalidOperationException>(() => detector.PollEvery(TimeSpan.Zero));

            items.Clear();
            items.Add("replacement");
            _realm.Update();
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("initial"));

            anchor.RestartDetector(detector);
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("replacement"));
        }

        /// <summary>
        /// SDK polling continues at a positive interval while Unity game time is paused.
        /// The test waits for the next observable read instead of depending on an exact frame rate.
        /// </summary>
        [UnityTest]
        public IEnumerator PollingInterval_UsesUnscaledUnityTime()
        {
            float previousTimeScale = Time.timeScale;
            int reads = 0;
            try
            {
                Time.timeScale = 0;
                PollingPresenceDetector<string> detector = new PollingPresenceDetector<string>(TrackedKind)
                    .ReadFrom(() =>
                    {
                        reads++;
                        return Array.Empty<string>();
                    })
                    .IdentifyBy(id => id)
                    .PollEvery(TimeSpan.FromMilliseconds(100));
                _realm.GetOrCreateAnchor("sdk", detector);
                Assert.That(reads, Is.EqualTo(1));
                double deadline = Time.realtimeSinceStartupAsDouble + 5;
                while (reads < 2 && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                    _realm.Update();
                }

                Assert.That(reads, Is.GreaterThanOrEqualTo(2));
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }
        }
    }
}
