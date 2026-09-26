using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies callback publication and registration lifetimes.
    /// </summary>
    public sealed class CallbackPresenceDetectorTests
    {
        private static readonly Kind Population = new Kind("tests.callbacks");
        private Realm _realm;

        /// <summary>
        /// Creates a clean default realm.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            Realm.Default.Dispose();
            _realm = Realm.Default;
        }

        /// <summary>
        /// Releases all tracking and scene objects.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// An incomplete callback source reports the required setup steps and can be attached after configuration.
        /// </summary>
        [Test]
        public void Configuration_CanBeCompletedAfterRejectedAttachment()
        {
            Feed feed = new Feed();
            CallbackPresenceDetector<Item, Probe> source = new CallbackPresenceDetector<Item, Probe>(Population);
            Anchor anchor = _realm.GetOrCreateAnchor("callbacks");
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => anchor.AddDetector(source));
            Assert.That(error.Message, Does.Contain("IdentifyBy").And.Contain("Apply").And.Contain("Listen"));
            Assert.That(source.IsAttached, Is.False);
            source.IdentifyBy(item => item.Id).Apply((item, ghost) => ghost.Value = item.Value).Listen(feed.Subscribe);
            anchor.AddDetector(source);
            feed.Publish(new Item("a", 7));
            _realm.Update();
            Assert.That(Find("a").Value, Is.EqualTo(7));
        }

        /// <summary>
        /// Callback configuration is fixed for an attachment and becomes editable after detachment.
        /// </summary>
        [Test]
        public void Configuration_LockedUntilDetached()
        {
            Feed feed = new Feed();
            CallbackPresenceDetector<Item, Probe> source = Source(feed);
            Anchor anchor = _realm.GetOrCreateAnchor("callbacks", source);
            AssertConfigurationLocked(source);
            anchor.RemoveDetector(source);
            Assert.DoesNotThrow(() => source.IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.Value = item.Value)
                .WithVariant(item => item.Variant).Listen(feed.Subscribe));
        }

        /// <summary>
        /// Initial and later events defer mapping, preserve identity and retain untouched entities.
        /// </summary>
        [Test]
        public void Publication_SeedsUpdatesAndRemovesExplicitly()
        {
            Feed feed = new Feed();
            feed.Starting = () =>
            {
                feed.Publish(new Item("a", 1));
                feed.Publish(new Item("b", 2));
            };
            _realm.GetOrCreateAnchor("callbacks", Source(feed));
            Assert.That(_realm.Query().Count, Is.Zero);
            _realm.Update();
            Probe first = Find("a");
            Probe untouched = Find("b");
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

        /// <summary>
        /// Appearance selectors update and clear variants; omitted selectors preserve them on replacement.
        /// </summary>
        [Test]
        public void Variants_MapClearAndPreserve()
        {
            Feed feed = new Feed();
            CallbackPresenceDetector<Item, Probe> source = Source(feed).WithVariant(item => item.Variant);
            Anchor anchor = _realm.GetOrCreateAnchor("callbacks", source);
            feed.Publish(new Item("a", variant: "first"));
            _realm.Update();
            Probe ghost = Find("a");
            Assert.That(ghost.Variant, Is.EqualTo(new Variant("first")));
            feed.Publish(new Item("a"));
            _realm.Update();
            Assert.That(ghost.Variant, Is.EqualTo(Variant.None));
            feed.Publish(new Item("a", variant: "retained"));
            _realm.Update();
            Feed next = new Feed();
            anchor.ReplaceDetector(source, Source(next));
            next.Publish(new Item("a"));
            _realm.Update();
            Assert.That(Find("a"), Is.SameAs(ghost));
            Assert.That(ghost.Variant, Is.EqualTo(new Variant("retained")));
        }

        /// <summary>
        /// Main-thread events defer selectors and mapping, preserving publication/removal order.
        /// </summary>
        [Test]
        public void Dispatch_DefersCallbacksAndPreservesOrder()
        {
            Feed feed = new Feed();
            List<string> calls = new List<string>();
            List<Probe> ghosts = new List<Probe>();
            CallbackPresenceDetector<Item, Probe> source = Source(feed)
                .IdentifyBy(item =>
                {
                    calls.Add("identify:" + item.Value);
                    return item.Id;
                })
                .WithVariant(item =>
                {
                    calls.Add("variant:" + item.Value);
                    return item.Variant;
                })
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

        /// <summary>
        /// Detaching a callback source unsubscribes once and makes retained SDK callbacks harmless.
        /// </summary>
        [Test]
        public void Detachment_UnsubscribesAndIgnoresLateEvents()
        {
            Feed feed = new Feed();
            CallbackPresenceDetector<Item, Probe> source = Source(feed);
            Anchor anchor = _realm.GetOrCreateAnchor("callbacks", source);
            Action<Item> publish = feed.Publish;
            anchor.RemoveDetector(source);
            publish(new Item("late"));
            _realm.Update();
            _realm.Dispose();
            Assert.That(feed.Stops, Is.EqualTo(1));
            Assert.That(source.IsAttached, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// An invalid SDK item stops its subscription and removes only that subscription's ghosts.
        /// </summary>
        [Test]
        public void InvalidPublication_StopsOnlyItsSubscription()
        {
            Feed feed = new Feed();
            Feed healthy = new Feed();
            CallbackPresenceDetector<Item, Probe> source = Source(feed);
            _realm.GetOrCreateAnchor("callbacks", source, Source(healthy));
            feed.Publish(new Item("a"));
            healthy.Publish(new Item("healthy"));
            _realm.Update();
            Probe retained = Find("a");
            feed.Publish(null);
            ExpectedErrors.Verify(_realm.Update, "cannot publish a null item");
            Assert.That(feed.Stops, Is.EqualTo(1));
            Assert.That(retained.IsAvailable, Is.False);
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            IGhost found;
            Assert.That(_realm.TryGetGhost(retained.Key, out found), Is.False);
            Assert.That(_realm.Query().Single().Key.EntityId, Is.EqualTo("healthy"));
        }

        /// <summary>
        /// Replacement preserves roots during handover, then removes identities that were not republished.
        /// </summary>
        [Test]
        public void Replacement_RemovesUnreportedEntitiesAfterHandover()
        {
            Feed first = new Feed();
            CallbackPresenceDetector<Item, Probe> source = Source(first);
            Anchor anchor = _realm.GetOrCreateAnchor("callbacks", source);
            first.Publish(new Item("a"));
            first.Publish(new Item("b"));
            _realm.Update();
            Probe a = Find("a");
            Probe b = Find("b");
            Feed second = new Feed();
            CallbackPresenceDetector<Item, Probe> replacement = Source(second);
            anchor.ReplaceDetector(source, replacement);
            IGhost found;
            Assert.That(_realm.TryGetGhost(b.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(b));
            second.Publish(new Item("a"));
            _realm.Update();
            Assert.That(_realm.Query().Single(), Is.SameAs(a));
            Assert.That(b.IsAvailable, Is.False);
            Assert.That(b.gameObject.activeSelf, Is.False);
            Assert.That(_realm.TryGetGhost(b.Key, out found), Is.False);
            second.Publish(new Item("b"));
            _realm.Update();
            Assert.That(Find("b"), Is.Not.SameAs(b));
        }

        /// <summary>
        /// Value-type payloads, including their default value, are valid callback items.
        /// </summary>
        [Test]
        public void Publication_AcceptsValueTypePayloads()
        {
            _realm.GetOrCreateAnchor("callbacks", new CallbackPresenceDetector<int, Probe>(Population)
                .IdentifyBy(value => "a")
                .Apply((value, ghost) => ghost.Value = value)
                .Listen((publish, remove) =>
                {
                    publish(0);
                    return null;
                }));
            _realm.Update();
            Assert.That(Find("a").Value, Is.Zero);
        }

        private Probe Find(string id)
        {
            foreach (IGhost ghost in _realm.Query())
            {
                if (ghost.Key.EntityId == id)
                {
                    return (Probe)ghost;
                }
            }

            Assert.Fail("Missing ghost " + id);
            return null;
        }

        private static void AssertConfigurationLocked(CallbackPresenceDetector<Item, Probe> source)
        {
            Assert.Throws<InvalidOperationException>(() => source.IdentifyBy(item => item.Id));
            Assert.Throws<InvalidOperationException>(() => source.Apply((item, ghost) =>
            {
            }));
            Assert.Throws<InvalidOperationException>(() => source.WithVariant(item => item.Variant));
            Assert.Throws<InvalidOperationException>(() => source.Listen((publish, remove) => null));
        }

        private static CallbackPresenceDetector<Item, Probe> Source(Feed feed)
        {
            return new CallbackPresenceDetector<Item, Probe>(Population)
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.Value = item.Value)
                .Listen(feed.Subscribe);
        }

        private sealed class Feed
        {
            internal Action<Item> Publish;
            internal Action<string> Remove;
            internal Action Starting;
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
        }
    }
}
