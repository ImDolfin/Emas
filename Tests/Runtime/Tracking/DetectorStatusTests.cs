using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Checks public source health and collection snapshots across registration lifetimes.
    /// </summary>
    public sealed class DetectorStatusTests
    {
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
        /// Releases all sources and scene objects.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Startup sees an active attachment; failure is retained after rollback and cleared before retry.
        /// </summary>
        [Test]
        public void StartupFailure_RetainsPrimaryErrorAndClearsBeforeRetry()
        {
            InvalidOperationException primary = new InvalidOperationException("startup failure");
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("status");
            Assert.That(source.IsAttached, Is.False);
            Assert.That(source.IsActive, Is.False);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            source.Starting = () =>
            {
                Assert.That(source.IsAttached && source.IsActive, Is.True);
                Assert.That(source.LastError, Is.Null);
                Assert.That(source.LastErrorContext, Is.Null);
                throw primary;
            };
            source.Stopping = () =>
            {
                throw new Exception("cleanup failure");
            };
            ExpectedErrors.Verify(() => Assert.That(Assert.Throws<InvalidOperationException>(() => anchor.AddDetector(source)), Is.SameAs(primary)), "cleanup failure");
            Assert.That(source.LastError, Is.SameAs(primary));
            Assert.That(source.IsAttached || source.IsActive, Is.False);
            Assert.That(source.Stops, Is.EqualTo(1));
            source.Starting = () => Assert.That(source.LastError, Is.Null);
            source.Stopping = null;
            anchor.AddDetector(source);
            Assert.That(source.IsActive && source.IsAttached, Is.True);
            anchor.RemoveDetector(source);
            Assert.That(source.IsAttached || source.IsActive, Is.False);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(source.Stops, Is.EqualTo(2));
        }

        /// <summary>
        /// An update failure stops only its detector and retains the primary error if cleanup also fails.
        /// </summary>
        [Test]
        public void UpdateFailure_RemovesGhostAndIsolatesHealthySource()
        {
            InvalidOperationException failure = new InvalidOperationException("runtime failure");
            ProbeSource source = new ProbeSource();
            ProbeSource healthy = new ProbeSource();
            int healthyUpdates = 0;
            healthy.Updating = () => healthyUpdates++;
            Anchor anchor = _realm.GetOrCreateAnchor("status", source, healthy);
            StatusGhost ghost = source.Publish("failed");
            StatusGhost other = healthy.Publish("healthy");
            _realm.Update();
            source.Stopping = () =>
            {
                throw new Exception("cleanup failure");
            };
            source.Updating = () =>
            {
                throw failure;
            };

            ExpectedErrors.Verify(_realm.Update, "runtime failure", "cleanup failure");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            IGhost found;
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.False);
            Assert.That(found, Is.Null);
            _realm.Update();
            Assert.That(source.Stops, Is.EqualTo(1));
            Assert.That(other.IsAvailable && healthy.IsActive, Is.True);
            Assert.That(healthyUpdates, Is.EqualTo(3));
            Assert.That(_realm.Query().Single(), Is.SameAs(other));
            Assert.That(healthy.LastError, Is.Null);
            ProbeSource replacement = new ProbeSource();
            anchor.ReplaceDetector(source, replacement);
            StatusGhost recovered = replacement.Publish("failed");
            Assert.That(recovered, Is.Not.SameAs(ghost));
            _realm.Update();
            Assert.That(recovered.IsAvailable, Is.True);
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(recovered));
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.Stops, Is.EqualTo(1));
        }

        /// <summary>
        /// A cleanup exception remains available for diagnostics without keeping the detector attached.
        /// </summary>
        [Test]
        public void CleanupFailure_IsRetainedAfterDetachment()
        {
            Exception failure = new Exception("cleanup only");
            ProbeSource source = new ProbeSource
            {
                Stopping = () =>
                {
                    throw failure;
                }
            };
            Anchor anchor = _realm.GetOrCreateAnchor("status", source);
            ExpectedErrors.Verify(() => anchor.RemoveDetector(source), "cleanup only");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("OnStop"));
            Assert.That(source.IsAttached || source.IsActive, Is.False);
            Assert.That(source.Stops, Is.EqualTo(1));
        }

        /// <summary>
        /// Captured anchor and detector lists retain their membership while fresh lists reflect replacement and disposal.
        /// </summary>
        [Test]
        public void Collections_CaptureMembershipWithoutFollowingOwnerChanges()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("first", source);
            IReadOnlyList<Anchor> anchors = _realm.Anchors;
            IReadOnlyList<PresenceDetector> sources = anchor.Detectors;
            ProbeSource replacement = new ProbeSource();
            anchor.ReplaceDetector(source, replacement);
            _realm.GetOrCreateAnchor("second", new ProbeSource());
            Assert.That(anchors.Count, Is.EqualTo(1));
            Assert.That(sources[0], Is.SameAs(source));
            Assert.That(anchor.Detectors[0], Is.SameAs(replacement));
            Assert.That(_realm.Anchors.Count, Is.EqualTo(2));
            _realm.Dispose();
            Assert.That(_realm.Anchors, Is.Empty);
            Assert.That(anchor.Detectors, Is.Empty);
            Assert.That(anchors.Count, Is.EqualTo(1));
            Assert.That(sources.Count, Is.EqualTo(1));
        }

        private sealed class ProbeSource : PresenceDetector
        {
            internal Action Starting;
            internal Action Updating;
            internal Action Stopping;
            internal int Stops;

            internal StatusGhost Publish(string id)
            {
                return GetOrCreate<StatusGhost>(id, new Kind("status"));
            }

            protected override void OnStart()
            {
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

        /// <summary>
        /// A minimal component used to test source identity and availability.
        /// </summary>
        public sealed class StatusGhost : Ghost
        {
        }
    }
}
