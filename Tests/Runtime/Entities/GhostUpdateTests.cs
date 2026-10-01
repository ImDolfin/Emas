using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Verifies the realm-driven lifecycle hook for application Ghost behavior.</summary>
    public sealed class GhostUpdateTests
    {
        private static readonly Kind TestKind = new Kind("tests.ghost.update");
        private Realm _realm;

        /// <summary>Creates an isolated realm for each lifecycle scenario.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
        }

        /// <summary>Releases the realm and its population.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>All readers precede Ghost behavior, which precedes projection and query notification.</summary>
        [Test]
        public void OnUpdate_UsesFreshModulesBeforeReferenceProjectionAndQueries()
        {
            int reads = 0;
            _realm.RegisterPresenceInitializer<PipelineGhost>(TestKind, (presence, root) =>
            {
                root.GetComponent<PipelinePositionModule>().Bind(() =>
                {
                    reads++;
                    return new Double3(presence.Key.EntityId == "origin" ? 1000 : 1010, 0, 0);
                });
                root.Updating = () =>
                {
                    Assert.That(reads, Is.EqualTo(2), "Every module reader must finish before any Ghost hook.");
                    Spatial spatial = root.GetComponent<Spatial>();
                    spatial.SetCartesianPosition(spatial.Position + new Double3(presence.Key.EntityId == "origin" ? 2 : 5, 0, 0));
                };
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            _realm.ReferenceFrame = new ReferenceFrame { FollowedGhost = new Key("sdk", TestKind, "origin") };
            PipelineGhost target = (PipelineGhost)detector.Arrive("target").Root;
            PipelineGhost origin = (PipelineGhost)detector.Arrive("origin").Root;
            int arrivals = 0;
            using (_realm.Query().OnAvailable(ghost =>
            {
                Assert.That(((PipelineGhost)ghost).UpdateCount, Is.EqualTo(1));
                Assert.That(((Ghost)ghost).transform.position,
                    Is.EqualTo(ghost.Key.EntityId == "origin" ? Vector3.zero : new Vector3(13, 0, 0)));
                arrivals++;
            }))
            {
                Assert.That(target.gameObject.activeSelf, Is.False);
                _realm.Update();
                Assert.That(arrivals, Is.EqualTo(2));
                Assert.That(origin.UpdateCount, Is.EqualTo(1));
                reads = 0;
                _realm.Update();
                Assert.That(target.UpdateCount, Is.EqualTo(2));
                Assert.That(origin.UpdateCount, Is.EqualTo(2));
                Assert.That(target.transform.position, Is.EqualTo(new Vector3(13, 0, 0)));
            }
        }

        /// <summary>Only realm updates invoke the hook; startup, view requests and rejected nested updates do not.</summary>
        [Test]
        public void OnUpdate_RunsOncePerUpdateWithoutStartupOrViewRequestTicks()
        {
            PipelineGhost ghost = null;
            Detector detector = new Detector();
            detector.Starting = () => ghost = detector.Publish("one");
            _realm.GetOrCreateAnchor("sdk", detector);
            Assert.That(ghost.IsAvailable, Is.True);
            Assert.That(ghost.UpdateCount, Is.Zero);
            ghost.Updating = () =>
            {
                Assert.Throws<InvalidOperationException>(() => _realm.Update());
                _realm.Manifest(ghost);
            };
            _realm.Update();
            Assert.That(ghost.UpdateCount, Is.EqualTo(1));
            _realm.Manifest(ghost);
            _realm.Demanifest(ghost);
            Assert.That(ghost.UpdateCount, Is.EqualTo(1));
            _realm.Update();
            Assert.That(ghost.UpdateCount, Is.EqualTo(2));
        }

        /// <summary>Prepared, disabled and disappeared Ghosts skip behavior until owned, enabled and present.</summary>
        [Test]
        public void OnUpdate_SkipsIneligibleGhostsAndResumesAfterRediscovery()
        {
            Detector detector = new Detector { DisappearanceGracePeriod = TimeSpan.FromMinutes(1) };
            _realm.GetOrCreateAnchor("sdk", detector);
            PipelineGhost ghost = _realm.Prepare<PipelineGhost>("sdk", TestKind, "one");
            _realm.Update();
            Assert.That(ghost.UpdateCount, Is.Zero);
            Assert.That(detector.Publish("one"), Is.SameAs(ghost));
            ghost.enabled = false;
            _realm.Update();
            Assert.That(ghost.UpdateCount, Is.Zero);
            ghost.enabled = true;
            _realm.Update();
            Assert.That(ghost.UpdateCount, Is.EqualTo(1));
            detector.Lose("one");
            _realm.Update();
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.UpdateCount, Is.EqualTo(1));
            Assert.That(detector.Publish("one"), Is.SameAs(ghost));
            _realm.Update();
            Assert.That(ghost.UpdateCount, Is.EqualTo(2));
        }

        /// <summary>A failing hook cleans up its detector population without preventing other detectors from updating.</summary>
        [Test]
        public void OnUpdate_FailureStopsOnlyTheOwningDetector()
        {
            Detector failing = new Detector();
            Detector healthy = new Detector();
            _realm.GetOrCreateAnchor("failed", failing);
            _realm.GetOrCreateAnchor("healthy", healthy);
            PipelineGhost bad = failing.Publish("bad");
            PipelineGhost sibling = failing.Publish("sibling");
            PipelineGhost good = healthy.Publish("good");
            InvalidOperationException error = new InvalidOperationException("ghost calculation failed");
            bad.Updating = () => { throw error; };
            ExpectedErrors.Verify(_realm.Update, "operation 'Ghost.OnUpdate'.*entity 'bad'.*ghost calculation failed");
            Assert.That(failing.IsActive, Is.False);
            Assert.That(failing.LastError, Is.SameAs(error));
            Assert.That(failing.LastErrorContext, Does.Contain("Ghost.OnUpdate").And.Contain("bad"));
            Assert.That(sibling.IsAvailable, Is.False);
            Assert.That(healthy.IsActive, Is.True);
            Assert.That(good.UpdateCount, Is.EqualTo(1));
            Assert.That(_realm.Query().Single(), Is.SameAs(good));
        }

        /// <summary>Disposal during a Ghost callback prevents later callbacks and projection on destroyed population.</summary>
        [Test]
        public void OnUpdate_DisposalStopsRemainingCallbacks()
        {
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            PipelineGhost first = detector.Publish("first");
            PipelineGhost second = detector.Publish("second");
            first.Updating = _realm.Dispose;
            second.Updating = _realm.Dispose;
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
            _realm.Update();
            Assert.That(first.UpdateCount + second.UpdateCount, Is.EqualTo(1));
        }

        private sealed class Detector : PresenceDetector
        {
            internal Action Starting;

            /// <inheritdoc />
            protected override void OnStart()
            {
                Starting?.Invoke();
            }

            internal Presence Arrive(string id)
            {
                return Detect(id, TestKind);
            }

            internal PipelineGhost Publish(string id)
            {
                return GetOrCreate<PipelineGhost>(id, TestKind);
            }

            internal void Lose(string id)
            {
                Disappear(TestKind, id);
            }
        }
    }
}
