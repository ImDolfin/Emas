using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Exercises lifecycle transactions against real Unity activation and destruction.</summary>
    public sealed class LifecycleTests
    {
        private static readonly Kind Kind = new Kind("tests.lifecycle");
        private static readonly Variant First = new Variant("first");
        private static readonly Variant Second = new Variant("second");
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private Context _context;

        /// <summary>Creates an isolated context and clears callback probes.</summary>
        [SetUp]
        public void SetUp()
        {
            ProbeGhost.Enabled = null;
            ProbeGhost.Disabled = null;
            ProbeView.Enabled = null;
            ProbeView.Disabled = null;
            _context = new Context();
        }

        /// <summary>Disposes the context and destroys temporary authoring assets.</summary>
        [TearDown]
        public void TearDown()
        {
            ProbeGhost.Enabled = null;
            ProbeGhost.Disabled = null;
            ProbeView.Enabled = null;
            ProbeView.Disabled = null;
            _context.Dispose();
            for (var index = 0; index < _assets.Count; index++)
            {
                if (_assets[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_assets[index]);
                }
            }
            _assets.Clear();
        }

        /// <summary>Rejected attachment cannot remove the coordinator's original population.</summary>
        [Test]
        public void SecondOriginAttachment_PreservesOriginalGhosts()
        {
            var source = new ProbeCoordinator();
            var firstOrigin = _context.CreateOriginFor("first", source);
            var ghost = source.Publish("car");
            _context.Update();
            Assert.Throws<InvalidOperationException>(() => _context.CreateOriginFor("second", source));
            Assert.That(_context.Query().Single(), Is.SameAs(ghost));
            Assert.That(ghost.transform.parent, Is.SameAs(firstOrigin.Transform));
            var secondOrigin = _context.CreateOriginFor("second");
            Assert.Throws<InvalidOperationException>(() => secondOrigin.AddCoordinator(source));
            Assert.That(_context.Query().Single(), Is.SameAs(ghost));
        }

        /// <summary>Failed startup preserves an existing prepared identity but removes newly created records.</summary>
        [Test]
        public void FailedAttachment_RollsBackOnlyItsNewGhosts()
        {
            var origin = _context.CreateOriginFor("origin");
            var prepared = _context.Prepare<ProbeGhost>("origin", Kind, "prepared");
            var source = new ProbeCoordinator();
            source.Starting = () =>
            {
                source.Publish("prepared");
                source.Publish("new");
                throw new InvalidOperationException("start failed");
            };
            Assert.Throws<InvalidOperationException>(() => origin.AddCoordinator(source));
            Assert.That(source.StopCount, Is.EqualTo(1));
            var replacement = new ProbeCoordinator();
            origin.AddCoordinator(replacement);
            Assert.That(replacement.Publish("prepared"), Is.SameAs(prepared));
            _context.Update();
            Assert.That(_context.Query().Count, Is.EqualTo(1));
        }

        /// <summary>A disposed context cannot create unmanaged scene objects or accept mutations.</summary>
        [Test]
        public void DisposedContext_RejectsMutationsAndKeepsDisposeIdempotent()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Update();
            var blueprint = Blueprint();
            _context.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _context.CreateOriginFor("late"));
            Assert.Throws<ObjectDisposedException>(() => _context.RegisterBlueprint(blueprint));
            Assert.Throws<ObjectDisposedException>(() => _context.Prepare<ProbeGhost>("origin", Kind, "late"));
            Assert.Throws<ObjectDisposedException>(() => _context.Manifest(ghost));
            Assert.Throws<ObjectDisposedException>(() => _context.Demanifest(ghost));
            Assert.Throws<ObjectDisposedException>(() => _context.SetDegree(ghost, DetailLevel.Full));
            Assert.Throws<ObjectDisposedException>(() => _context.RemoveOrigin("origin"));
            Assert.Throws<ObjectDisposedException>(() => _context.Query().OnAvailable(value => { }));
            Assert.DoesNotThrow(() => _context.Dispose());
            Assert.DoesNotThrow(() => _context.Update());
            Assert.That(_context.Query().Count, Is.EqualTo(0));
        }

        /// <summary>Direct origin disposal unregisters prepared records immediately and rejects mutations.</summary>
        [Test]
        public void DisposedOrigin_UnregistersBeforeDeferredDestruction()
        {
            var source = new ProbeCoordinator();
            var origin = _context.CreateOriginFor("origin", source);
            var prepared = _context.Prepare<ProbeGhost>("origin", Kind, "prepared");
            origin.Dispose();
            Assert.Throws<ObjectDisposedException>(() => origin.AddCoordinator(new ProbeCoordinator()));
            Assert.Throws<ObjectDisposedException>(() => origin.RemoveCoordinator(source));
            Assert.Throws<ObjectDisposedException>(() => origin.ReplaceCoordinator(source, new ProbeCoordinator()));
            var next = _context.CreateOriginFor("origin");
            Assert.That(next, Is.Not.SameAs(origin));
            Assert.That(_context.Prepare<ProbeGhost>("origin", Kind, "prepared"), Is.Not.SameAs(prepared));
        }

        /// <summary>Root activation may add records without invalidating a dictionary enumeration.</summary>
        [Test]
        public void OnEnable_CanPrepareAnotherGhost()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            ProbeGhost prepared = null;
            ProbeGhost.Enabled = ghost =>
            {
                if (ghost.Key.EntityId == "car")
                {
                    prepared = _context.Prepare<ProbeGhost>("origin", Kind, "late");
                }
            };
            source.Publish("car");
            _context.Update();
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_context.Query().Count, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(0));
        }

        /// <summary>A root can remove itself during activation without being manifested afterward.</summary>
        [UnityTest]
        public IEnumerator OnEnable_CanRemoveItsOwnGhost()
        {
            _context.RegisterBlueprint(Blueprint());
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Manifest(ghost);
            var views = 0;
            ProbeView.Enabled = view => views++;
            ProbeGhost.Enabled = value => source.Depart(value.Key.EntityId);
            _context.Update();
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            Assert.That(views, Is.EqualTo(0));
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            yield return null;
            Assert.That(ghost == null, Is.True);
        }

        /// <summary>Disposal inside activation cancels remaining work and later notifications.</summary>
        [Test]
        public void OnEnable_CanDisposeContext()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            source.Publish("first");
            source.Publish("second");
            var notifications = 0;
            _context.Query().OnAvailable(ghost => notifications++);
            ProbeGhost.Enabled = ghost => _context.Dispose();
            _context.Update();
            Assert.That(notifications, Is.EqualTo(0));
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            Assert.That(source.StopCount, Is.EqualTo(1));
        }

        /// <summary>Deactivation may remove another record during ownership transfer.</summary>
        [Test]
        public void OnDisable_CanRemoveOriginDuringReplacement()
        {
            var source = new ProbeCoordinator();
            var origin = _context.CreateOriginFor("origin", source);
            source.Publish("first");
            source.Publish("second");
            _context.Update();
            var replacement = new ProbeCoordinator();
            ProbeGhost.Disabled = ghost => _context.RemoveOrigin("origin");
            Assert.DoesNotThrow(() => origin.ReplaceCoordinator(source, replacement));
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            Assert.That(replacement.StartCount, Is.EqualTo(0));
        }

        /// <summary>A replacement failure keeps compatible identities unavailable for a subsequent recovery.</summary>
        [Test]
        public void FailedReplacement_PreservesUnavailableIdentityAndCanRecover()
        {
            var source = new ProbeCoordinator();
            var origin = _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Update();
            var failed = new ProbeCoordinator();
            failed.Starting = () =>
            {
                failed.Publish("car");
                throw new InvalidOperationException("replacement failed");
            };
            Assert.Throws<InvalidOperationException>(() => origin.ReplaceCoordinator(source, failed));
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            var otherOrigin = _context.CreateOriginFor("other");
            Assert.Throws<InvalidOperationException>(() => otherOrigin.AddCoordinator(failed));
            var recovery = new ProbeCoordinator();
            origin.ReplaceCoordinator(failed, recovery);
            Assert.That(recovery.Publish("car"), Is.SameAs(ghost));
            _context.Update();
            Assert.That(_context.Query().Single(), Is.SameAs(ghost));
        }

        /// <summary>Variant-driven view activation observes the completed source update.</summary>
        [Test]
        public void VariantViewRefresh_WaitsUntilAllSourceUpdatesFinish()
        {
            _context.RegisterBlueprint(Blueprint());
            var first = new ProbeCoordinator();
            var second = new ProbeCoordinator();
            _context.CreateOriginFor("origin", first, second);
            var ghost = first.Publish("car", First, 1);
            var other = second.Publish("other", First, 10);
            _context.Update();
            var initial = _context.Manifest(ghost);
            var observed = new List<int>();
            ProbeView.Enabled = view =>
            {
                var data = (ProbeGhost)view.GetComponent<global::Emas.View>().Ghost;
                observed.Add(data.Value + other.Value);
            };
            first.Updating = () =>
            {
                var updated = first.Publish("car", Second, 2);
                _context.Manifest(updated);
                updated.Value = 3;
                Assert.That(observed.Count, Is.EqualTo(0));
            };
            second.Updating = () => other.Value = 20;
            _context.Update();
            Assert.That(observed, Is.EqualTo(new[] { 23 }));
            Assert.That(initial.gameObject.activeSelf, Is.False);
            Assert.That(_context.Manifest(ghost).gameObject.name, Is.EqualTo("Second view"));
        }

        /// <summary>Availability notification occurs after a previously requested view is bound and active.</summary>
        [Test]
        public void AvailabilityNotification_FollowsViewRefresh()
        {
            _context.RegisterBlueprint(Blueprint());
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Manifest(ghost);
            var hadView = false;
            _context.Query().OnAvailable(value => hadView = ghost.GetComponentInChildren<global::Emas.View>() != null);
            _context.Update();
            Assert.That(hadView, Is.True);
        }

        /// <summary>A view activation callback can demanifest itself without resurrecting the view.</summary>
        [UnityTest]
        public IEnumerator ViewOnEnable_CanDemanifestItself()
        {
            _context.RegisterBlueprint(Blueprint());
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Update();
            ProbeView.Enabled = view => _context.Demanifest(ghost);
            Assert.That(_context.Manifest(ghost), Is.Null);
            yield return null;
            Assert.That(ghost.GetComponentInChildren<global::Emas.View>(true), Is.Null);
            Assert.That(ghost.IsAvailable, Is.True);
        }

        /// <summary>Deactivating an old view may remove the ghost without constructing a replacement.</summary>
        [UnityTest]
        public IEnumerator ViewOnDisable_CanRemoveGhostDuringVariantChange()
        {
            _context.RegisterBlueprint(Blueprint());
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Update();
            _context.Manifest(ghost);
            var newViews = 0;
            ProbeView.Enabled = view => newViews++;
            ProbeView.Disabled = view => source.Depart("car");
            source.Updating = () => source.Publish("car", Second, 2);
            _context.Update();
            Assert.That(newViews, Is.EqualTo(0));
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            yield return null;
            Assert.That(ghost == null, Is.True);
        }

        /// <summary>A subscription callback cannot notify an identity removed by an earlier callback.</summary>
        [Test]
        public void Subscription_RechecksMatchesAfterCallbackMutation()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            source.Publish("first");
            source.Publish("second");
            _context.Update();
            var calls = 0;
            _context.Query().OnAvailable(ghost =>
            {
                calls++;
                source.Depart("first");
                source.Depart("second");
            });
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>Disposing during notification suppresses subsequent matches.</summary>
        [Test]
        public void Subscription_CanDisposeItselfDuringNotification()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            IDisposable subscription = null;
            var calls = 0;
            subscription = _context.Query().OnAvailable(ghost =>
            {
                calls++;
                subscription.Dispose();
            });
            source.Publish("first");
            source.Publish("second");
            _context.Update();
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>Typed interface filters retain conjunction, root-only lookup and immutable descriptions.</summary>
        [Test]
        public void TypedQuery_ComposesAndExcludesViewOnlyContracts()
        {
            _context.RegisterBlueprint(Blueprint());
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            _context.Update();
            _context.Manifest(ghost);
            var query = _context.Query().With<IProbeData>();
            Assert.That(query.Count, Is.EqualTo(1));
            Assert.That(query.With<IViewOnly>().Count, Is.EqualTo(0));
            Assert.That(query.Count, Is.EqualTo(1));
        }

        /// <summary>Nested dispatch is deferred and cannot starve coordinator updates.</summary>
        [Test]
        public void Dispatch_RequeuedActionRunsNextUpdate()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var calls = 0;
            Action repeat = null;
            repeat = () =>
            {
                calls++;
                source.Queue(repeat);
            };
            source.Queue(repeat);
            _context.Update();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(source.UpdateCount, Is.EqualTo(1));
            _context.Update();
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>A large backlog is processed over multiple updates in FIFO order.</summary>
        [Test]
        public void Dispatch_BacklogRespectsBudgetAndOrder()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var calls = new List<int>();
            var count = Context.MaxDispatchActionsPerUpdate + 7;
            for (var index = 0; index < count; index++)
            {
                var value = index;
                source.Queue(() => calls.Add(value));
            }
            _context.Update();
            Assert.That(calls.Count, Is.EqualTo(Context.MaxDispatchActionsPerUpdate));
            Assert.That(source.UpdateCount, Is.EqualTo(1));
            _context.Update();
            Assert.That(calls.Count, Is.EqualTo(count));
            for (var index = 0; index < count; index++)
            {
                Assert.That(calls[index], Is.EqualTo(index));
            }
        }

        /// <summary>A reused coordinator cannot execute queued actions from its previous registration.</summary>
        [Test]
        public void Dispatch_ReattachedInstanceDiscardsOldGeneration()
        {
            var source = new ProbeCoordinator();
            var origin = _context.CreateOriginFor("origin", source);
            var calls = 0;
            source.Queue(() => calls++);
            origin.RemoveCoordinator(source);
            origin.AddCoordinator(source);
            source.Queue(() => calls += 10);
            _context.Update();
            Assert.That(calls, Is.EqualTo(10));
        }

        /// <summary>A failing source deactivates only its own population and stops once.</summary>
        [Test]
        public void SourceFailure_DoesNotStopOtherCoordinators()
        {
            var source = new ProbeCoordinator();
            var other = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source, other);
            var failedGhost = source.Publish("failed");
            var healthyGhost = other.Publish("healthy");
            _context.Update();
            source.Updating = () => { throw new InvalidOperationException("probe failure"); };
            LogAssert.Expect(LogType.Exception, new Regex("probe failure"));
            _context.Update();
            Assert.That(failedGhost.IsAvailable, Is.False);
            Assert.That(failedGhost.gameObject.activeSelf, Is.False);
            Assert.That(_context.Query().Single(), Is.SameAs(healthyGhost));
            Assert.That(source.StopCount, Is.EqualTo(1));
            _context.Update();
            Assert.That(source.StopCount, Is.EqualTo(1));
            Assert.That(other.UpdateCount, Is.EqualTo(3));
        }

        /// <summary>Scene unload removes both published and prepared roots and permits identity reuse.</summary>
        [UnityTest]
        public IEnumerator SceneUnload_RemovesOriginAndAllItsRecords()
        {
            var scene = SceneManager.CreateScene("Emas lifecycle " + Guid.NewGuid().ToString("N"));
            var frame = new GameObject("Scene frame");
            SceneManager.MoveGameObjectToScene(frame, scene);
            var source = new ProbeCoordinator();
            var origin = _context.CreateOriginFor("scene", frame.transform, source);
            var ghost = source.Publish("published");
            var prepared = _context.Prepare<ProbeGhost>("scene", Kind, "prepared");
            _context.Update();
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            Assert.That(ghost == null, Is.True);
            Assert.That(prepared == null, Is.True);
            Assert.That(source.StopCount, Is.EqualTo(1));
            var next = _context.CreateOriginFor("scene");
            Assert.That(next, Is.Not.SameAs(origin));
            Assert.That(_context.Prepare<ProbeGhost>("scene", Kind, "prepared"), Is.Not.Null);
        }

        /// <summary>Ownership changes during root activation defer publication until the new source is finalized.</summary>
        [Test]
        public void OnEnable_CanReplaceCoordinatorWithoutPublishingStaleAvailability()
        {
            var source = new ProbeCoordinator();
            var origin = _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car");
            var replacement = new ProbeCoordinator();
            replacement.Starting = () => replacement.Publish("car", Second, 42);
            var replaced = false;
            ProbeGhost.Enabled = value =>
            {
                if (!replaced)
                {
                    replaced = true;
                    origin.ReplaceCoordinator(source, replacement);
                }
            };
            var notifications = 0;
            _context.Query().OnAvailable(value => notifications++);
            _context.Update();
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(notifications, Is.EqualTo(0));
            _context.Update();
            Assert.That(_context.Query().Single(), Is.SameAs(ghost));
            Assert.That(ghost.Value, Is.EqualTo(42));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(1));
        }

        /// <summary>Dispatched source failure cannot expose its partially populated ghost.</summary>
        [Test]
        public void DispatchFailure_KeepsPartialPublicationUnavailable()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            ProbeGhost ghost = null;
            source.Queue(() =>
            {
                ghost = source.Publish("car");
                throw new InvalidOperationException("dispatch failure");
            });
            LogAssert.Expect(LogType.Exception, new Regex("dispatch failure"));
            _context.Update();
            Assert.That(ghost.IsAvailable, Is.False);
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            Assert.That(_context.Query().Count, Is.EqualTo(0));
            Assert.That(source.StopCount, Is.EqualTo(1));
        }

        /// <summary>A subscription created while applying source data waits for the completed update.</summary>
        [Test]
        public void SubscriptionCreatedDuringSourceUpdate_WaitsForFinalValues()
        {
            var source = new ProbeCoordinator();
            _context.CreateOriginFor("origin", source);
            var ghost = source.Publish("car", First, 1);
            _context.Update();
            var observed = 0;
            source.Updating = () =>
            {
                _context.Query().OnAvailable(value => observed = ((ProbeGhost)value).Value);
                Assert.That(observed, Is.EqualTo(0));
                ghost.Value = 42;
            };
            _context.Update();
            Assert.That(observed, Is.EqualTo(42));
        }

        private Blueprint Blueprint()
        {
            var root = new GameObject("Ghost template");
            root.SetActive(false);
            var ghost = root.AddComponent<ProbeGhost>();
            var first = new GameObject("First view");
            first.SetActive(false);
            first.AddComponent<ProbeView>();
            var second = new GameObject("Second view");
            second.SetActive(false);
            second.AddComponent<ProbeView>();
            var blueprint = ScriptableObject.CreateInstance<Blueprint>();
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

        private sealed class ProbeCoordinator : Coordinator
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
                var ghost = GetOrCreate<ProbeGhost>(id, Kind, variant);
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
