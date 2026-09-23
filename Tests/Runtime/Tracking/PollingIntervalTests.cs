using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies polling cadence, population retention and attachment deadlines.
    /// </summary>
    public sealed class PollingIntervalTests
    {
        private static readonly Kind Kind = new Kind("interval");
        private readonly List<string> _items = new List<string>();
        private Realm _realm;
        private double _now;
        private int _reads;
        private int _value;
        private bool _fail;

        /// <summary>
        /// Creates a realm and a controllable elapsed-time clock.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
            _now = 10;
            _reads = 0;
            _value = 1;
            _fail = false;
            _items.Clear();
            _items.Add("one");
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
        /// Explicit zero restores polling on every realm update.
        /// </summary>
        [Test]
        public void ZeroInterval_PollsEveryUpdate()
        {
            PollingPresenceSource<string, TestGhost> source = CreateSource().PollEvery(TimeSpan.FromSeconds(1));
            Assert.That(source.PollEvery(TimeSpan.Zero), Is.SameAs(source));

            _realm.GetOrCreateAnchor("anchor", source);
            _realm.Update();
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(3));
        }

        /// <summary>
        /// Negative intervals fail without replacing the previous configuration.
        /// </summary>
        [Test]
        public void NegativeInterval_IsRejected()
        {
            PollingPresenceSource<string, TestGhost> source = CreateSource().PollEvery(TimeSpan.FromSeconds(1));
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
                () => source.PollEvery(TimeSpan.FromTicks(-1)));
            Assert.That(error.ParamName, Is.EqualTo("interval"));
            _realm.GetOrCreateAnchor("anchor", source);
            _now += 0.5;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(1));
        }

        /// <summary>
        /// The complete population stays intact until the next due read succeeds.
        /// </summary>
        [Test]
        public void Interval_RetainsDataAndMembershipUntilDeadline()
        {
            _items.Add("removed");
            _realm.GetOrCreateAnchor("anchor", CreateSource().PollEvery(TimeSpan.FromSeconds(0.5)));
            Key key = new Key("anchor", Kind, "one");
            IGhost retained;
            Assert.That(_realm.TryGetGhost(key, out retained), Is.True);
            Assert.That(_reads, Is.EqualTo(1));
            _items.Remove("removed");
            _items.Add("added");
            _value = 2;
            _now = 10.499;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(1));
            Assert.That(((TestGhost)retained).Value, Is.EqualTo(1));
            IGhost found;
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "removed"), out found), Is.True);
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "added"), out found), Is.False);

            _now = 10.5;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(2));
            Assert.That(_realm.TryGetGhost(key, out found), Is.True);
            Assert.That(found, Is.SameAs(retained));
            Assert.That(((TestGhost)found).Value, Is.EqualTo(2));
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "removed"), out found), Is.False);
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "added"), out found), Is.True);
        }

        /// <summary>
        /// Late updates perform one read and schedule the next from the actual read time.
        /// </summary>
        [Test]
        public void DelayedUpdate_DoesNotCatchUp()
        {
            _realm.GetOrCreateAnchor("anchor", CreateSource().PollEvery(TimeSpan.FromSeconds(0.5)));
            _now = 100;
            _realm.Update();
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(2));
            _now = 100.499;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(2));
            _now = 100.5;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(3));
        }

        /// <summary>
        /// Detaching inside a read cannot change its scheduling configuration mid-call.
        /// </summary>
        [Test]
        public void Configuration_IsLockedDuringDetachedRead()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("anchor");
            PollingPresenceSource<string, TestGhost> source = CreateSource();
            source.ReadFrom(() =>
            {
                anchor.RemoveSource(source);
                Assert.Throws<InvalidOperationException>(() => source.PollEvery(TimeSpan.FromSeconds(1)));
                return Array.Empty<string>();
            });
            anchor.AddSource(source);
            Assert.That(source.IsAttached, Is.False);
            Assert.DoesNotThrow(() => source.PollEvery(TimeSpan.FromSeconds(1)));
        }

        /// <summary>
        /// Restart after failure reads immediately, recreates removed ghosts and starts a fresh interval.
        /// </summary>
        [Test]
        public void Restart_ResetsDeadline()
        {
            PollingPresenceSource<string, TestGhost> source = CreateSource().PollEvery(TimeSpan.FromSeconds(1));
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            IGhost original = _realm.Query().Single();
            _fail = true;
            _now = 11;
            ExpectedErrors.Verify(_realm.Update, "interval read failed");
            _now = 12;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(2));
            Assert.That(original.IsAvailable, Is.False);
            Assert.That(_realm.GetOwnedGhosts(source), Is.Empty);
            IGhost found;
            Assert.That(_realm.TryGetGhost(original.Key, out found), Is.False);

            _fail = false;
            _now += 0.25;
            int before = _reads;
            anchor.RestartSource(source);
            Assert.That(_reads, Is.EqualTo(before + 1));
            Assert.That(_realm.Query().Single(), Is.Not.SameAs(original));
            Assert.That(_realm.Query().Single().Key, Is.EqualTo(original.Key));
            Assert.That(source.LastError, Is.Null);
            _now += 0.75;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(before + 1));
            _now += 0.25;
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(before + 2));
        }

        /// <summary>
        /// A failed startup can retry without inheriting the failed attachment's deadline.
        /// </summary>
        [Test]
        public void FailedStartup_CanRetryImmediately()
        {
            PollingPresenceSource<string, TestGhost> source = CreateSource().PollEvery(TimeSpan.FromHours(1));
            Anchor anchor = _realm.GetOrCreateAnchor("anchor");
            _fail = true;
            Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source));
            Assert.That(source.IsAttached, Is.False);
            _fail = false;
            anchor.AddSource(source);
            Assert.That(_reads, Is.EqualTo(2));
            Assert.That(source.LastError, Is.Null);
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
            _realm.Update();
            Assert.That(_reads, Is.EqualTo(2));
        }

        /// <summary>
        /// The public builder uses real elapsed time even while scaled game time is paused.
        /// </summary>
        [UnityTest]
        public IEnumerator PublicClock_PollsWhileTimeScaleIsZero()
        {
            float previousTimeScale = Time.timeScale;
            int reads = 0;
            try
            {
                Time.timeScale = 0;
                PollingPresenceSource<string, TestGhost> source = new PollingPresenceSource<string, TestGhost>(Kind)
                    .PollEvery(TimeSpan.FromMilliseconds(100))
                    .ReadFrom(() =>
                    {
                        reads++;
                        return Array.Empty<string>();
                    })
                    .IdentifyBy(id => id)
                    .Apply((item, ghost) =>
                    {
                    });
                _realm.GetOrCreateAnchor("anchor", source);
                Assert.That(reads, Is.EqualTo(1));
                double deadline = Time.realtimeSinceStartupAsDouble + 3;
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

        private PollingPresenceSource<string, TestGhost> CreateSource()
        {
            // A controlled clock makes exact deadlines and long gaps deterministic.
            return new PollingPresenceSource<string, TestGhost>(Kind, () => _now)
                .ReadFrom(() =>
                {
                    _reads++;
                    if (_fail)
                    {
                        throw new InvalidOperationException("interval read failed");
                    }

                    return _items;
                })
                .IdentifyBy(id => id)
                .Apply((item, ghost) => ghost.Value = _value);
        }

        private sealed class TestGhost : Ghost
        {
            internal int Value;
        }
    }
}
