using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises paired query notifications across removal, recovery and callback mutation.
    /// </summary>
    public sealed class ObservationTests
    {
        private Realm _realm;
        private Probe _source;
        private Anchor _anchor;

        /// <summary>
        /// Creates an isolated query population.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
            _source = new Probe();
            _anchor = _realm.GetOrCreateAnchor("observe", _source);
        }

        /// <summary>
        /// Releases subscriptions and scene objects.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Existing matches arrive immediately and explicit departures wait for the update phase.
        /// </summary>
        [Test]
        public void Observe_ReportsInitialAndFutureMembership()
        {
            _source.Publish("first");
            _realm.Update();
            List<string> events = new List<string>();
            IDisposable subscription = _realm.Query().Observe(
                ghost => events.Add("enter " + ghost.Key.EntityId),
                key => events.Add("leave " + key.EntityId));
            Assert.That(events, Is.EqualTo(new[] { "enter first" }));
            _source.Publish("second");
            _realm.Update();
            _source.RemoveId("first");
            Assert.That(events.Count, Is.EqualTo(2));
            _realm.Update();
            _realm.Update();
            Assert.That(events, Is.EqualTo(new[] { "enter first", "enter second", "leave first" }));
            subscription.Dispose();
            _source.RemoveId("second");
            _realm.Update();
            Assert.That(events.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Departure identities remain usable after Unity has destroyed the removed component.
        /// </summary>
        [UnityTest]
        public IEnumerator Departure_UsesStableKeyAfterObjectDestruction()
        {
            TestGhost ghost = _source.Publish("removed");
            _realm.Update();
            Key expected = ghost.Key;
            List<Key> departed = new List<Key>();
            _realm.Query().Observe(item =>
            {
            }, departed.Add);
            _source.RemoveId("removed");
            yield return null;
            Assert.That(ghost == null, Is.True);
            _realm.Update();
            Assert.That(departed, Is.EqualTo(new[] { expected }));
        }

        /// <summary>
        /// Filter changes report one departure and a fresh entry when the ghost matches again.
        /// </summary>
        [Test]
        public void Observe_ReportsVariantMembershipChanges()
        {
            Variant red = new Variant("red");
            _source.Publish("one", red, "selected");
            _realm.Update();
            Query query = _realm.Query().WithVariant(red);
            List<string> events = new List<string>();
            query.Observe(ghost => events.Add("enter"), key => events.Add("leave"));
            _source.Publish("one", new Variant("blue"), "other");
            _realm.Update();
            _realm.Update();
            _source.Publish("one", red, "selected");
            _realm.Update();
            Assert.That(events, Is.EqualTo(new[] { "enter", "leave", "enter" }));
        }

        /// <summary>
        /// Restart and immediate recovery retain an observable break in availability.
        /// </summary>
        [Test]
        public void Observe_ReportsLossBeforeRecovery()
        {
            TestGhost ghost = _source.Publish("one");
            _realm.Update();
            List<string> events = new List<string>();
            _realm.Query().Observe(item => events.Add("enter"), key => events.Add("leave"));
            _anchor.RestartDetector(_source);

            Assert.That(_source.Publish("one"), Is.SameAs(ghost));
            Assert.That(events, Is.EqualTo(new[] { "enter" }));
            _realm.Update();
            Assert.That(events, Is.EqualTo(new[] { "enter", "leave", "enter" }));
        }

        /// <summary>
        /// Departure exceptions are isolated so other observers still receive the departure.
        /// </summary>
        [Test]
        public void Observe_IsolatesDepartureExceptions()
        {
            _source.Publish("one");
            _realm.Update();
            int healthy = 0;
            _realm.Query().Observe(ghost =>
            {
            }, key =>
            {
                throw new Exception("leave failed");
            });
            _realm.Query().Observe(ghost =>
            {
            }, key => healthy++);
            _source.RemoveId("one");
            ExpectedErrors.Verify(_realm.Update, "query departure.*one.*leave failed");
            Assert.That(healthy, Is.EqualTo(1));
            Assert.That(_source.IsActive, Is.True);
        }

        /// <summary>
        /// Disposing the subscription or realm cancels pending departures without synthetic callbacks.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Observe_DisposalCancelsPendingNotifications(bool disposeRealm)
        {
            _source.Publish("one");
            _realm.Update();
            int departures = 0;
            IDisposable subscription = _realm.Query().Observe(ghost =>
            {
            }, key => departures++);
            _source.RemoveId("one");
            if (disposeRealm)
            {
                _realm.Dispose();
            }
            else
            {
                subscription.Dispose();
            }

            _realm.Update();
            Assert.That(departures, Is.Zero);
        }

        /// <summary>
        /// A departure queued by another departure callback waits for the next notification pass.
        /// </summary>
        [Test]
        public void Observe_QueuesDeparturesCreatedDuringCallback()
        {
            _source.Publish("first");
            _source.Publish("second");
            _realm.Update();
            List<string> departed = new List<string>();
            _realm.Query().Observe(ghost =>
            {
            }, key =>
            {
                departed.Add(key.EntityId);
                if (key.EntityId == "first")
                {
                    _source.RemoveId("second");
                }
            });

            _source.RemoveId("first");
            _realm.Update();
            Assert.That(departed, Is.EqualTo(new[] { "first" }));

            _realm.Update();
            Assert.That(departed, Is.EqualTo(new[] { "first", "second" }));
        }

        /// <summary>
        /// A departure can cancel its subscription before any recovered entries are delivered.
        /// </summary>
        [Test]
        public void Observe_CanDisposeDuringDeparture()
        {
            _source.Publish("one");
            _realm.Update();
            int entries = 0;
            IDisposable subscription = null;
            subscription = _realm.Query().Observe(ghost => entries++, key => subscription.Dispose());
            _anchor.RestartDetector(_source);
            _source.Publish("one");
            _realm.Update();
            Assert.That(entries, Is.EqualTo(1));
        }

        private sealed class Probe : PresenceDetector
        {
            internal TestGhost Publish(string id, Variant? variant = null, string name = null)
            {
                return GetOrCreate<TestGhost>(id, new Kind("observe"), variant, name);
            }

            internal void RemoveId(string id)
            {
                Disappear(new Kind("observe"), id);
            }
        }

        private sealed class TestGhost : Ghost
        {
        }
    }
}
