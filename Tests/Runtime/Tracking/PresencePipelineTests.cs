using System;
using System.Collections;
using System.Runtime.CompilerServices;
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
        /// Preparation exposes a reusable root to identity lookup; detection claims it, and only an update makes it queryable.
        /// </summary>
        [Test]
        public void Prepare_DoesNotExposePresenceUntilDetection()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("sdk");
            PipelineGhost prepared = _realm.Prepare<PipelineGhost>("sdk", TrackedKind, "prepared");
            Key key = new Key("sdk", TrackedKind, "prepared");
            Assert.That(_realm.TryGetPresence(key, out Presence presence), Is.False);
            Assert.That(presence, Is.Null);
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_realm.TryGetGhost(key, out IGhost found), Is.True);
            Assert.That(found, Is.SameAs(prepared));
            Assert.That(_realm.Query().Count, Is.Zero);

            Detector detector = new Detector();
            anchor.AddDetector(detector);
            Presence detected = detector.PublishMetadata("prepared", "Claimed car");
            Assert.That(detected.Root, Is.SameAs(prepared));
            Assert.That(_realm.TryGetPresence(key, out presence), Is.True);
            Assert.That(presence, Is.SameAs(detected));
            Assert.That(detected.IsAvailable, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
            _realm.Update();
            Assert.That(detected.IsAvailable, Is.True);
            Assert.That(_realm.Query().OfKind(TrackedKind).Single(), Is.SameAs(prepared));
        }

        /// <summary>
        /// Ghost-defined modules read the latest mapped values without additional detections,
        /// and all initial values are ready before query consumers see the entity.
        /// </summary>
        [Test]
        public void Modules_UpdateFromBoundReadersWithoutDetectorReports()
        {
            Reading proxy = new Reading(new Double3(1, 2, 3), 7);
            int initializations = 0;
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                initializations++;
                root.GetComponent<PipelinePositionModule>().Bind(() => ((Reading)presence.Source).Position);
                root.GetComponent<PipelineArticulationModule>().Bind(() => ((Reading)presence.Source).Articulation);
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            int arrivals = 0;
            using (_realm.Query().OnAvailable(ghost =>
            {
                Assert.That(ghost.GetRequired<Spatial>().Position, Is.EqualTo(proxy.Position));
                Assert.That(((PipelineGhost)ghost).Articulation, Is.EqualTo(proxy.Articulation));
                arrivals++;
            }))
            {
                Presence presence = detector.Arrive("one", proxy);
                Ghost root = presence.Root;
                Assert.That(root.GetComponents<EntityModule>().Length, Is.EqualTo(3));
                Assert.That(presence.IsAvailable, Is.False);
                _realm.Update();
                Assert.That(arrivals, Is.EqualTo(1));

                proxy.Position = new Double3(4, 5, 6);
                proxy.Articulation = 12;
                _realm.Update();
                Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(proxy.Position));
                Assert.That(((PipelineGhost)root).Articulation, Is.EqualTo(12));
                Assert.That(_realm.TryGetPresence(presence.Key, out Presence found), Is.True);
                Assert.That(found, Is.SameAs(presence));
                Assert.That(found.Root, Is.SameAs(root));
                Assert.That(initializations, Is.EqualTo(1));
                Assert.That(arrivals, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A presence exposes its supplied proxy or SDK before initialization; changing that
        /// object reconnects existing modules, while repeated metadata preserves the binding.
        /// </summary>
        [Test]
        public void Detect_SourceReplacementReinitializesTheSameGhost()
        {
            int initializations = 0;
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                initializations++;
                PipelinePositionModule module = root.GetComponent<PipelinePositionModule>();
                if (presence.Source is Reading)
                {
                    module.Bind(() => ((Reading)presence.Source).Position);
                }
                else
                {
                    module.Bind(() => ((PositionSdk)presence.Source).Position);
                }
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Reading proxy = new Reading(new Double3(1, 2, 3), 0);
            Presence presence = detector.Arrive("one", proxy);
            Ghost root = presence.Root;
            Assert.That(presence.Source, Is.SameAs(proxy));
            _realm.Update();
            detector.Arrive("one", proxy);
            detector.PublishMetadata("one", "Renamed");
            Assert.That(initializations, Is.EqualTo(1));
            Assert.That(presence.Source, Is.SameAs(proxy));

            PositionSdk sdk = new PositionSdk { Position = new Double3(4, 5, 6) };
            Assert.That(detector.Arrive("one", sdk), Is.SameAs(presence));
            Assert.That(presence.Root, Is.SameAs(root));
            Assert.That(initializations, Is.EqualTo(2));
            Assert.That(presence.Source, Is.SameAs(sdk));
            _realm.Update();
            Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(sdk.Position));
            detector.Lose("one");
            Assert.That(presence.Source, Is.Null);
            Assert.That(sdk.IsDisposed, Is.False, "SDK ownership remains with the application.");
            GC.KeepAlive(proxy);
            GC.KeepAlive(sdk);
        }

        /// <summary>
        /// Retaining a Presence does not keep an otherwise unreferenced managed source alive.
        /// </summary>
        [Test]
        public void Source_DoesNotKeepManagedObjectsAlive()
        {
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = DetectTemporarySource(detector);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(presence.Source, Is.Null);
            Assert.That(presence.IsRemoved, Is.False);
        }

        /// <summary>
        /// A destroyed Unity object resolves to null even while its managed wrapper remains alive.
        /// </summary>
        [Test]
        public void Source_TreatsDestroyedUnityObjectsAsMissing()
        {
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            GameObject source = new GameObject("SDK proxy");
            try
            {
                Presence presence = detector.Arrive("one", source);
                Assert.That(presence.Source, Is.SameAs(source));
                UnityEngine.Object.DestroyImmediate(source);
                Assert.That(presence.Source, Is.Null);
                Assert.That(presence.IsRemoved, Is.False);
                GC.KeepAlive(source);
            }
            finally
            {
                if (source != null)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Presence DetectTemporarySource(Detector detector)
        {
            return detector.Arrive("temporary", new object());
        }

        /// <summary>
        /// A module can be disabled and rebound; a returning presence reconnects its existing
        /// components after grace without reading the departed proxy.
        /// </summary>
        [Test]
        public void Modules_StopWhileDisabledOrMissingAndRebindOnReturn()
        {
            int reads = 0;
            int initializations = 0;
            Reading proxy = new Reading(new Double3(1, 2, 3), 0);
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                initializations++;
                Reading current = (Reading)presence.Source;
                root.GetComponent<PipelinePositionModule>().Bind(() =>
                {
                    reads++;
                    return current.Position;
                });
            });
            Detector detector = new Detector { DisappearanceGracePeriod = TimeSpan.FromSeconds(60) };
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.Arrive("one", proxy);
            _realm.Update();
            PipelinePositionModule module = presence.Root.GetComponent<PipelinePositionModule>();
            Assert.Throws<ArgumentNullException>(() => module.Bind(null));
            module.enabled = false;
            _realm.Update();
            Assert.That(reads, Is.EqualTo(1));
            module.enabled = true;
            module.Bind(() => new Double3(7, 8, 9));
            _realm.Update();
            Assert.That(presence.Root.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(7, 8, 9)));

            detector.Lose("one");
            _realm.Update();
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(presence.IsAvailable, Is.False);
            Assert.That(presence.Source, Is.Null);
            proxy = new Reading(new Double3(10, 11, 12), 0);
            Assert.That(detector.Arrive("one", proxy), Is.SameAs(presence));
            _realm.Update();
            Assert.That(presence.Root.GetComponent<PipelinePositionModule>(), Is.SameAs(module));
            Assert.That(presence.Root.GetComponent<Spatial>().Position, Is.EqualTo(proxy.Position));
            Assert.That(initializations, Is.EqualTo(2));
            Assert.That(reads, Is.EqualTo(2));
        }

        /// <summary>
        /// A replacement SDK can bind the same Ghost modules from a different proxy shape,
        /// preserving roots and reconnecting their inputs before the new attachment activates.
        /// </summary>
        [Test]
        public void SourceReplacement_RebindsExistingModulesToAnotherSdk()
        {
            Reading firstProxy = new Reading(new Double3(1, 2, 3), 7);
            PositionSdk secondSdk = new PositionSdk { Position = new Double3(4, 5, 6) };
            int initializations = 0;
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                initializations++;
                PipelinePositionModule module = root.GetComponent<PipelinePositionModule>();
                if (presence.Source is PositionSdk)
                {
                    module.Bind(() => ((PositionSdk)presence.Source).Position);
                }
                else
                {
                    module.Bind(() => ((Reading)presence.Source).Position);
                }
            });
            Detector first = new Detector { Starting = detector => detector.Arrive("one", firstProxy) };
            Anchor anchor = _realm.GetOrCreateAnchor("sdk", first);
            Assert.That(_realm.TryGetPresence(new Key("sdk", TrackedKind, "one"), out Presence presence), Is.True);
            Ghost root = presence.Root;
            PipelinePositionModule originalModule = root.GetComponent<PipelinePositionModule>();
            anchor.ReplaceDetector(first, new Detector { Starting = detector => detector.Arrive("one", secondSdk) });
            Assert.That(presence.Source, Is.SameAs(secondSdk));
            Assert.That(presence.Root, Is.SameAs(root));
            Assert.That(root.GetComponent<PipelinePositionModule>(), Is.SameAs(originalModule));
            Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(4, 5, 6)));
            Assert.That(initializations, Is.EqualTo(2));
            firstProxy.Position = new Double3(90, 90, 90);
            secondSdk.Position = new Double3(7, 8, 9);
            _realm.Update();
            Assert.That(root.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(7, 8, 9)));
        }

        /// <summary>
        /// Changed capability metadata reruns input binding without adding or replacing Ghost modules.
        /// </summary>
        [Test]
        public void Detect_CapabilityChangesRebindConfiguredModules()
        {
            int initializations = 0;
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                initializations++;
                if (presence.HasCapability<IArticulationCapability>())
                {
                    root.GetComponent<PipelineArticulationModule>().Bind(() => 12);
                }
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.PublishMetadata("one", "Car");
            _realm.Update();
            Assert.That(((PipelineGhost)presence.Root).Articulation, Is.Zero);
            Assert.That(detector.PublishMetadata("one", "Renamed", typeof(IArticulationCapability)), Is.SameAs(presence));
            _realm.Update();
            Assert.That(((PipelineGhost)presence.Root).Articulation, Is.EqualTo(12));
            Assert.That(presence.Root.GetComponents<EntityModule>().Length, Is.EqualTo(3));
            Assert.That(presence.Name, Is.EqualTo("Renamed"));
            Assert.That(initializations, Is.EqualTo(2));
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
        /// A failed reader or module removes its detector's population before query notifications
        /// and retains the original exception for diagnostics.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ModuleFailure_RemovesDetectorPopulationAndPreservesError(bool failInReader)
        {
            bool rejectData = false;
            InvalidOperationException failure = new InvalidOperationException("module rejected data");
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                PipelineActionModule module = root.GetComponent<PipelineActionModule>();
                module.Bind(() =>
                {
                    if (rejectData && failInReader)
                    {
                        throw failure;
                    }

                    return 42;
                });
                module.Applying = () =>
                {
                    if (rejectData && !failInReader)
                    {
                        throw failure;
                    }
                };
            });
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence previous = detector.PublishMetadata("previous", null);
            _realm.Update();
            Assert.That(previous.IsAvailable, Is.True);
            rejectData = true;
            Presence next = detector.PublishMetadata("next", null);
            ExpectedErrors.Verify(_realm.Update, "module rejected data");
            Assert.That(detector.IsActive, Is.False);
            Assert.That(detector.LastError, Is.SameAs(failure));
            Assert.That(previous.IsRemoved, Is.True);
            Assert.That(next.IsRemoved, Is.True);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// A reader that removes its entity cannot apply a stale value.
        /// </summary>
        [Test]
        public void ReaderRemoval_PreventsStaleValueApplication()
        {
            Detector detector = new Detector();
            _realm.RegisterPresenceInitializer<PipelineGhost>(TrackedKind, (presence, root) =>
            {
                root.GetComponent<PipelinePositionModule>().Bind(() =>
                {
                    Assert.Throws<InvalidOperationException>(_realm.Update);
                    detector.Lose(presence.Key.EntityId);
                    return new Double3(1, 2, 3);
                });
            });
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.PublishMetadata("one", null);
            Spatial spatial = presence.Root.GetComponent<Spatial>();
            _realm.Update();
            Assert.That(presence.IsRemoved, Is.True);
            Assert.That(spatial.HasPosition, Is.False);
        }

        /// <summary>
        /// A Presence can request and cancel its fallback view while retaining its root and availability.
        /// </summary>
        [Test]
        public void Manifest_PresenceKeepsRootAndAvailability()
        {
            GameObject prefab = new GameObject("presence view");
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                prefab.SetActive(false);
                blueprint.Configure(TrackedKind, null, null, prefab);
                _realm.RegisterManifestationBlueprint(blueprint);
                Detector detector = new Detector();
                _realm.GetOrCreateAnchor("sdk", detector);
                Presence presence = detector.PublishMetadata("car-2", "SUV two");
                Ghost root = presence.Root;
                _realm.Update();
                Assert.That(root.GetComponentInChildren<View>(), Is.Null);

                View view = _realm.Manifest(presence);
                Assert.That(view.gameObject.name, Is.EqualTo("presence view"));
                Assert.That(view.Ghost, Is.SameAs(root));
                Assert.That(_realm.Manifest(presence), Is.SameAs(view));
                _realm.Demanifest(presence);
                Assert.That(root.GetComponentInChildren<View>(), Is.Null);
                Assert.That(presence.Root, Is.SameAs(root));
                Assert.That(presence.IsAvailable, Is.True);
                Assert.That(_realm.Manifest(presence).Ghost, Is.SameAs(root));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// A metadata-only report needs neither a blueprint nor an initializer and names its root by entity ID.
        /// </summary>
        [Test]
        public void MetadataOnlyDetection_UsesPlainViewlessGhost()
        {
            Detector detector = new Detector();
            _realm.GetOrCreateAnchor("sdk", detector);
            Presence presence = detector.PublishMetadata("silent", "Silent object");
            Assert.That(presence.Root, Is.TypeOf<Ghost>());
            Assert.That(presence.Root.gameObject.name, Is.EqualTo("silent"));
            Assert.That(presence.Name, Is.EqualTo("Silent object"));
            Assert.That(presence.HasCapability<IPositionCapability>(), Is.False);
            Assert.That(presence.Root.GetComponents<EntityModule>(), Is.Empty);
            _realm.Update();
            Assert.That(presence.IsAvailable, Is.True);
            Assert.That(_realm.Query().Single(), Is.SameAs(presence.Root));
            Assert.That(_realm.Manifest(presence), Is.Null);
            Assert.That(presence.Root.GetComponentInChildren<View>(), Is.Null);
        }

        /// <summary>
        /// Plain Ghost prefabs bind reusable modules that resolve their own cloned Ghost before activation,
        /// project after all readers update the followed reference,
        /// and name roots by entity ID before initialization so Unity can find them once activated.
        /// </summary>
        [Test]
        public void PlainGhostPrefab_ComposesModulesBeforeRelativePlacement()
        {
            GameObject prefab = new GameObject("composed ghost");
            prefab.SetActive(false);
            Ghost prefabGhost = prefab.AddComponent<Ghost>();
            prefab.AddComponent<Spatial>();
            PipelinePositionModule prefabModule = prefab.AddComponent<PipelinePositionModule>();
            Assert.That(prefabModule.Ghost, Is.SameAs(prefabGhost));
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                blueprint.Configure(TrackedKind, prefabGhost, null, null);
                _realm.RegisterManifestationBlueprint(blueprint);
                _realm.RegisterPresenceInitializer<Ghost>(TrackedKind, (presence, root) =>
                {
                    Assert.That(root.gameObject.name, Is.EqualTo(presence.Key.EntityId));
                    PipelinePositionModule module = root.GetRequired<PipelinePositionModule>();
                    Assert.That(module.Ghost, Is.SameAs(root));
                    Assert.That(module.Ghost.Key, Is.EqualTo(presence.Key));
                    module.Bind(() => ((Reading)presence.Source).Position);
                });
                _realm.ReferenceFrame = new ReferenceFrame
                {
                    FollowedGhost = new Key("sdk", TrackedKind, "named-prefab-origin")
                };
                Detector detector = new Detector();
                _realm.GetOrCreateAnchor("sdk", detector);
                Reading targetData = new Reading(new Double3(1010, 0, 0), 0);
                Reading originData = new Reading(new Double3(1000, 0, 0), 0);
                Presence target = detector.Arrive("named-prefab-target", targetData);
                Presence origin = detector.Arrive("named-prefab-origin", originData);
                _realm.Update();

                Assert.That(target.Root, Is.TypeOf<Ghost>());
                Assert.That(target.Root, Is.Not.SameAs(prefabGhost));
                Assert.That(GameObject.Find("named-prefab-target"), Is.SameAs(target.Root.gameObject));
                Assert.That(GameObject.Find("named-prefab-origin"), Is.SameAs(origin.Root.gameObject));
                Assert.That(target.Root.GetRequired<Spatial>().Position, Is.EqualTo(targetData.Position));
                Assert.That(target.Root.transform.position, Is.EqualTo(new Vector3(10, 0, 0)));
                Assert.That(origin.Root.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(target.IsAvailable, Is.True);
                Assert.That(_realm.Query().Count, Is.EqualTo(2));
                Assert.That(target.Root.GetComponentInChildren<View>(), Is.Null);

                originData.Position = new Double3(1005, 0, 0);
                _realm.Update();
                Assert.That(target.Root.transform.position, Is.EqualTo(new Vector3(5, 0, 0)),
                    "Projection must use this update's mapped origin, even when the target was detected first.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        private interface IPositionCapability
        {
        }

        private interface IArticulationCapability
        {
        }

        private sealed class PositionSdk : IDisposable
        {
            internal Double3 Position;
            internal bool IsDisposed;

            /// <summary>Records disposal initiated by the application.</summary>
            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class Reading
        {
            internal Reading(Double3 position, int articulation)
            {
                Position = position;
                Articulation = articulation;
            }

            internal Double3 Position;
            internal int Articulation;
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
            internal Action<Detector> Starting;

            /// <inheritdoc />
            protected override void OnStart()
            {
                Starting?.Invoke(this);
            }

            internal Presence Arrive(string entityId, object source)
            {
                return Detect(entityId, TrackedKind, source: source);
            }

            internal void Lose(string entityId)
            {
                Disappear(TrackedKind, entityId);
            }

            internal void PublishCached(IGhost ghost)
            {
                MarkPublished(ghost);
            }

            internal Presence PublishMetadata(string entityId, string name, params Type[] capabilities)
            {
                return Detect(entityId, TrackedKind, name, capabilities: capabilities);
            }
        }
    }
}
