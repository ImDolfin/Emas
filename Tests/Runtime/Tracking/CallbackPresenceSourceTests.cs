using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Verifies callback publication, registration lifetimes and Inspector integration.</summary>
    public sealed class CallbackPresenceSourceTests
    {
        private static readonly Kind Population = new Kind("tests.callbacks");
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private Realm _realm;

        /// <summary>Creates a clean default realm.</summary>
        [SetUp]
        public void SetUp()
        {
            Realm.Default.Dispose();
            _realm = Realm.Default;
        }

        /// <summary>Releases all tracking and scene objects.</summary>
        [TearDown]
        public void TearDown()
        {
            Probe.Disabled = null;
            _realm.Dispose();
            foreach (var value in _objects)
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
            _objects.Clear();
        }

        /// <summary>Invalid constructor and configuration arguments are rejected immediately.</summary>
        [Test]
        public void Configuration_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => new CallbackPresenceSource<Item, Probe>(default(Kind)));
            var source = new CallbackPresenceSource<Item, Probe>(Population);
            Assert.Throws<ArgumentNullException>(() => source.IdentifyBy(null));
            Assert.Throws<ArgumentNullException>(() => source.Apply(null));
            Assert.Throws<ArgumentNullException>(() => source.WithVariant(null));
            Assert.Throws<ArgumentNullException>(() => source.Listen(null));
        }

        /// <summary>Incomplete startup identifies only missing steps and permits a corrected retry.</summary>
        [TestCase("IdentifyBy")]
        [TestCase("Apply")]
        [TestCase("Listen")]
        [TestCase("IdentifyBy, Apply, Listen")]
        public void Configuration_ReportsMissingStepsAndAllowsRetry(string missing)
        {
            var feed = new Feed();
            var source = new CallbackPresenceSource<Item, Probe>(Population);
            if (!missing.Contains("IdentifyBy"))
            {
                source.IdentifyBy(item => item.Id);
            }
            if (!missing.Contains("Apply"))
            {
                source.Apply((item, ghost) => ghost.Value = item.Value);
            }
            if (!missing.Contains("Listen"))
            {
                source.Listen(feed.Subscribe);
            }
            var anchor = _realm.GetOrCreateAnchor("callbacks");
            var error = Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source));
            Assert.That(error.Message, Is.EqualTo("Callback source is missing required steps: " + missing + ". Configure them before tracking."));
            Assert.That(feed.Starts, Is.Zero);
            source.IdentifyBy(item => item.Id).Apply((item, ghost) => ghost.Value = item.Value).Listen(feed.Subscribe);
            anchor.AddSource(source);
            feed.Publish(new Item("a"));
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>Attached configuration stays locked after failure and unlocks upon detachment.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Configuration_LockedUntilDetached(bool fail)
        {
            var feed = new Feed();
            var source = Source(feed);
            var anchor = _realm.GetOrCreateAnchor("callbacks", source);
            if (fail)
            {
                feed.Publish(null);
                LogAssert.Expect(LogType.Exception, new Regex("cannot publish a null item"));
                _realm.Update();
            }
            AssertConfigurationLocked(source);
            anchor.RemoveSource(source);
            Assert.DoesNotThrow(() => source.IdentifyBy(item => item.Id).Apply((item, ghost) => { })
                .WithVariant(item => item.Variant).Listen(feed.Subscribe));
        }

        /// <summary>Initial and later events defer mapping, preserve identity and retain untouched entities.</summary>
        [Test]
        public void Publication_SeedsUpdatesAndRemovesExplicitly()
        {
            var feed = new Feed();
            feed.Starting = () => { feed.Publish(new Item("a", 1)); feed.Publish(new Item("b", 2)); };
            _realm.GetOrCreateAnchor("callbacks", Source(feed));
            Assert.That(_realm.Query().Count, Is.Zero);
            _realm.Update();
            var first = Find("a");
            var untouched = Find("b");
            feed.Publish(new Item("a", 3));
            Assert.That(first.Value, Is.EqualTo(1));
            _realm.Update();
            Assert.That(Find("a"), Is.SameAs(first));
            Assert.That(first.Value, Is.EqualTo(3));
            Assert.That(Find("b"), Is.SameAs(untouched));
            feed.Remove("missing");
            feed.Remove("a");
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(untouched));
            Assert.That(feed.Starts, Is.EqualTo(1));
        }

        /// <summary>Appearance selectors update and clear variants; omitted selectors preserve them on replacement.</summary>
        [Test]
        public void Variants_MapClearAndPreserve()
        {
            var feed = new Feed();
            var source = Source(feed).WithVariant(item => item.Variant);
            var anchor = _realm.GetOrCreateAnchor("callbacks", source);
            feed.Publish(new Item("a", variant: "first"));
            _realm.Update();
            var ghost = Find("a");
            Assert.That(ghost.Variant, Is.EqualTo(new Variant("first")));
            feed.Publish(new Item("a"));
            _realm.Update();
            Assert.That(ghost.Variant, Is.EqualTo(Variant.None));
            feed.Publish(new Item("a", variant: "retained"));
            _realm.Update();
            var next = new Feed();
            anchor.ReplaceSource(source, Source(next));
            next.Publish(new Item("a"));
            _realm.Update();
            Assert.That(Find("a"), Is.SameAs(ghost));
            Assert.That(ghost.Variant, Is.EqualTo(new Variant("retained")));
        }

        /// <summary>Main-thread events defer selectors and mapping, preserving publication/removal order.</summary>
        [Test]
        public void Dispatch_DefersCallbacksAndPreservesOrder()
        {
            var feed = new Feed();
            var calls = new List<string>();
            var ghosts = new List<Probe>();
            var source = Source(feed)
                .IdentifyBy(item => { calls.Add("identify:" + item.Value); return item.Id; })
                .WithVariant(item => { calls.Add("variant:" + item.Value); return item.Variant; })
                .Apply((item, ghost) =>
                {
                    calls.Add("apply:" + item.Value);
                    ghosts.Add(ghost);
                    ghost.Value = item.Value;
                });
            _realm.GetOrCreateAnchor("callbacks", source);
            feed.Publish(new Item("a", 1));
            feed.Remove("a");
            feed.Publish(new Item("a", 2));
            Assert.That(calls, Is.Empty);
            Assert.That(_realm.Query().Count, Is.Zero);
            _realm.Update();
            Assert.That(calls, Is.EqualTo(new[]
            {
                "identify:1", "variant:1", "apply:1",
                "identify:2", "variant:2", "apply:2"
            }));
            Assert.That(ghosts[1], Is.Not.SameAs(ghosts[0]), "Removal must occur before the second publication.");
            Assert.That(_realm.Query().Single(), Is.SameAs(ghosts[1]));
            Assert.That(ghosts[1].Value, Is.EqualTo(2));
        }

        /// <summary>Callback events share the realm budget and nested publication waits another update.</summary>
        [Test]
        public void Dispatch_UsesExistingBudgetAndDefersNestedEvents()
        {
            var feed = new Feed();
            var values = new List<int>();
            _realm.GetOrCreateAnchor("callbacks", Source(feed).Apply((item, ghost) =>
            {
                values.Add(item.Value);
                if (item.Value == 0)
                {
                    feed.Publish(new Item("a", 999));
                }
            }));
            for (var index = 0; index < Realm.MaxDispatchActionsPerUpdate + 2; index++)
            {
                feed.Publish(new Item("a", index));
            }
            _realm.Update();
            Assert.That(values.Count, Is.EqualTo(Realm.MaxDispatchActionsPerUpdate));
            _realm.Update();
            for (var index = 0; index < Realm.MaxDispatchActionsPerUpdate + 2; index++)
            {
                Assert.That(values[index], Is.EqualTo(index));
            }
            Assert.That(values[values.Count - 1], Is.EqualTo(999));
        }

        /// <summary>Old delegates and queued work cannot enter a later registration, including another realm.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Restart_RejectsOldCallbacksAndQueuedWork(bool otherRealm)
        {
            var feed = new Feed();
            var source = Source(feed);
            var anchor = _realm.GetOrCreateAnchor("callbacks", source);
            var oldPublish = feed.Publish;
            var oldRemove = feed.Remove;
            oldPublish(new Item("queued"));
            anchor.RemoveSource(source);
            using (var second = new Realm())
            {
                var destination = otherRealm ? second : _realm;
                destination.GetOrCreateAnchor("callbacks").AddSource(source);
                oldPublish(new Item("late"));
                feed.Publish(new Item("current", 5));
                oldRemove("current");
                _realm.Update();
                if (otherRealm)
                {
                    second.Update();
                }
                Assert.That(destination.Query().Count, Is.EqualTo(1));
                Assert.That(destination.Query().Single().Key.EntityId, Is.EqualTo("current"));
                Assert.That(feed.Starts, Is.EqualTo(2));
                Assert.That(feed.Stops, Is.EqualTo(1));
            }
        }

        /// <summary>Normal stop paths invoke cleanup once, even when the cleanup itself throws.</summary>
        [TestCase("source", false)]
        [TestCase("anchor", false)]
        [TestCase("realm", false)]
        [TestCase("replace", false)]
        [TestCase("source", true)]
        public void Cleanup_RunsOnce(string stop, bool throws)
        {
            var feed = new Feed();
            var source = Source(feed);
            var anchor = _realm.GetOrCreateAnchor("callbacks", source);
            if (throws)
            {
                feed.Stopping = () => { throw new InvalidOperationException("cleanup failed"); };
                LogAssert.Expect(LogType.Exception, new Regex("cleanup failed"));
            }
            if (stop == "source")
            {
                anchor.RemoveSource(source);
            }
            else if (stop == "anchor")
            {
                anchor.Dispose();
            }
            else if (stop == "realm")
            {
                _realm.Dispose();
            }
            else
            {
                anchor.ReplaceSource(source, Source(new Feed()));
            }
            _realm.Dispose();
            feed.Publish(new Item("late"));
            Assert.That(feed.Stops, Is.EqualTo(1));
        }

        /// <summary>A listener without resources can return null cleanup.</summary>
        [Test]
        public void Listen_AllowsNullCleanup()
        {
            _realm.GetOrCreateAnchor("callbacks", Source(new Feed()).Listen((publish, remove) =>
            {
                publish(new Item("a"));
                return null;
            }));
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>Stopping during subscription still cleans up the returned handle and locks configuration.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Listen_InterruptedStartupCleansUpLateReturn(bool throws)
        {
            var anchor = _realm.GetOrCreateAnchor("callbacks");
            var source = Source(new Feed());
            var stops = 0;
            source.Listen((publish, remove) =>
            {
                publish(new Item("stale"));
                anchor.RemoveSource(source);
                AssertConfigurationLocked(source);
                return () =>
                {
                    stops++;
                    if (throws)
                    {
                        throw new InvalidOperationException("late cleanup failed");
                    }
                };
            });
            if (throws)
            {
                LogAssert.Expect(LogType.Exception, new Regex("late cleanup failed"));
            }
            anchor.AddSource(source);
            _realm.Update();
            Assert.That(stops, Is.EqualTo(1));
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>Nested restart keeps the new registration's cleanup separate from the interrupted one.</summary>
        [Test]
        public void Listen_RestartDuringSubscriptionPreservesNewCleanup()
        {
            var anchor = _realm.GetOrCreateAnchor("callbacks");
            var source = Source(new Feed());
            var starts = 0;
            var oldStops = 0;
            var newStops = 0;
            source.Listen((publish, remove) =>
            {
                starts++;
                if (starts == 1)
                {
                    anchor.RemoveSource(source);
                    anchor.AddSource(source);
                    return () => oldStops++;
                }
                publish(new Item("new"));
                return () => newStops++;
            });
            anchor.AddSource(source);
            _realm.Update();
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("new"));
            Assert.That(oldStops, Is.EqualTo(1));
            Assert.That(newStops, Is.Zero);
            anchor.RemoveSource(source);
            Assert.That(newStops, Is.EqualTo(1));
        }

        /// <summary>A throwing listener rolls back startup; its queued events stay invalid after correction.</summary>
        [Test]
        public void Listen_ThrowingStartupCanBeCorrected()
        {
            var anchor = _realm.GetOrCreateAnchor("callbacks");
            var source = Source(new Feed()).Listen((publish, remove) =>
            {
                publish(new Item("old"));
                throw new InvalidOperationException("listen failed");
            });
            Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source));
            var feed = new Feed();
            source.Listen(feed.Subscribe);
            anchor.AddSource(source);
            feed.Publish(new Item("new"));
            _realm.Update();
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("new"));
        }

        /// <summary>Bad events stop only their source, retain identities and permit replacement recovery.</summary>
        [TestCase("null-item")]
        [TestCase("empty-id")]
        [TestCase("null-id")]
        [TestCase("empty-removal")]
        [TestCase("null-removal")]
        [TestCase("identify")]
        [TestCase("variant")]
        [TestCase("apply")]
        public void Failure_StopsAndRetainsPopulation(string failure)
        {
            var feed = new Feed();
            var fail = false;
            var source = Source(feed)
                .IdentifyBy(item =>
                {
                    if (fail && failure == "identify")
                    {
                        throw new InvalidOperationException("identify failed");
                    }
                    return item.Id;
                })
                .WithVariant(item =>
                {
                    if (fail && failure == "variant")
                    {
                        throw new InvalidOperationException("variant failed");
                    }
                    return item.Variant;
                })
                .Apply((item, ghost) =>
                {
                    if (fail && failure == "apply")
                    {
                        throw new InvalidOperationException("apply failed");
                    }
                    ghost.Value = item.Value;
                });
            var healthy = new Feed();
            var anchor = _realm.GetOrCreateAnchor("callbacks", source, Source(healthy));
            feed.Publish(new Item("a"));
            healthy.Publish(new Item("healthy"));
            _realm.Update();
            var retained = Find("a");
            fail = true;
            if (failure.EndsWith("removal"))
            {
                feed.Remove(failure == "null-removal" ? null : "");
            }
            else
            {
                feed.Publish(failure == "null-item" ? null : new Item(
                    failure == "empty-id" ? "" : failure == "null-id" ? null : "a"));
            }
            feed.Remove("a");
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            _realm.Update();
            Assert.That(feed.Stops, Is.EqualTo(1));
            Assert.That(retained.IsAvailable, Is.False);
            Assert.That(_realm.GetOwnedGhosts(source).Count, Is.EqualTo(1));
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("healthy"));
            var recovery = new Feed();
            anchor.ReplaceSource(source, Source(recovery));
            recovery.Publish(new Item("a", 42));
            _realm.Update();
            Assert.That(Find("a"), Is.SameAs(retained));
            Assert.That(retained.Value, Is.EqualTo(42));
            Assert.That(feed.Stops, Is.EqualTo(1));
        }

        /// <summary>Application callbacks can end tracking without later mapping or stale publication.</summary>
        [TestCase("identify")]
        [TestCase("variant")]
        [TestCase("apply")]
        public void Publication_RechecksRegistrationAfterUserCallbacks(string stage)
        {
            var feed = new Feed();
            var anchor = _realm.GetOrCreateAnchor("callbacks");
            var source = Source(feed);
            var variants = 0;
            var mappings = 0;
            source.IdentifyBy(item =>
            {
                if (stage == "identify")
                {
                    anchor.RemoveSource(source);
                }
                return item.Id;
            }).WithVariant(item =>
            {
                variants++;
                if (stage == "variant")
                {
                    anchor.RemoveSource(source);
                }
                return item.Variant;
            }).Apply((item, ghost) =>
            {
                mappings++;
                anchor.RemoveSource(source);
            });
            anchor.AddSource(source);
            feed.Publish(new Item("a"));
            feed.Publish(new Item("b"));
            _realm.Update();
            Assert.That(variants, Is.EqualTo(stage == "identify" ? 0 : 1));
            Assert.That(mappings, Is.EqualTo(stage == "apply" ? 1 : 0));
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(feed.Stops, Is.EqualTo(1));
        }

        /// <summary>Replacement retains unreported identities as unavailable instead of inferring removals.</summary>
        [Test]
        public void Replacement_DoesNotReconcileUnreportedEntities()
        {
            var first = new Feed();
            var source = Source(first);
            var anchor = _realm.GetOrCreateAnchor("callbacks", source);
            first.Publish(new Item("a"));
            first.Publish(new Item("b"));
            _realm.Update();
            var a = Find("a");
            var b = Find("b");
            var second = new Feed();
            var replacement = Source(second);
            anchor.ReplaceSource(source, replacement);
            second.Publish(new Item("a"));
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(a));
            Assert.That(b.IsAvailable, Is.False);
            Assert.That(_realm.GetOwnedGhosts(replacement).Count, Is.EqualTo(2));
            second.Publish(new Item("b"));
            _realm.Update();
            Assert.That(Find("b"), Is.SameAs(b));
        }

        /// <summary>Inspector tracking creates views, cleans up on disable and permits fresh subscriptions.</summary>
        [Test]
        public void SceneSetup_ManifestsCleansUpAndRestarts()
        {
            var owner = new GameObject("callback setup");
            _objects.Add(owner);
            var setup = owner.AddComponent<SceneSetup>();
            var prefab = new GameObject("callback view");
            _objects.Add(prefab);
            prefab.SetActive(false);
            var blueprint = ScriptableObject.CreateInstance<Blueprint>();
            _objects.Add(blueprint);
            blueprint.Configure(Population, null, new Blueprint.ViewMapping[0], prefab);
            typeof(SceneSetup).GetField("_blueprints", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(setup, new[] { blueprint });
            var feed = new Feed();
            var source = Source(feed);
            setup.Track(source);
            var oldPublish = feed.Publish;
            feed.Publish(new Item("a"));
            _realm.Update();
            Assert.That(Find("a").GetComponentInChildren<View>(), Is.Not.Null);
            setup.enabled = false;
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(feed.Stops, Is.EqualTo(1));
            setup.enabled = true;
            setup.Track(source);
            oldPublish(new Item("old"));
            feed.Publish(new Item("b"));
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
            Assert.That(Find("b").GetComponentInChildren<View>(), Is.Not.Null);
        }

        /// <summary>Failure-triggered scene callbacks can restart a source without losing or stopping the wrong subscription.</summary>
        [Test]
        public void Failure_ReentrantRestartCleansUpOnlyTheOldSubscription()
        {
            var feed = new Feed();
            var stops = new List<int>();
            var starts = 0;
            var fail = false;
            var source = Source(feed).Apply((item, ghost) =>
            {
                if (fail)
                {
                    throw new InvalidOperationException("restart failure");
                }
            }).Listen((publish, remove) =>
            {
                var subscription = ++starts;
                feed.Publish = publish;
                feed.Remove = remove;
                return () => stops.Add(subscription);
            });
            var anchor = _realm.GetOrCreateAnchor("callbacks", source);
            feed.Publish(new Item("a"));
            _realm.Update();
            Probe.Disabled = () =>
            {
                Probe.Disabled = null;
                anchor.RemoveSource(source);
                anchor.AddSource(source);
            };
            fail = true;
            feed.Publish(new Item("a"));
            LogAssert.Expect(LogType.Exception, new Regex("restart failure"));
            _realm.Update();
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(stops, Is.EqualTo(new[] { 1 }));
            fail = false;
            feed.Publish(new Item("b"));
            _realm.Update();
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("b"));
            anchor.RemoveSource(source);
            Assert.That(stops, Is.EqualTo(new[] { 1, 2 }));
        }

        /// <summary>Value-type payloads, including their default value, are valid callback items.</summary>
        [Test]
        public void Publication_AcceptsValueTypePayloads()
        {
            _realm.GetOrCreateAnchor("callbacks", new CallbackPresenceSource<int, Probe>(Population)
                .IdentifyBy(value => "a")
                .Apply((value, ghost) => ghost.Value = value)
                .Listen((publish, remove) => { publish(0); return null; }));
            _realm.Update();
            Assert.That(Find("a").Value, Is.Zero);
        }

        private Probe Find(string id)
        {
            foreach (var ghost in _realm.Query())
            {
                if (ghost.Key.EntityId == id)
                {
                    return (Probe)ghost;
                }
            }
            Assert.Fail("Missing ghost " + id);
            return null;
        }

        private static void AssertConfigurationLocked(CallbackPresenceSource<Item, Probe> source)
        {
            Assert.Throws<InvalidOperationException>(() => source.IdentifyBy(item => item.Id));
            Assert.Throws<InvalidOperationException>(() => source.Apply((item, ghost) => { }));
            Assert.Throws<InvalidOperationException>(() => source.WithVariant(item => item.Variant));
            Assert.Throws<InvalidOperationException>(() => source.Listen((publish, remove) => null));
        }

        private static CallbackPresenceSource<Item, Probe> Source(Feed feed)
        {
            return new CallbackPresenceSource<Item, Probe>(Population)
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.Value = item.Value)
                .Listen(feed.Subscribe);
        }

        private sealed class Feed
        {
            internal Action<Item> Publish;
            internal Action<string> Remove;
            internal Action Starting;
            internal Action Stopping;
            internal int Starts;
            internal int Stops;

            internal Action Subscribe(Action<Item> publish, Action<string> remove)
            {
                Starts++;
                Publish = publish;
                Remove = remove;
                if (Starting != null)
                {
                    Starting();
                }
                return () =>
                {
                    Stops++;
                    if (Stopping != null)
                    {
                        Stopping();
                    }
                };
            }
        }

        private sealed class Item
        {
            internal Item(string id, int value = 0, string variant = null)
            {
                Id = id;
                Value = value;
                Variant = variant == null ? Variant.None : new Variant(variant);
            }

            internal readonly string Id;
            internal readonly int Value;
            internal readonly Variant Variant;
        }

        private sealed class Probe : Ghost
        {
            internal int Value;
            internal static Action Disabled;

            private void OnDisable()
            {
                if (Disabled != null)
                {
                    Disabled();
                }
            }
        }
    }
}
