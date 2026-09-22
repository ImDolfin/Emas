using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

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
        /// Restart retains identity, invalidates old work and recovers both active and failed sources.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Restart_PreservesIdentityAndDiscardsOldWork(bool fail)
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
                source.Updating = null;
            }
            else
            {
                source.Enqueue(() => Assert.Fail("Old work ran after restart."));
            }

            anchor.RestartSource(source);
            Assert.That(source.Starts, Is.EqualTo(2));
            Assert.That(source.Stops, Is.EqualTo(1));
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(source.IsAttached && source.IsActive, Is.True);
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(anchor.Sources, Is.EqualTo(new[] { source }));
            Assert.That(source.Publish("one"), Is.SameAs(ghost));
            _realm.Update();
            Assert.That(ghost.IsAvailable, Is.True);
        }

        /// <summary>
        /// Failed restart leaves identities attached and unavailable until a later successful retry.
        /// </summary>
        [Test]
        public void RestartFailure_RetainsGhostsAndCanRetry()
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
            Assert.That(Assert.Throws<InvalidOperationException>(() => anchor.RestartSource(source)), Is.SameAs(failure));
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("OnStart"));
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(source.Population.Count, Is.EqualTo(2));
            source.Starting = () => source.Publish("one");
            anchor.RestartSource(source);
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
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
            ExpectedErrors.Verify(() => anchor.RestartSource(source), "operation 'OnStop'.*cleanup failed");
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
            CallbackPresenceSource<string, TestGhost> source = new CallbackPresenceSource<string, TestGhost>(Kind)
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
            anchor.RestartSource(source);
            publishers[0](null);
            Assert.That(ghost.IsAvailable, Is.False);
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(cleanups, Is.EqualTo(1));
            anchor.Dispose();
            Assert.That(cleanups, Is.EqualTo(2));
        }

        /// <summary>
        /// Rejects invalid owners and recursive restarts during startup or cleanup.
        /// </summary>
        [Test]
        public void Restart_ValidatesOwnerAndLifecycle()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("restart");
            Probe source = new Probe();
            Assert.Throws<ArgumentNullException>(() => anchor.RestartSource(null));
            Assert.Throws<InvalidOperationException>(() => anchor.RestartSource(source));
            source.Starting = () => Assert.Throws<InvalidOperationException>(() => anchor.RestartSource(source));
            source.Stopping = () => Assert.Throws<InvalidOperationException>(() => anchor.RestartSource(source));
            anchor.AddSource(source);
            anchor.RestartSource(source);
            source.Stopping = null;
            anchor.Dispose();
            Assert.Throws<ObjectDisposedException>(() => anchor.RestartSource(source));
        }

        /// <summary>
        /// Cleanup may remove the owner, cancelling the restart without recreating tracking.
        /// </summary>
        [Test]
        public void Restart_StopsWhenCleanupDisposesAnchor()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            source.Stopping = anchor.Dispose;
            anchor.RestartSource(source);
            Assert.That(source.Starts, Is.EqualTo(1));
            Assert.That(source.Stops, Is.EqualTo(1));
            Assert.That(source.IsAttached, Is.False);
            Assert.That(_realm.Anchors, Is.Empty);
        }

        /// <summary>
        /// A registration established during cleanup survives the older detach operation.
        /// </summary>
        [Test]
        public void Restart_PreservesReattachmentFromCleanup()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            source.Stopping = () =>
            {
                source.Stopping = null;
                anchor.RemoveSource(source);
                anchor.AddSource(source);
            };
            anchor.RestartSource(source);
            Assert.That(source.Starts, Is.EqualTo(2));
            Assert.That(source.IsAttached && source.IsActive, Is.True);
            Assert.That(anchor.Sources, Is.EqualTo(new[] { source }));
            source.Publish("new");
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Failure deactivation cannot restart the source before its old cleanup has completed.
        /// </summary>
        [Test]
        public void Restart_RejectsReentryDuringFailureDeactivation()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("restart", source);
            TestGhost ghost = source.Publish("one");
            RestartOnDisable observer = ghost.gameObject.AddComponent<RestartOnDisable>();
            _realm.Update();
            int attempts = 0;
            observer.Stopping = () =>
            {
                attempts++;
                Assert.Throws<InvalidOperationException>(() => anchor.RestartSource(source));
            };
            source.Updating = () =>
            {
                throw new InvalidOperationException("update failed");
            };
            ExpectedErrors.Verify(_realm.Update, "update failed");
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(source.Stops, Is.EqualTo(1));
            Assert.That(source.Starts, Is.EqualTo(1));
            Assert.That(source.IsActive, Is.False);
            observer.Stopping = null;
        }

        private sealed class RestartOnDisable : MonoBehaviour
        {
            internal Action Stopping;

            private void OnDisable()
            {
                Stopping?.Invoke();
            }
        }

        private sealed class Probe : PresenceSource
        {
            internal Action Starting;
            internal Action Updating;
            internal Action Stopping;
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
