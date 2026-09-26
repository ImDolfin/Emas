using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises lifecycle transactions against real Unity activation and destruction.
    /// </summary>
    public sealed class LifecycleTests
    {
        private static readonly Kind Kind = new Kind("tests.lifecycle");
        private static readonly Variant First = new Variant("first");
        private static readonly Variant Second = new Variant("second");
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private Realm _realm;

        /// <summary>
        /// Creates an isolated realm and clears callback probes.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            ProbeView.Enabled = null;
            _realm = new Realm();
        }

        /// <summary>
        /// Disposes the realm and destroys temporary authoring assets.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            ProbeView.Enabled = null;
            _realm.Dispose();
            for (int index = 0; index < _assets.Count; index++)
            {
                if (_assets[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_assets[index]);
                }
            }

            _assets.Clear();
        }

        /// <summary>
        /// Rejected attachment cannot remove the source's original population.
        /// </summary>
        [Test]
        public void Detector_CannotBeAttachedToTwoAnchors()
        {
            ProbeSource source = new ProbeSource();
            Anchor firstAnchor = _realm.GetOrCreateAnchor("first", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            Assert.Throws<InvalidOperationException>(() => _realm.GetOrCreateAnchor("second", source));
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(ghost.transform.parent, Is.SameAs(firstAnchor.Transform));
            Anchor secondAnchor = _realm.GetOrCreateAnchor("second");
            Assert.Throws<InvalidOperationException>(() => secondAnchor.AddDetector(source));
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
        }

        /// <summary>
        /// Failed source batches on an existing anchor undo only this call's new attachments.
        /// </summary>
        [Test]
        public void GetOrCreateAnchor_RollsBackNewSourcesOnExistingAnchorFailure()
        {
            ProbeSource retained = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", retained);
            ProbeGhost kept = retained.Publish("kept");
            ProbeGhost prepared = _realm.Prepare<ProbeGhost>("anchor", Kind, "prepared");
            _realm.Update();

            ProbeSource added = new ProbeSource();
            added.Starting = () =>
            {
                added.Publish("prepared");
                added.Publish("temporary");
            };
            ProbeSource alsoAdded = new ProbeSource();
            alsoAdded.Starting = () => alsoAdded.Publish("another");
            ProbeSource failing = new ProbeSource();
            failing.Starting = () =>
            {
                failing.Publish("failed");
                throw new InvalidOperationException("start failed");
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                _realm.GetOrCreateAnchor("anchor", retained, added, alsoAdded, failing));

            Assert.That(error.Message, Is.EqualTo("start failed"));
            Assert.That(anchor.Detectors, Is.EquivalentTo(new[] { retained }));
            Assert.That(retained.IsActive, Is.True);
            Assert.That(retained.StopCount, Is.Zero);
            Assert.That(added.IsAttached, Is.False);
            Assert.That(added.StopCount, Is.EqualTo(1));
            Assert.That(alsoAdded.IsAttached, Is.False);
            Assert.That(alsoAdded.StopCount, Is.EqualTo(1));
            Assert.That(failing.IsAttached, Is.False);
            Assert.That(failing.StopCount, Is.EqualTo(1));
            IGhost found;
            Assert.That(_realm.TryGetGhost(prepared.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(prepared));
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_realm.Query().Single(), Is.SameAs(kept));
        }

        /// <summary>
        /// A disposed realm cannot create unmanaged scene objects or accept mutations.
        /// </summary>
        [Test]
        public void DisposedRealm_RejectsMutationsAndKeepsDisposeIdempotent()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            ManifestationBlueprint blueprint = CreateManifestationBlueprint();
            _realm.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _realm.GetOrCreateAnchor("late"));
            Assert.Throws<ObjectDisposedException>(() => _realm.RegisterManifestationBlueprint(blueprint));
            Assert.Throws<ObjectDisposedException>(() => _realm.Prepare<ProbeGhost>("anchor", Kind, "late"));
            Assert.Throws<ObjectDisposedException>(() => _realm.Manifest(ghost));
            Assert.Throws<ObjectDisposedException>(() => _realm.Demanifest(ghost));
            Assert.Throws<ObjectDisposedException>(() => _realm.SetDetailLevel(ghost, DetailLevel.Full));
            Assert.Throws<ObjectDisposedException>(() => _realm.RemoveAnchor("anchor"));
            Assert.Throws<ObjectDisposedException>(() => _realm.Query().OnAvailable(value =>
            {
            }));
            Assert.DoesNotThrow(() => _realm.Dispose());
            Assert.DoesNotThrow(() => _realm.Update());
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Direct anchor disposal unregisters prepared records immediately and rejects mutations.
        /// </summary>
        [Test]
        public void DisposedAnchor_UnregistersBeforeDeferredDestruction()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost prepared = _realm.Prepare<ProbeGhost>("anchor", Kind, "prepared");
            anchor.Dispose();
            IGhost found;
            Assert.That(_realm.TryGetGhost(prepared.Key, out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => anchor.AddDetector(new ProbeSource()));
            Assert.Throws<ObjectDisposedException>(() => anchor.RemoveDetector(source));
            Assert.Throws<ObjectDisposedException>(() => anchor.ReplaceDetector(source, new ProbeSource()));
            Anchor next = _realm.GetOrCreateAnchor("anchor");
            Assert.That(next, Is.Not.SameAs(anchor));
            Assert.That(_realm.Prepare<ProbeGhost>("anchor", Kind, "prepared"), Is.Not.SameAs(prepared));
        }

        /// <summary>
        /// A replacement failure removes its population and supports recovery with new ghost objects.
        /// </summary>
        [Test]
        public void FailedReplacement_RemovesPopulationAndCanRecover()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            ProbeSource failed = new ProbeSource();
            failed.Starting = () =>
            {
                failed.Publish("car");
                throw new InvalidOperationException("replacement failed");
            };
            Assert.Throws<InvalidOperationException>(() => anchor.ReplaceDetector(source, failed));
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            IGhost found;
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(failed.IsAttached, Is.True);
            Assert.That(failed.IsActive, Is.False);
            Anchor otherAnchor = _realm.GetOrCreateAnchor("other");
            Assert.Throws<InvalidOperationException>(() => otherAnchor.AddDetector(failed));
            ProbeSource recovery = new ProbeSource();
            anchor.ReplaceDetector(failed, recovery);
            ProbeGhost recovered = recovery.Publish("car");
            Assert.That(recovered, Is.Not.SameAs(ghost));
            Assert.That(recovered.Key, Is.EqualTo(ghost.Key));
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(recovered));
        }

        /// <summary>
        /// Variant-driven view activation observes the completed source update.
        /// </summary>
        [Test]
        public void VariantViewRefresh_WaitsUntilAllSourceUpdatesFinish()
        {
            _realm.RegisterManifestationBlueprint(CreateManifestationBlueprint());
            ProbeSource first = new ProbeSource();
            ProbeSource second = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", first, second);
            ProbeGhost ghost = first.Publish("car", First, 1);
            ProbeGhost other = second.Publish("other", First, 10);
            _realm.Update();
            View initial = _realm.Manifest(ghost);
            List<int> observed = new List<int>();
            ProbeView.Enabled = view =>
            {
                ProbeGhost data = (ProbeGhost)view.GetComponent<global::Emas.View>().Ghost;
                observed.Add(data.Value + other.Value);
            };
            first.Updating = () =>
            {
                ProbeGhost updated = first.Publish("car", Second, 2);
                _realm.Manifest(updated);
                updated.Value = 3;
                Assert.That(observed.Count, Is.EqualTo(0));
            };
            second.Updating = () => other.Value = 20;
            _realm.Update();
            Assert.That(observed, Is.EqualTo(new[] { 23 }));
            Assert.That(initial.gameObject.activeSelf, Is.False);
            Assert.That(_realm.Manifest(ghost).gameObject.name, Is.EqualTo("Second view"));
        }

        /// <summary>
        /// Availability notification occurs after a previously requested view is bound and active.
        /// </summary>
        [Test]
        public void AvailabilityNotification_FollowsViewRefresh()
        {
            _realm.RegisterManifestationBlueprint(CreateManifestationBlueprint());
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Manifest(ghost);
            bool hadView = false;
            _realm.Query().OnAvailable(value => hadView = ghost.GetComponentInChildren<global::Emas.View>() != null);
            _realm.Update();
            Assert.That(hadView, Is.True);
        }

        /// <summary>
        /// Disposing during notification suppresses subsequent matches.
        /// </summary>
        [Test]
        public void Subscription_CanDisposeItselfDuringNotification()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            IDisposable subscription = null;
            int calls = 0;
            subscription = _realm.Query().OnAvailable(ghost =>
            {
                calls++;
                subscription.Dispose();
            });
            source.Publish("first");
            source.Publish("second");
            _realm.Update();
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Typed interface filters retain conjunction, root-only lookup and immutable descriptions.
        /// </summary>
        [Test]
        public void TypedQuery_ComposesAndExcludesViewOnlyContracts()
        {
            _realm.RegisterManifestationBlueprint(CreateManifestationBlueprint());
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            _realm.Manifest(ghost);
            Query query = _realm.Query().With<IProbeData>();
            Assert.That(query.Count, Is.EqualTo(1));
            Assert.That(query.With<IViewOnly>().Count, Is.EqualTo(0));
            Assert.That(query.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Nested dispatch is deferred and cannot starve source updates.
        /// </summary>
        [Test]
        public void Dispatch_RequeuedActionRunsNextUpdate()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            int calls = 0;
            Action repeat = null;
            repeat = () =>
            {
                calls++;
                source.Queue(repeat);
            };
            source.Queue(repeat);
            _realm.Update();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(source.UpdateCount, Is.EqualTo(1));
            _realm.Update();
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Scene unload removes both published and prepared roots and permits identity reuse.
        /// </summary>
        [UnityTest]
        public IEnumerator SceneUnload_RemovesAnchorAndAllItsRecords()
        {
            Scene scene = SceneManager.CreateScene("Emas lifecycle " + Guid.NewGuid().ToString("N"));
            GameObject frame = new GameObject("Scene frame");
            SceneManager.MoveGameObjectToScene(frame, scene);
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("scene", frame.transform, source);
            ProbeGhost ghost = source.Publish("published");
            ProbeGhost prepared = _realm.Prepare<ProbeGhost>("scene", Kind, "prepared");
            _realm.Update();
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            Assert.That(ghost == null, Is.True);
            Assert.That(prepared == null, Is.True);
            Assert.That(source.StopCount, Is.EqualTo(1));
            Anchor next = _realm.GetOrCreateAnchor("scene");
            Assert.That(next, Is.Not.SameAs(anchor));
            Assert.That(_realm.Prepare<ProbeGhost>("scene", Kind, "prepared"), Is.Not.Null);
        }

        /// <summary>
        /// Dispatched source failure removes and destroys its partially populated ghost.
        /// </summary>
        [UnityTest]
        public IEnumerator DispatchFailure_RemovesPartialPublication()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = null;
            source.Queue(() =>
            {
                ghost = source.Publish("car");
                throw new InvalidOperationException("dispatch failure");
            });
            ExpectedErrors.Verify(_realm.Update, "dispatch failure");
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            Assert.That(source.StopCount, Is.EqualTo(1));
            IGhost found;
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.False);
            GameObject root = ghost.gameObject;
            yield return null;
            Assert.That(root == null, Is.True);
            Assert.That(ghost == null, Is.True);
        }

        private ManifestationBlueprint CreateManifestationBlueprint()
        {
            GameObject root = new GameObject("Ghost template");
            root.SetActive(false);
            ProbeGhost ghost = root.AddComponent<ProbeGhost>();
            GameObject first = new GameObject("First view");
            first.SetActive(false);
            first.AddComponent<ProbeView>();
            GameObject second = new GameObject("Second view");
            second.SetActive(false);
            second.AddComponent<ProbeView>();
            ManifestationVariant firstVariant = ScriptableObject.CreateInstance<ManifestationVariant>();
            firstVariant.Configure(First, new[]
            {
                new ManifestationVariant.DetailMapping(DetailLevel.Full, first)
            });
            ManifestationVariant secondVariant = ScriptableObject.CreateInstance<ManifestationVariant>();
            secondVariant.Configure(Second, new[]
            {
                new ManifestationVariant.DetailMapping(DetailLevel.Full, second)
            });
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(Kind, ghost, new[] { firstVariant, secondVariant }, first);
            _assets.Add(root);
            _assets.Add(first);
            _assets.Add(second);
            _assets.Add(firstVariant);
            _assets.Add(secondVariant);
            _assets.Add(blueprint);
            return blueprint;
        }

        private interface IProbeData
        {
        }

        private interface IViewOnly
        {
        }

        private sealed class ProbeGhost : Ghost, IProbeData
        {
            internal int Value;
        }

        private sealed class ProbeView : MonoBehaviour, IViewOnly
        {
            internal static Action<ProbeView> Enabled;

            private void OnEnable()
            {
                if (Enabled != null)
                {
                    Enabled(this);
                }
            }
        }

        private sealed class ProbeSource : PresenceDetector
        {
            internal Action Starting;
            internal Action Updating;
            internal int UpdateCount;
            internal int StopCount;

            internal ProbeGhost Publish(string id)
            {
                return Publish(id, First, 0);
            }

            internal ProbeGhost Publish(string id, Variant variant, int value)
            {
                ProbeGhost ghost = GetOrCreate<ProbeGhost>(id, Kind, variant);
                ghost.Value = value;
                return ghost;
            }

            internal void Queue(Action action)
            {
                Dispatch(action);
            }

            protected override void OnStart()
            {
                if (Starting != null)
                {
                    Starting();
                }
            }

            protected override void OnUpdate()
            {
                UpdateCount++;
                if (Updating != null)
                {
                    Updating();
                }
            }

            protected override void OnStop()
            {
                StopCount++;
            }
        }
    }
}
