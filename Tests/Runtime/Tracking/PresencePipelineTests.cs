using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Covers detector reports and the realm-owned Presence lifecycle.
    /// </summary>
    public sealed class PresencePipelineTests
    {
        private static readonly Kind TrackedKind = new Kind("tests.presence.pipeline");
        private Realm _realm;

        /// <summary>Creates an isolated realm through its public constructor.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
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
        /// Reporting an identity from its old root's removal callback creates a distinct presence.
        /// Stale publication and delayed Unity destruction cannot replace or remove the new entity.
        /// </summary>
        [UnityTest]
        public IEnumerator RemovalCallback_CanRediscoverIdentityWithoutOldCleanupRemovingIt()
        {
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence original = detector.PublishMetadata("one", "Original entity");
            Ghost originalRoot = original.Root;
            _realm.Update();
            Presence replacement = null;
            RemovalCallback callback = originalRoot.gameObject.AddComponent<RemovalCallback>();
            callback.Disabled = () => replacement = detector.PublishMetadata("one", "Replacement entity");

            detector.Lose("one");

            Assert.That(original.IsRemoved, Is.True);
            Assert.That(original.Root, Is.Null);
            Assert.That(replacement, Is.Not.Null.And.Not.SameAs(original));
            Assert.That(replacement.Key, Is.EqualTo(original.Key));
            Ghost replacementRoot = replacement.Root;
            Assert.That(replacementRoot, Is.Not.SameAs(originalRoot));
            Assert.Throws<ArgumentException>(() => detector.PublishCached(originalRoot));
            Assert.That(_realm.TryGetPresence(original.Key, out Presence found), Is.True);
            Assert.That(found, Is.SameAs(replacement));
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(replacementRoot));

            yield return null;

            Assert.That(originalRoot == null, Is.True);
            Assert.That(_realm.TryGetPresence(original.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(replacement));
            Assert.That(_realm.TryGetGhost(original.Key, out IGhost root), Is.True);
            Assert.That(root, Is.SameAs(replacementRoot));
            Assert.That(replacement.IsAvailable, Is.True);
        }

        /// <summary>
        /// A module failure makes all data from its detector unavailable, including earlier successful reports.
        /// Consumers can inspect the original error without receiving a partially applied entity.
        /// </summary>
        [Test]
        public void ModuleFailure_RemovesDetectorPopulationAndPreservesError()
        {
            bool rejectData = false;
            InvalidOperationException failure = new InvalidOperationException("module rejected data");
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                presence.AddModule(new ActionModule(() =>
                {
                    root.Articulation = 42;
                    if (rejectData)
                    {
                        throw failure;
                    }
                }));
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence previous = detector.Publish("previous", default(Reading), null, null);
            _realm.Update();
            Assert.That(previous.IsAvailable, Is.True);

            rejectData = true;
            ExpectedErrors.Verify(() =>
            {
                Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    detector.Publish("broken", default(Reading), null, null)), Is.SameAs(failure));
            }, "module rejected data");

            Assert.That(detector.IsActive, Is.False);
            Assert.That(detector.LastError, Is.SameAs(failure));
            Assert.That(previous.IsRemoved, Is.True);
            Assert.That(_realm.TryGetPresence(new Key("sdk", TrackedKind, "broken"), out Presence ignored), Is.False);
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Modules finish applying a report before queries receive it. Advancing the realm inside a module
        /// is rejected; the report can still complete and notify consumers on the next update.
        /// </summary>
        [Test]
        public void Report_CompletesModuleDataBeforeNotifyingConsumers()
        {
            int arrivals = 0;
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                presence.AddModule(new ActionModule(() =>
                {
                    Assert.Throws<InvalidOperationException>(_realm.Update);
                    Assert.That(arrivals, Is.Zero);
                    root.Articulation = 7;
                }));
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            using (_realm.Query().OnAvailable(ghost =>
            {
                Assert.That(((ProbeGhost)ghost).Articulation, Is.EqualTo(7));
                arrivals++;
            }))
            {
                Presence presence = detector.Publish("one", default(Reading), null, null);
                Assert.That(arrivals, Is.Zero);
                Assert.That(presence.IsAvailable, Is.False);
                _realm.Update();
                Assert.That(arrivals, Is.EqualTo(1));
                Assert.That(detector.IsActive, Is.True);
                Assert.That(detector.LastError, Is.Null);
            }
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

        private sealed class RemovalCallback : MonoBehaviour
        {
            internal Action Disabled;

            private void OnDisable()
            {
                Action disabled = Disabled;
                Disabled = null;
                disabled?.Invoke();
            }
        }

        private sealed class Detector : PresenceDetector
        {
            internal Presence Publish(string entityId, Reading reading, string name, Variant? variant,
                params Type[] capabilities)
            {
                return Report(entityId, TrackedKind, reading, name, variant, capabilities);
            }

            internal void Lose(string entityId)
            {
                Disappear(TrackedKind, entityId);
            }

            internal void PublishCached(IGhost ghost)
            {
                MarkPublished(ghost);
            }

            internal Presence PublishMetadata(string entityId, string name)
            {
                return Detect(entityId, TrackedKind, name);
            }
        }
    }
}
