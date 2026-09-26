using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies explicit source restarts preserve identities and isolate registration lifetimes.
    /// </summary>
    public sealed class RestartTests
    {
        private static readonly Kind Kind = new Kind("restart");
        private Realm _realm;

        /// <summary>
        /// Creates an isolated realm.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
        }

        /// <summary>
        /// Releases all tracked objects.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Restart invalidates old work and recreates ghosts removed by a previous source failure.
        /// </summary>
        [TestCase(false, TestName = "Restart_PreservesIdentityAndDiscardsQueuedWork")]
        [TestCase(true, TestName = "Restart_RecoversPopulationAfterFailure")]
        public void Restart_DiscardsOldWorkAndRecoversPopulation(bool fail)
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            TestGhost ghost = source.Publish("one");
            _realm.Update();
            if (fail)
            {
                source.Updating = () =>
                {
                    throw new InvalidOperationException("update failed");
                };
                ExpectedErrors.Verify(_realm.Update, "update failed");
                Assert.That(source.Population, Is.Empty);
                IGhost removed;
                Assert.That(_realm.TryGetGhost(ghost.Key, out removed), Is.False);
                source.Updating = null;
            }
            else
            {
                source.Enqueue(() => Assert.Fail("Old work ran after restart."));
            }

            anchor.RestartDetector(source);
            Assert.That(source.Starts, Is.EqualTo(2));
            Assert.That(source.Stops, Is.EqualTo(1));
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(source.IsAttached && source.IsActive, Is.True);
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(anchor.Detectors, Is.EqualTo(new[] { source }));
            TestGhost recovered = source.Publish("one");
            if (fail)
            {
                Assert.That(recovered, Is.Not.SameAs(ghost));
            }
            else
            {
                Assert.That(recovered, Is.SameAs(ghost));
            }

            _realm.Update();
            Assert.That(recovered.IsAvailable, Is.True);
        }

        /// <summary>
        /// Failed restart removes its population while keeping the source attached for a later retry.
        /// </summary>
        [Test]
        public void RestartFailure_RemovesGhostsAndCanRetry()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            TestGhost ghost = source.Publish("one");
            _realm.Update();
            InvalidOperationException failure = new InvalidOperationException("retry failed");
            source.Starting = () =>
            {
                Assert.That(source.Publish("one"), Is.SameAs(ghost));
                source.Publish("partial");
                throw failure;
            };
            Assert.That(Assert.Throws<InvalidOperationException>(() => anchor.RestartDetector(source)), Is.SameAs(failure));
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("OnStart"));
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(source.Population, Is.Empty);
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            IGhost found;
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.False);
            Assert.That(_realm.TryGetGhost(new Key("restart", Kind, "partial"), out found), Is.False);
            source.Starting = () => source.Publish("one");
            anchor.RestartDetector(source);
            Assert.That(_realm.Query().Single(), Is.Not.SameAs(ghost));
            Assert.That(_realm.Query().Single().Key, Is.EqualTo(ghost.Key));
            Assert.That(source.Starts, Is.EqualTo(3));
            Assert.That(source.Stops, Is.EqualTo(2));
            Assert.That(source.LastError, Is.Null);
        }

        /// <summary>
        /// Cleanup failures are logged once and do not prevent a new attachment.
        /// </summary>
        [Test]
        public void Restart_CleanupFailureDoesNotPreventStartup()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            source.Stopping = () =>
            {
                throw new InvalidOperationException("cleanup failed");
            };
            ExpectedErrors.Verify(() => anchor.RestartDetector(source), "operation 'OnStop'.*cleanup failed");
            source.Stopping = null;
            Assert.That(source.IsActive, Is.True);
            Assert.That(source.Stops, Is.EqualTo(1));
            Assert.That(source.LastError, Is.Null);
        }

        /// <summary>
        /// Retained callback delegates and already queued work cannot enter a new registration.
        /// </summary>
        [Test]
        public void CallbackRestart_RejectsOldCallbacksAndCleansUpOnce()
        {
            List<Action<string>> publishers = new List<Action<string>>();
            int cleanups = 0;
            CallbackPresenceDetector<string, TestGhost> source = new CallbackPresenceDetector<string, TestGhost>(Kind)
                .IdentifyBy(id => id)
                .Apply((id, ghost) =>
                {
                })
                .Listen((publish, remove) =>
                {
                    publishers.Add(publish);
                    publish("one");
                    return () => cleanups++;
                });
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            _realm.Update();
            IGhost ghost = _realm.Query().Single();
            publishers[0](null);
            anchor.RestartDetector(source);
            publishers[0](null);
            Assert.That(ghost.IsAvailable, Is.False);
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(cleanups, Is.EqualTo(1));
            anchor.Dispose();
            Assert.That(cleanups, Is.EqualTo(2));
        }

        /// <summary>
        /// Captured dispatchers ignore old callbacks even after the same source starts again.
        /// </summary>
        [Test]
        public void CapturedDispatcher_RejectsStaleCallbacksAfterRestart()
        {
            Probe source = new Probe();
            Assert.Throws<InvalidOperationException>(() => source.Capture());
            source.Starting = () => source.Captured = source.Capture();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            Action<Action> original = source.Captured;
            int calls = 0;
            original(() => calls++);

            anchor.RestartDetector(source);
            Action<Action> current = source.Captured;
            original(() => calls += 100);
            current(() => calls += 10);
            _realm.Update();
            Assert.That(calls, Is.EqualTo(10));

            anchor.RemoveDetector(source);
            current(() => calls += 100);
            _realm.Update();
            Assert.That(calls, Is.EqualTo(10));
        }

        private sealed class Probe : PresenceDetector
        {
            internal Action Starting;
            internal Action Updating;
            internal Action Stopping;
            internal Action<Action> Captured;
            internal int Starts;
            internal int Stops;

            internal IReadOnlyList<IGhost> Population => OwnedGhosts;

            internal TestGhost Publish(string id)
            {
                return GetOrCreate<TestGhost>(id, Kind);
            }

            internal void Enqueue(Action action)
            {
                Dispatch(action);
            }

            internal Action<Action> Capture()
            {
                return CaptureDispatcher();
            }

            protected override void OnStart()
            {
                Starts++;
                Starting?.Invoke();
            }

            protected override void OnUpdate()
            {
                Updating?.Invoke();
            }

            protected override void OnStop()
            {
                Stops++;
                Stopping?.Invoke();
            }
        }

        private sealed class TestGhost : Ghost
        {
        }
    }
}
