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
            ProbeGhost.Enabled = null;
            ProbeGhost.Disabled = null;
            ProbeView.Enabled = null;
            ProbeView.Disabled = null;
            _realm = new Realm();
        }

        /// <summary>
        /// Disposes the realm and destroys temporary authoring assets.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            ProbeGhost.Enabled = null;
            ProbeGhost.Disabled = null;
            ProbeView.Enabled = null;
            ProbeView.Disabled = null;
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
        public void SecondAnchorAttachment_PreservesAnchoralGhosts()
        {
            ProbeSource source = new ProbeSource();
            Anchor firstAnchor = _realm.GetOrCreateAnchor("first", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            Assert.Throws<InvalidOperationException>(() => _realm.GetOrCreateAnchor("second", source));
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(ghost.transform.parent, Is.SameAs(firstAnchor.Transform));
            Anchor secondAnchor = _realm.GetOrCreateAnchor("second");
            Assert.Throws<InvalidOperationException>(() => secondAnchor.AddSource(source));
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
        }

        /// <summary>
        /// Failed startup preserves an existing prepared identity but removes newly created records.
        /// </summary>
        [Test]
        public void FailedAttachment_RollsBackOnlyItsNewGhosts()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("anchor");
            ProbeGhost prepared = _realm.Prepare<ProbeGhost>("anchor", Kind, "prepared");
            ProbeSource source = new ProbeSource();
            source.Starting = () =>
            {
                source.Publish("prepared");
                source.Publish("new");
                throw new InvalidOperationException("start failed");
            };
            Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source));
            Assert.That(source.StopCount, Is.EqualTo(1));
            ProbeSource replacement = new ProbeSource();
            anchor.AddSource(replacement);
            Assert.That(replacement.Publish("prepared"), Is.SameAs(prepared));
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
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
            Assert.That(anchor.Sources, Is.EquivalentTo(new[] { retained }));
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
        /// Removing an old attachment preserves roots reclaimed by a new attachment and removes unclaimed leftovers.
        /// </summary>
        [Test]
        public void SourceRemoval_PreservesReclaimedRootDuringSceneCleanup()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost[] population = { source.Publish("first"), source.Publish("second"), source.Publish("third") };
            _realm.Update();
            ProbeGhost reclaimed = null;
            ProbeGhost.Disabled = disabled =>
            {
                ProbeGhost.Disabled = null;
                source.Starting = () =>
                {
                    for (int index = 0; index < population.Length; index++)
                    {
                        IGhost found;
                        if (_realm.TryGetGhost(population[index].Key, out found))
                        {
                            reclaimed = source.Publish(found.Key.EntityId);
                            Assert.That(reclaimed, Is.SameAs(found));
                            break;
                        }
                    }
                };
                anchor.AddSource(source);
            };

            anchor.RemoveSource(source);

            Assert.That(source.IsActive && source.IsAttached, Is.True);
            Assert.That(source.StartCount, Is.EqualTo(2));
            Assert.That(reclaimed, Is.Not.Null);
            Assert.That(_realm.Query().Single(), Is.SameAs(reclaimed));
            for (int index = 0; index < population.Length; index++)
            {
                IGhost found;
                Assert.That(_realm.TryGetGhost(population[index].Key, out found),
                    Is.EqualTo(ReferenceEquals(population[index], reclaimed)));
            }
        }

        /// <summary>
        /// An older batch rollback cannot clear ownership of a prepared root reclaimed during scene cleanup.
        /// </summary>
        [Test]
        public void BatchRollback_PreservesPreparedRootReclaimedByNewAttachment()
        {
            Anchor anchor = _realm.GetOrCreateAnchor("anchor");
            ProbeGhost first = _realm.Prepare<ProbeGhost>("anchor", Kind, "first");
            ProbeGhost second = _realm.Prepare<ProbeGhost>("anchor", Kind, "second");
            ProbeSource source = new ProbeSource();
            source.Starting = () =>
            {
                source.Publish("first");
                source.Publish("second");
                source.Publish("temporary");
            };
            ProbeSource failing = new ProbeSource();
            failing.Starting = () => throw new InvalidOperationException("start failed");
            ProbeGhost reclaimed = null;
            ProbeGhost.Disabled = disabled =>
            {
                ProbeGhost.Disabled = null;
                // At least one prepared root is still active when the first rollback scene effect runs.
                ProbeGhost candidate = first.IsAvailable ? first : second;
                source.Starting = () => reclaimed = source.Publish(candidate.Key.EntityId);
                anchor.AddSource(source);
                Assert.That(reclaimed, Is.SameAs(candidate));
            };

            Assert.Throws<InvalidOperationException>(() => _realm.GetOrCreateAnchor("anchor", source, failing));

            Assert.That(source.IsActive && source.IsAttached, Is.True);
            Assert.That(anchor.Sources, Is.EqualTo(new[] { source }));
            Assert.That(_realm.Query().Single(), Is.SameAs(reclaimed));
            IGhost found;
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "temporary"), out found), Is.False);
            ProbeGhost unclaimed = ReferenceEquals(reclaimed, first) ? second : first;
            Assert.That(_realm.TryGetGhost(unclaimed.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(unclaimed));
            Assert.That(unclaimed.IsAvailable, Is.False);
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(reclaimed));
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
            Blueprint blueprint = Blueprint();
            _realm.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _realm.GetOrCreateAnchor("late"));
            Assert.Throws<ObjectDisposedException>(() => _realm.RegisterBlueprint(blueprint));
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
            Assert.Throws<ObjectDisposedException>(() => anchor.AddSource(new ProbeSource()));
            Assert.Throws<ObjectDisposedException>(() => anchor.RemoveSource(source));
            Assert.Throws<ObjectDisposedException>(() => anchor.ReplaceSource(source, new ProbeSource()));
            Anchor next = _realm.GetOrCreateAnchor("anchor");
            Assert.That(next, Is.Not.SameAs(anchor));
            Assert.That(_realm.Prepare<ProbeGhost>("anchor", Kind, "prepared"), Is.Not.SameAs(prepared));
        }

        /// <summary>
        /// Root activation may add records without invalidating a dictionary enumeration.
        /// </summary>
        [Test]
        public void OnEnable_CanPrepareAnotherGhost()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost prepared = null;
            ProbeGhost.Enabled = ghost =>
            {
                if (ghost.Key.EntityId == "car")
                {
                    prepared = _realm.Prepare<ProbeGhost>("anchor", Kind, "late");
                }
            };
            source.Publish("car");
            _realm.Update();
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(0));
        }

        /// <summary>
        /// A root can remove itself during activation without being manifested afterward.
        /// </summary>
        [UnityTest]
        public IEnumerator OnEnable_CanRemoveItsOwnGhost()
        {
            _realm.RegisterBlueprint(Blueprint());
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Manifest(ghost);
            int views = 0;
            ProbeView.Enabled = view => views++;
            ProbeGhost.Enabled = value => source.Depart(value.Key.EntityId);
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            Assert.That(views, Is.EqualTo(0));
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            yield return null;
            Assert.That(ghost == null, Is.True);
        }

        /// <summary>
        /// Disposal inside activation cancels remaining work and later notifications.
        /// </summary>
        [Test]
        public void OnEnable_CanDisposeRealm()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            source.Publish("first");
            source.Publish("second");
            int notifications = 0;
            _realm.Query().OnAvailable(ghost => notifications++);
            ProbeGhost.Enabled = ghost => _realm.Dispose();
            _realm.Update();
            Assert.That(notifications, Is.EqualTo(0));
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            Assert.That(source.StopCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Deactivation may remove another record during ownership transfer.
        /// </summary>
        [Test]
        public void OnDisable_CanRemoveAnchorDuringReplacement()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            source.Publish("first");
            source.Publish("second");
            _realm.Update();
            ProbeSource replacement = new ProbeSource();
            ProbeGhost.Disabled = ghost => _realm.RemoveAnchor("anchor");
            Assert.DoesNotThrow(() => anchor.ReplaceSource(source, replacement));
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            Assert.That(replacement.StartCount, Is.EqualTo(0));
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
            Assert.Throws<InvalidOperationException>(() => anchor.ReplaceSource(source, failed));
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            IGhost found;
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(_realm.GetOwnedGhosts(failed), Is.Empty);
            Assert.That(failed.IsAttached, Is.True);
            Assert.That(failed.IsActive, Is.False);
            Anchor otherAnchor = _realm.GetOrCreateAnchor("other");
            Assert.Throws<InvalidOperationException>(() => otherAnchor.AddSource(failed));
            ProbeSource recovery = new ProbeSource();
            anchor.ReplaceSource(failed, recovery);
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
            _realm.RegisterBlueprint(Blueprint());
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
            _realm.RegisterBlueprint(Blueprint());
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
        /// A view activation callback can demanifest itself without resurrecting the view.
        /// </summary>
        [UnityTest]
        public IEnumerator ViewOnEnable_CanDemanifestItself()
        {
            _realm.RegisterBlueprint(Blueprint());
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            ProbeView.Enabled = view => _realm.Demanifest(ghost);
            Assert.That(_realm.Manifest(ghost), Is.Null);
            yield return null;
            Assert.That(ghost.GetComponentInChildren<global::Emas.View>(true), Is.Null);
            Assert.That(ghost.IsAvailable, Is.True);
        }

        /// <summary>
        /// Deactivating an old view may remove the ghost without constructing a replacement.
        /// </summary>
        [UnityTest]
        public IEnumerator ViewOnDisable_CanRemoveGhostDuringVariantChange()
        {
            _realm.RegisterBlueprint(Blueprint());
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            _realm.Update();
            _realm.Manifest(ghost);
            int newViews = 0;
            ProbeView.Enabled = view => newViews++;
            ProbeView.Disabled = view => source.Depart("car");
            source.Updating = () => source.Publish("car", Second, 2);
            _realm.Update();
            Assert.That(newViews, Is.EqualTo(0));
            Assert.That(_realm.Query().Count, Is.EqualTo(0));
            yield return null;
            Assert.That(ghost == null, Is.True);
        }

        /// <summary>
        /// A subscription callback cannot notify an identity removed by an earlier callback.
        /// </summary>
        [Test]
        public void Subscription_RechecksMatchesAfterCallbackMutation()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            source.Publish("first");
            source.Publish("second");
            _realm.Update();
            int calls = 0;
            _realm.Query().OnAvailable(ghost =>
            {
                calls++;
                source.Depart("first");
                source.Depart("second");
            });
            Assert.That(calls, Is.EqualTo(1));
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
            _realm.RegisterBlueprint(Blueprint());
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
        /// A large backlog is processed over multiple updates in FIFO order.
        /// </summary>
        [Test]
        public void Dispatch_BacklogRespectsBudgetAndOrder()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            List<int> calls = new List<int>();
            int count = Realm.MaxDispatchActionsPerUpdate + 7;
            for (int index = 0; index < count; index++)
            {
                int value = index;
                source.Queue(() => calls.Add(value));
            }

            _realm.Update();
            Assert.That(calls.Count, Is.EqualTo(Realm.MaxDispatchActionsPerUpdate));
            Assert.That(source.UpdateCount, Is.EqualTo(1));
            _realm.Update();
            Assert.That(calls.Count, Is.EqualTo(count));
            for (int index = 0; index < count; index++)
            {
                Assert.That(calls[index], Is.EqualTo(index));
            }
        }

        /// <summary>
        /// A reused source cannot execute queued actions from its previous registration.
        /// </summary>
        [Test]
        public void Dispatch_ReattachedInstanceDiscardsOldGeneration()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            int calls = 0;
            source.Queue(() => calls++);
            anchor.RemoveSource(source);
            anchor.AddSource(source);
            source.Queue(() => calls += 10);
            _realm.Update();
            Assert.That(calls, Is.EqualTo(10));
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
        /// Ownership changes during root activation defer publication until the new source is finalized.
        /// </summary>
        [Test]
        public void OnEnable_CanReplaceSourceWithoutPublishingStaleAvailability()
        {
            ProbeSource source = new ProbeSource();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car");
            ProbeSource replacement = new ProbeSource();
            replacement.Starting = () => replacement.Publish("car", Second, 42);
            bool replaced = false;
            ProbeGhost.Enabled = value =>
            {
                if (!replaced)
                {
                    replaced = true;
                    anchor.ReplaceSource(source, replacement);
                }
            };
            int notifications = 0;
            _realm.Query().OnAvailable(value => notifications++);
            _realm.Update();
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(notifications, Is.EqualTo(0));
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(ghost.Value, Is.EqualTo(42));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(1));
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
            Assert.That(_realm.GetOwnedGhosts(source), Is.Empty);
            IGhost found;
            Assert.That(_realm.TryGetGhost(ghost.Key, out found), Is.False);
            GameObject root = ghost.gameObject;
            yield return null;
            Assert.That(root == null, Is.True);
            Assert.That(ghost == null, Is.True);
        }

        /// <summary>
        /// A subscription created while applying source data waits for the completed update.
        /// </summary>
        [Test]
        public void SubscriptionCreatedDuringSourceUpdate_WaitsForFinalValues()
        {
            ProbeSource source = new ProbeSource();
            _realm.GetOrCreateAnchor("anchor", source);
            ProbeGhost ghost = source.Publish("car", First, 1);
            _realm.Update();
            int observed = 0;
            source.Updating = () =>
            {
                _realm.Query().OnAvailable(value => observed = ((ProbeGhost)value).Value);
                Assert.That(observed, Is.EqualTo(0));
                ghost.Value = 42;
            };
            _realm.Update();
            Assert.That(observed, Is.EqualTo(42));
        }

        private Blueprint Blueprint()
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
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            blueprint.Configure(Kind, ghost, new[]
            {
                new Blueprint.ViewMapping(First, DetailLevel.Full, first),
                new Blueprint.ViewMapping(Second, DetailLevel.Full, second)
            }, first);
            _assets.Add(root);
            _assets.Add(first);
            _assets.Add(second);
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
            internal static Action<ProbeGhost> Enabled;
            internal static Action<ProbeGhost> Disabled;
            internal int Value;

            private void OnEnable()
            {
                if (Enabled != null)
                {
                    Enabled(this);
                }
            }

            private void OnDisable()
            {
                if (Disabled != null)
                {
                    Disabled(this);
                }
            }
        }

        private sealed class ProbeView : MonoBehaviour, IViewOnly
        {
            internal static Action<ProbeView> Enabled;
            internal static Action<ProbeView> Disabled;

            private void OnEnable()
            {
                if (Enabled != null)
                {
                    Enabled(this);
                }
            }

            private void OnDisable()
            {
                if (Disabled != null)
                {
                    Disabled(this);
                }
            }
        }

        private sealed class ProbeSource : PresenceSource
        {
            internal Action Starting;
            internal Action Updating;
            internal int StartCount;
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

            internal void Depart(string id)
            {
                Remove(Kind, id);
            }

            internal void Queue(Action action)
            {
                Dispatch(action);
            }

            protected override void OnStart()
            {
                StartCount++;
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
