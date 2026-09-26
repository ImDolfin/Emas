using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Covers detector reports and the realm-owned Presence lifecycle.
    /// </summary>
    public sealed class PresencePipelineTests
    {
        private static readonly Kind TrackedKind = new Kind("tests.presence.pipeline");
        private Realm _realm;
        private double _now;

        /// <summary>Creates an isolated realm and deterministic clock.</summary>
        [SetUp]
        public void SetUp()
        {
            _now = 10;
            _realm = new Realm(() => _now);
        }

        /// <summary>Releases the realm and its tracked objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Ghost preparation is not a detector report and creates a Presence only when claimed.
        /// </summary>
        [Test]
        public void Prepare_DoesNotExposePresenceUntilDetection()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("sdk");
            ProbeGhost prepared = _realm.Prepare<ProbeGhost>("sdk", TrackedKind, "prepared");
            Key key = new Key("sdk", TrackedKind, "prepared");
            Assert.That(_realm.TryGetPresence(key, out Presence presence), Is.False);
            Assert.That(presence, Is.Null);
            Assert.That(prepared.IsAvailable, Is.False);

            Detector detector = new Detector();
            anchor.AddDetector(detector);
            Presence detected = detector.PublishMetadata("prepared", "Claimed car");
            Assert.That(detected.Root, Is.SameAs(prepared));
            Assert.That(_realm.TryGetPresence(key, out presence), Is.True);
            Assert.That(presence, Is.SameAs(detected));
            _realm.Update();
            Assert.That(detected.IsAvailable, Is.True);
        }
        /// <summary>
        /// Capability changes install modules once while updates retain the Presence and root.
        /// </summary>
        [Test]
        public void Report_InitializesModulesAndPreservesPresence()
        {
            int initializations = 0;
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                initializations++;
                PositionModule position;
                if (presence.HasCapability<IPositionCapability>() && !presence.TryGetModule(out position))
                {
                    root.gameObject.AddComponent<Spatial>();
                    presence.AddModule(new PositionModule());
                }

                ArticulationModule articulation;
                if (presence.HasCapability<IArticulationCapability>() && !presence.TryGetModule(out articulation))
                {
                    presence.AddModule(new ArticulationModule());
                }
            });

            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence first = detector.Publish("car-1", new Reading(new Double3(1, 2, 3), 7),
                "SUV one", new Variant("model-a"), typeof(IPositionCapability));
            ProbeGhost root = (ProbeGhost)first.Root;
            Assert.That(first.IsAvailable, Is.False);
            Assert.That(first.HasCapability<IPositionCapability>(), Is.True);
            Assert.That(first.HasCapability<IArticulationCapability>(), Is.False);
            Assert.That(first.Modules.Count, Is.EqualTo(1));
            Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(1, 2, 3)));
            Assert.That(root.Articulation, Is.Zero);
            _realm.Update();
            Assert.That(first.IsAvailable, Is.True);
            Assert.That(_realm.Query().Single(), Is.SameAs(root));

            Presence updated = detector.Publish("car-1", new Reading(new Double3(4, 5, 6), 12),
                "SUV renamed", new Variant("model-b"), typeof(IPositionCapability), typeof(IArticulationCapability));
            Assert.That(updated, Is.SameAs(first));
            Assert.That(updated.Root, Is.SameAs(root));
            Assert.That(updated.Name, Is.EqualTo("SUV renamed"));
            Assert.That(updated.Variant, Is.EqualTo(new Variant("model-b")));
            Assert.That(updated.HasCapability<IArticulationCapability>(), Is.True);
            Assert.That(updated.Modules.Count, Is.EqualTo(2));
            Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(4, 5, 6)));
            Assert.That(root.Articulation, Is.EqualTo(12));
            Assert.That(initializations, Is.EqualTo(2));

            Presence repeated = detector.Publish("car-1", new Reading(new Double3(7, 8, 9), 15),
                null, null, typeof(IPositionCapability), typeof(IArticulationCapability));
            Assert.That(repeated, Is.SameAs(first));
            Assert.That(repeated.Modules.Count, Is.EqualTo(2));
            Assert.That(initializations, Is.EqualTo(2));
            Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(7, 8, 9)));
            Assert.That(root.Articulation, Is.EqualTo(15));
            Assert.That(_realm.TryGetPresence(first.Key, out Presence found), Is.True);
            Assert.That(found, Is.SameAs(first));
        }

        /// <summary>
        /// Failed direct reports stop their detector and remove both partial and previously published roots.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Report_DirectFailureStopsDetectorAndRemovesPopulation(bool moduleFailure)
        {
            bool fail = false;
            Presence failedPresence = null;
            InvalidOperationException failure = new InvalidOperationException("report failed");
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                presence.AddModule(new ActionModule(() =>
                {
                    root.Articulation = 42;
                    if (fail && moduleFailure)
                    {
                        throw failure;
                    }
                }));
                if (fail)
                {
                    failedPresence = presence;
                    if (!moduleFailure)
                    {
                        throw failure;
                    }
                }
            });
            Detector detector = new Detector();
            Anchor anchor = _realm.GetOrCreateAnchor("sdk", detector);
            Presence previous = detector.Publish("previous", default(Reading), null, null);
            _realm.Update();
            Assert.That(previous.IsAvailable, Is.True);

            fail = true;
            ExpectedErrors.Verify(() =>
            {
                Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    detector.Publish("broken", default(Reading), null, null)), Is.SameAs(failure));
            }, "operation 'Report'.*kind 'tests.presence.pipeline'.*entity 'broken'.*report failed");

            Assert.That(detector.IsActive, Is.False);
            Assert.That(detector.IsAttached, Is.True);
            Assert.That(detector.Stops, Is.EqualTo(1));
            Assert.That(detector.LastError, Is.SameAs(failure));
            Assert.That(detector.LastErrorContext, Does.Contain("anchor 'sdk'").And.Contain("operation 'Report'")
                .And.Contain("kind 'tests.presence.pipeline'").And.Contain("entity 'broken'"));
            Assert.That(previous.IsRemoved, Is.True);
            Assert.That(failedPresence.IsRemoved, Is.True);
            Assert.That(_realm.TryGetPresence(failedPresence.Key, out Presence ignored), Is.False);
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(detector.Stops, Is.EqualTo(1));

            fail = false;
            anchor.RestartDetector(detector);
            Presence recovered = detector.Publish("recovered", default(Reading), null, null);
            _realm.Update();
            Assert.That(detector.LastError, Is.Null);
            Assert.That(detector.LastErrorContext, Is.Null);
            Assert.That(recovered.IsAvailable, Is.True);
        }

        /// <summary>
        /// Initializers and modules cannot run a realm update that exposes partially initialized roots.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Report_DirectFailureRejectsReentrantUpdateBeforePublication(bool moduleFailure)
        {
            int arrivals = 0;
            Presence failedPresence = null;
            InvalidOperationException failure = new InvalidOperationException("reentrant report failed");
            Action attemptUpdate = () =>
            {
                InvalidOperationException reentry = Assert.Throws<InvalidOperationException>(_realm.Update);
                Assert.That(reentry.Message, Does.Contain("cannot be reentrant"));
                Assert.That(arrivals, Is.Zero);
                throw failure;
            };
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                failedPresence = presence;
                if (moduleFailure)
                {
                    presence.AddModule(new ActionModule(attemptUpdate));
                }
                else
                {
                    attemptUpdate();
                }
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            using (_realm.Query().OnAvailable(ghost => arrivals++))
            {
                ExpectedErrors.Verify(() =>
                {
                    Assert.That(Assert.Throws<InvalidOperationException>(() =>
                        detector.Publish("broken", default(Reading), null, null)), Is.SameAs(failure));
                }, "operation 'Report'.*entity 'broken'.*reentrant report failed");

                Assert.That(detector.IsActive, Is.False);
                Assert.That(detector.LastError, Is.SameAs(failure));
                Assert.That(detector.Stops, Is.EqualTo(1));
                Assert.That(failedPresence.IsRemoved, Is.True);
                _realm.Update();
                Assert.That(_realm.Query().Count, Is.Zero);
                Assert.That(arrivals, Is.Zero);
            }
        }

        /// <summary>
        /// A startup report failure preserves the existing prepared root rollback contract.
        /// </summary>
        [Test]
        public void Report_StartupFailureRestoresPreparedRootWithoutLoggingTwice()
        {
            InvalidOperationException failure = new InvalidOperationException("startup report failed");
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                throw failure;
            });
            Anchor anchor = _realm.GetOrCreateAnchor("sdk");
            ProbeGhost prepared = _realm.Prepare<ProbeGhost>("sdk", TrackedKind, "prepared");
            Detector detector = new Detector();
            detector.Starting = () => detector.PublishMetadata("prepared", null);

            Assert.That(Assert.Throws<InvalidOperationException>(() => anchor.AddDetector(detector)), Is.SameAs(failure));
            Assert.That(detector.Stops, Is.EqualTo(1));
            Assert.That(detector.IsAttached, Is.False);
            Assert.That(detector.IsActive, Is.False);
            Assert.That(detector.LastError, Is.SameAs(failure));
            Assert.That(detector.LastErrorContext, Does.Contain("operation 'Report'").And.Contain("entity 'prepared'"));
            Assert.That(_realm.Prepare<ProbeGhost>("sdk", TrackedKind, "prepared"), Is.SameAs(prepared));
            _realm.Update();
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Existing update and dispatch failure boundaries clean up and log each failed report once.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Report_ManagedCallbackFailureUsesExistingBoundary(bool dispatched)
        {
            InvalidOperationException failure = new InvalidOperationException("managed report failed");
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                presence.AddModule(new ActionModule(() =>
                {
                    throw failure;
                }));
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Action publish = () => detector.Publish("broken", default(Reading), null, null);
            if (dispatched)
            {
                detector.Queue(publish);
            }
            else
            {
                detector.Updating = publish;
            }

            ExpectedErrors.Verify(_realm.Update, "operation 'Report'.*entity 'broken'.*managed report failed");
            Assert.That(detector.IsActive, Is.False);
            Assert.That(detector.IsAttached, Is.True);
            Assert.That(detector.LastError, Is.SameAs(failure));
            Assert.That(detector.Stops, Is.EqualTo(1));
            Assert.That(_realm.TryGetPresence(new Key("sdk", TrackedKind, "broken"), out Presence ignored), Is.False);
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// One detector's callback cannot defer another detector's direct report cleanup.
        /// </summary>
        [Test]
        public void Report_InsideAnotherDetectorCallbackStopsReportingDetector()
        {
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                throw new InvalidOperationException("nested report failed");
            });
            Detector caller = new Detector();
            Detector reporter = new Detector();
            _realm.GetOrCreateAnchor("caller", caller);
            _realm.GetOrCreateAnchor("reporter", reporter);
            caller.Updating = () => Assert.Throws<InvalidOperationException>(() => reporter.PublishMetadata("broken", null));

            ExpectedErrors.Verify(_realm.Update, "anchor 'reporter'.*operation 'Report'.*nested report failed");
            caller.Updating = null;
            Assert.That(caller.IsActive, Is.True);
            Assert.That(caller.LastError, Is.Null);
            Assert.That(reporter.IsActive, Is.False);
            Assert.That(reporter.Stops, Is.EqualTo(1));
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// A newer registration cannot rely on the older callback's failure boundary for cleanup.
        /// </summary>
        [Test]
        public void Report_NewRegistrationInsideOldCallbackUsesOwnFailureBoundary()
        {
            InvalidOperationException failure = new InvalidOperationException("replacement report failed");
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                throw failure;
            });
            Detector detector = new Detector();
            Anchor anchor = _realm.GetOrCreateAnchor("sdk", detector);
            detector.Updating = () =>
            {
                detector.Updating = null;
                anchor.RemoveDetector(detector);
                anchor.AddDetector(detector);
                Assert.That(Assert.Throws<InvalidOperationException>(() => detector.PublishMetadata("broken", null)),
                    Is.SameAs(failure));
            };

            ExpectedErrors.Verify(_realm.Update, "operation 'Report'.*entity 'broken'.*replacement report failed");
            Assert.That(detector.IsActive, Is.False);
            Assert.That(detector.LastError, Is.SameAs(failure));
            Assert.That(detector.Stops, Is.EqualTo(2));
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// An initializer failure from an obsolete registration cannot stop or poison its replacement.
        /// </summary>
        [Test]
        public void Report_InitializerReattachesBeforeThrowingPreservesNewRegistration()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("sdk");
            Detector detector = new Detector();
            InvalidOperationException failure = new InvalidOperationException("obsolete report failed");
            Presence replacement = null;
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                if (presence.Key.EntityId == "obsolete")
                {
                    anchor.RemoveDetector(detector);
                    anchor.AddDetector(detector);
                    replacement = detector.PublishMetadata("replacement", null);
                    throw failure;
                }
            });
            anchor.AddDetector(detector);

            Assert.That(Assert.Throws<InvalidOperationException>(() => detector.PublishMetadata("obsolete", null)),
                Is.SameAs(failure));
            Assert.That(detector.IsAttached && detector.IsActive, Is.True);
            Assert.That(detector.LastError, Is.Null);
            Assert.That(detector.LastErrorContext, Is.Null);
            Assert.That(detector.Stops, Is.EqualTo(1));
            _realm.Update();
            Assert.That(replacement.IsAvailable, Is.True);
            Assert.That(_realm.Query().Single(), Is.SameAs(replacement.Root));
        }

        /// <summary>
        /// Manifesting a Presence selects the requested view detail while retaining its root.
        /// </summary>
        [Test]
        public void Manifest_PresenceSelectsDetailAndKeepsRoot()
        {
            GameObject minimalPrefab = new GameObject("minimal view");
            GameObject fullPrefab = new GameObject("full view");
            ManifestationVariant variant = ScriptableObject.CreateInstance<ManifestationVariant>();
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                minimalPrefab.SetActive(false);
                fullPrefab.SetActive(false);
                variant.Configure(Variant.None, new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Minimal, minimalPrefab),
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, fullPrefab)
                });
                blueprint.Configure(TrackedKind, null, new[] { variant }, null);
                _realm.RegisterManifestationBlueprint(blueprint);
                Detector detector = new Detector();
                _realm.GetOrCreateAnchor("sdk", detector);
                Presence presence = detector.PublishMetadata("car-2", "SUV two");
                Ghost root = presence.Root;
                _realm.Update();
                Assert.That(root.GetComponentInChildren<View>(), Is.Null);

                View minimal = _realm.Manifest(presence, DetailLevel.Minimal);
                Assert.That(minimal, Is.Not.Null);
                Assert.That(minimal.gameObject.name, Is.EqualTo("minimal view"));
                Assert.That(minimal.Ghost, Is.SameAs(root));
                Assert.That(minimal.RequestedDetailLevel, Is.EqualTo(DetailLevel.Minimal));

                View full = _realm.Manifest(presence, DetailLevel.Full);
                Assert.That(full, Is.Not.Null);
                Assert.That(full.gameObject.name, Is.EqualTo("full view"));
                Assert.That(full.RequestedDetailLevel, Is.EqualTo(DetailLevel.Full));
                Assert.That(presence.Root, Is.SameAs(root));
                _realm.Demanifest(presence);
                Assert.That(root.GetComponentInChildren<View>(), Is.Null);
                Assert.That(presence.IsAvailable, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(minimalPrefab);
                UnityEngine.Object.DestroyImmediate(fullPrefab);
            }
        }

        /// <summary>
        /// A disappeared Presence recovers within grace and is removed at its next deadline.
        /// </summary>
        [Test]
        public void Disappear_GraceReusesPresenceThenRemovesAtDeadline()
        {
            Detector detector = new Detector
            {
                DisappearanceGracePeriod = TimeSpan.FromSeconds(2)
            };
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence first = detector.PublishMetadata("car-3", "SUV three");
            Ghost root = first.Root;
            Key key = first.Key;
            _realm.Update();
            Assert.That(first.IsAvailable, Is.True);

            detector.Lose("car-3");
            Assert.That(first.IsAvailable, Is.False);
            Assert.That(first.IsRemoved, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(_realm.TryGetPresence(key, out Presence missing), Is.True);
            Assert.That(missing, Is.SameAs(first));
            _now = 11;
            _realm.Update();
            Presence returned = detector.PublishMetadata("car-3", "SUV returned");
            _realm.Update();
            Assert.That(returned, Is.SameAs(first));
            Assert.That(returned.Root, Is.SameAs(root));
            Assert.That(returned.IsAvailable, Is.True);
            Assert.That(_realm.Query().Single(), Is.SameAs(root));

            detector.Lose("car-3");
            _now = 12.999;
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out missing), Is.True);
            _now = 13;
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out missing), Is.False);
            Assert.That(first.IsRemoved, Is.True);
            Assert.That(first.Root, Is.Null);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// A metadata-only report needs neither a blueprint nor an initializer.
        /// </summary>
        [Test]
        public void MetadataOnlyReport_UsesSilentDefaultRoot()
        {
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.PublishMetadata("silent", "Silent object");
            Assert.That(presence.Root, Is.Not.Null);
            Assert.That(presence.Name, Is.EqualTo("Silent object"));
            Assert.That(presence.HasCapability<IPositionCapability>(), Is.False);
            Assert.That(presence.Modules.Count, Is.Zero);
            _realm.Update();
            Assert.That(presence.IsAvailable, Is.True);
            Assert.That(_realm.Query().Single(), Is.SameAs(presence.Root));
            Assert.That(_realm.Manifest(presence, DetailLevel.Full), Is.Null);
            Assert.That(presence.Root.GetComponentInChildren<View>(), Is.Null);
        }

        private interface IPositionCapability
        {
        }

        private interface IArticulationCapability
        {
        }

        private struct Reading
        {
            internal Reading(Double3 position, int articulation)
            {
                Position = position;
                Articulation = articulation;
            }

            internal readonly Double3 Position;
            internal readonly int Articulation;
        }

        private sealed class ProbeGhost : Ghost
        {
            internal int Articulation { get; set; }
        }

        private sealed class PositionModule : EntityModule<Reading>
        {
            /// <inheritdoc />
            public override void Apply(Reading data)
            {
                Presence.Root.GetComponent<Spatial>().SetPosition(data.Position);
            }
        }

        private sealed class ArticulationModule : EntityModule<Reading>
        {
            /// <inheritdoc />
            public override void Apply(Reading data)
            {
                ((ProbeGhost)Presence.Root).Articulation = data.Articulation;
            }
        }

        private sealed class ActionModule : EntityModule<Reading>
        {
            private readonly Action _apply;

            internal ActionModule(Action apply)
            {
                _apply = apply;
            }

            /// <inheritdoc />
            public override void Apply(Reading data)
            {
                _apply();
            }
        }

        private sealed class Detector : PresenceDetector
        {
            internal Action Starting;
            internal Action Updating;
            internal int Stops;

            /// <inheritdoc />
            protected override void OnStart()
            {
                Starting?.Invoke();
            }

            /// <inheritdoc />
            protected override void OnUpdate()
            {
                Updating?.Invoke();
            }

            /// <inheritdoc />
            protected override void OnStop()
            {
                Stops++;
            }

            internal void Queue(Action action)
            {
                Dispatch(action);
            }

            internal Presence Publish(string entityId, Reading reading, string name, Variant? variant,
                params Type[] capabilities)
            {
                return Report(entityId, TrackedKind, reading, name, variant, capabilities);
            }

            internal Presence PublishMetadata(string entityId, string name)
            {
                return Detect(entityId, TrackedKind, name);
            }

            internal void Lose(string entityId)
            {
                Disappear(TrackedKind, entityId);
            }
        }
    }
}


