using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Checks public source health and collection snapshots across registration lifetimes.
    /// </summary>
    public sealed class SourceStatusTests
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
            ExpectedErrors.Verify(() => Assert.That(Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source)), Is.SameAs(primary)), "cleanup failure");
            Assert.That(source.LastError, Is.SameAs(primary));
            Assert.That(source.IsAttached || source.IsActive, Is.False);
            Assert.That(source.Stops, Is.EqualTo(1));
            source.Starting = () => Assert.That(source.LastError, Is.Null);
            source.Stopping = null;
            anchor.AddSource(source);
            Assert.That(source.IsActive && source.IsAttached, Is.True);
            anchor.RemoveSource(source);
            Assert.That(source.IsAttached || source.IsActive, Is.False);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(source.Stops, Is.EqualTo(2));
        }

        /// <summary>
        /// Update and queued failures stop only the owner and retain the primary error over cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RuntimeFailure_RetainsUnavailableGhostAndIsolatesHealthySource(bool dispatched)
        {
            InvalidOperationException failure = new InvalidOperationException("runtime failure");
            ProbeSource source = new ProbeSource();
            ProbeSource healthy = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("status", source, healthy);
            StatusGhost ghost = source.Publish("failed");
            StatusGhost other = healthy.Publish("healthy");
            _realm.Update();
            source.Stopping = () =>
            {
                throw new Exception("cleanup failure");
            };
            if (dispatched)
            {
                source.Enqueue(() =>
                {
                    throw failure;
                });
                source.Enqueue(() => Assert.Fail("Work after failure must be discarded."));
            }
            else
            {
                source.Updating = () =>
                {
                    throw failure;
                };
            }

            ExpectedErrors.Verify(_realm.Update, "runtime failure", "cleanup failure");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(other.IsAvailable && healthy.IsActive, Is.True);
            Assert.That(healthy.LastError, Is.Null);
            ProbeSource replacement = new ProbeSource();
            anchor.ReplaceSource(source, replacement);
            Assert.That(replacement.Publish("failed"), Is.SameAs(ghost));
            _realm.Update();
            Assert.That(ghost.IsAvailable, Is.True);
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.Stops, Is.EqualTo(1));
        }

        /// <summary>
        /// Normal teardown records a cleanup-only failure without preventing detachment.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CleanupFailure_IsRetainedAfterRemoveOrDispose(bool dispose)
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
            ExpectedErrors.Verify(() =>
            {
                if (dispose)
                {
                    anchor.Dispose();
                }
                else
                {
                    anchor.RemoveSource(source);
                }
            }, "cleanup only");

            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.IsAttached || source.IsActive, Is.False);
            Assert.That(source.Stops, Is.EqualTo(1));
        }

        /// <summary>
        /// Callback adapters retain mapping errors over throwing unsubscribe and clear health on restart.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CallbackCleanup_RecordsErrorWithoutReplacingMappingFailure(bool mappingFails)
        {
            Action<string> publish = null;
            Exception mapping = new Exception("mapping failure");
            Exception cleanup = new Exception("unsubscribe failure");
            CallbackPresenceSource<string, StatusGhost> source = new CallbackPresenceSource<string, StatusGhost>(new Kind("status"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                    if (mappingFails)
                    {
                        throw mapping;
                    }
                })
                .Listen((changed, removed) =>
                {
                    publish = changed;
                    return () =>
                    {
                        throw cleanup;
                    };
                });
            Anchor anchor = _realm.GetOrCreateAnchor("status", source);
            if (mappingFails)
            {
                publish("one");
                ExpectedErrors.Verify(_realm.Update, "mapping failure", "unsubscribe failure");
            }
            else
            {
                ExpectedErrors.Verify(() => anchor.RemoveSource(source), "unsubscribe failure");
            }

            Assert.That(source.LastError, Is.SameAs(mappingFails ? mapping : cleanup));
            anchor.RemoveSource(source);
            source.Listen((changed, removed) => null);
            anchor.AddSource(source);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(source.IsActive, Is.True);
        }

        /// <summary>
        /// Old queued work and callbacks called after reattachment cannot poison the new registration.
        /// </summary>
        [Test]
        public void StaleCallbacks_DoNotChangeRestartedStatus()
        {
            List<Action<string>> publishers = new List<Action<string>>();
            CallbackPresenceSource<string, StatusGhost> source = new CallbackPresenceSource<string, StatusGhost>(new Kind("status"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                })
                .Listen((publish, remove) =>
                {
                    publishers.Add(publish);
                    return null;
                });
            Anchor anchor = _realm.GetOrCreateAnchor("status", source);
            publishers[0](null);
            anchor.RemoveSource(source);
            anchor.AddSource(source);
            publishers[0](null);
            publishers[1]("current");
            _realm.Update();
            Assert.That(source.IsActive && source.IsAttached, Is.True);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("current"));
        }

        /// <summary>
        /// Cleanup returned by interrupted startup must not overwrite a nested reattachment's state.
        /// </summary>
        [Test]
        public void InterruptedStartup_OldCleanupCannotPoisonNewAttachment()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("status");
            int starts = 0;
            CallbackPresenceSource<string, StatusGhost> source = null;
            source = new CallbackPresenceSource<string, StatusGhost>(new Kind("status"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                })
                .Listen((publish, remove) =>
                {
                    if (++starts == 1)
                    {
                        anchor.RemoveSource(source);
                        anchor.AddSource(source);
                        return () =>
                        {
                            throw new Exception("obsolete cleanup");
                        };
                    }

                    publish("current");
                    return null;
                });
            ExpectedErrors.Verify(() => anchor.AddSource(source), "obsolete cleanup");
            _realm.Update();
            Assert.That(source.IsActive && source.IsAttached, Is.True);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("current"));
        }

        /// <summary>
        /// An obsolete startup failure cannot detach or overwrite a nested successful registration.
        /// </summary>
        [Test]
        public void StartupThrowsAfterReattachment_PreservesNewRegistration()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("status");
            int starts = 0;
            CallbackPresenceSource<string, StatusGhost> source = null;
            source = new CallbackPresenceSource<string, StatusGhost>(new Kind("status"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                })
                .Listen((publish, remove) =>
                {
                    if (++starts == 1)
                    {
                        anchor.RemoveSource(source);
                        anchor.AddSource(source);
                        throw new InvalidOperationException("obsolete startup");
                    }

                    publish("current");
                    return null;
                });
            Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source));
            _realm.Update();
            Assert.That(source.IsAttached && source.IsActive, Is.True);
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.LastErrorContext, Is.Null);
            Assert.That(anchor.Sources, Is.EquivalentTo(new[] { source }));
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("current"));
        }

        /// <summary>
        /// Snapshots cannot mutate the owner, retain their membership and become empty on disposed owners.
        /// </summary>
        [Test]
        public void Collections_AreCopiedReadOnlySnapshots()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("first", source);
            IReadOnlyList<Anchor> anchors = _realm.Anchors;
            IReadOnlyList<PresenceSource> sources = anchor.Sources;
            Assert.Throws<NotSupportedException>(() => ((IList<Anchor>)anchors).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<PresenceSource>)sources).Clear());
            ProbeSource replacement = new ProbeSource();
            anchor.ReplaceSource(source, replacement);
            replacement.Stopping = () => Assert.That(_realm.Anchors, Is.Empty);
            _realm.GetOrCreateAnchor("second", new ProbeSource
            {
                Stopping = () => Assert.That(_realm.Anchors, Is.Empty)
            });
            Assert.That(anchors.Count, Is.EqualTo(1));
            Assert.That(sources[0], Is.SameAs(source));
            Assert.That(anchor.Sources[0], Is.SameAs(replacement));
            Assert.That(_realm.Anchors.Count, Is.EqualTo(2));
            _realm.Dispose();
            Assert.That(_realm.Anchors, Is.Empty);
            Assert.That(anchor.Sources, Is.Empty);
            Assert.That(anchors.Count, Is.EqualTo(1));
            Assert.That(sources.Count, Is.EqualTo(1));
        }

        private sealed class ProbeSource : PresenceSource
        {
            internal Action Starting;
            internal Action Updating;
            internal Action Stopping;
            internal int Stops;

            internal StatusGhost Publish(string id)
            {
                return GetOrCreate<StatusGhost>(id, new Kind("status"));
            }

            internal void Enqueue(Action action)
            {
                Dispatch(action);
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
