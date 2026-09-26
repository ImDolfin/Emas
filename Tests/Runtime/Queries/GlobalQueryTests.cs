using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies live queries and subscriptions across every realm.
    /// </summary>
    public sealed class GlobalQueryTests
    {
        private static readonly Kind TestKind = new Kind("tests.global-query");
        private readonly List<Realm> _realms = new List<Realm>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        /// <summary>
        /// Releases every test realm and global subscription.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
            foreach (Realm realm in _realms)
            {
                realm.Dispose();
            }

            _realms.Clear();
        }

        /// <summary>
        /// Global queries include the default and isolated realms even when their entity keys collide.
        /// Rebinding selects one owner, and disposed realms stop contributing results.
        /// </summary>
        [Test]
        public void All_AggregatesRealmsAndRebindsToOneOwner()
        {
            Realm first = Own(Realm.Default);
            Realm second = Own(new Realm());
            ProbeSource firstSource = new ProbeSource();
            ProbeSource secondSource = new ProbeSource();
            first.GetOrCreateAnchor("shared", firstSource);
            second.GetOrCreateAnchor("shared", secondSource);
            TestGhost left = firstSource.Publish("same");
            TestGhost right = secondSource.Publish("same");
            first.Update();
            second.Update();

            Query all = Query.All().OfKind(TestKind);
            Assert.That(left.Key, Is.EqualTo(right.Key));
            Assert.That(all.ToArray(), Is.EquivalentTo(new IGhost[] { left, right }));
            Assert.That(first.Query(all).Single(), Is.SameAs(left));
            Assert.That(second.Query(all).Single(), Is.SameAs(right));

            first.Dispose();
            Assert.That(all.Single(), Is.SameAs(right));
            second.Dispose();
            Assert.That(all.Count, Is.Zero);
        }

        /// <summary>
        /// A global arrival subscription observes existing and future realms, then stops on disposal.
        /// </summary>
        [Test]
        public void OnAvailable_FollowsFutureRealmsAndCanBeDisposed()
        {
            List<IGhost> arrivals = new List<IGhost>();
            Realm first = Own(new Realm());
            ProbeSource firstSource = new ProbeSource();
            first.GetOrCreateAnchor("first", firstSource);
            TestGhost firstGhost = firstSource.Publish("one");
            first.Update();
            IDisposable subscription = Query.All().OfKind(TestKind).OnAvailable(arrivals.Add);
            _subscriptions.Add(subscription);
            Assert.That(arrivals, Is.EqualTo(new IGhost[] { firstGhost }));
            Realm second = Own(new Realm());
            ProbeSource secondSource = new ProbeSource();
            second.GetOrCreateAnchor("second", secondSource);
            TestGhost secondGhost = secondSource.Publish("two");
            second.Update();
            Assert.That(arrivals, Is.EqualTo(new IGhost[] { firstGhost, secondGhost }));

            subscription.Dispose();
            secondSource.Publish("three");
            second.Update();
            Assert.That(arrivals.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Duplicate keys remain independent in each realm, including on removal and realm disposal.
        /// </summary>
        [Test]
        public void ObserveWithRealm_SeparatesDuplicateKeysAndRealmDisposal()
        {
            List<string> events = new List<string>();
            Realm first = Own(new Realm());
            Realm second = Own(new Realm());
            IDisposable subscription = Query.All().OfKind(TestKind).ObserveWithRealm(
                (realm, ghost) => events.Add("enter " + (realm == first ? "first" : "second")),
                (realm, key) => events.Add("leave " + (realm == first ? "first" : "second")));
            _subscriptions.Add(subscription);
            ProbeSource firstSource = new ProbeSource();
            ProbeSource secondSource = new ProbeSource();
            first.GetOrCreateAnchor("same", firstSource);
            second.GetOrCreateAnchor("same", secondSource);
            firstSource.Publish("same");
            secondSource.Publish("same");
            first.Update();
            second.Update();
            Assert.That(Query.All().OfKind(TestKind).Count, Is.EqualTo(2));

            firstSource.RemoveId("same");
            first.Update();
            firstSource.Publish("same");
            first.Update();
            second.Dispose();
            Assert.That(events, Is.EqualTo(new[]
            {
                "enter first", "enter second", "leave first", "enter first", "leave second"
            }));

            subscription.Dispose();
            first.Dispose();
            Assert.That(events.Count, Is.EqualTo(5));
        }

        /// <summary>
        /// Key-only global observations deliver a departure when an owning realm is disposed.
        /// </summary>
        [Test]
        public void Observe_ReportsDeparturesAcrossRealmDisposal()
        {
            Realm realm = Own(new Realm());
            ProbeSource source = new ProbeSource();
            realm.GetOrCreateAnchor("observed", source);
            List<string> events = new List<string>();
            IDisposable subscription = Query.All().OfKind(TestKind).Observe(
                ghost => events.Add("enter " + ghost.Key.EntityId),
                key => events.Add("leave " + key.EntityId));
            _subscriptions.Add(subscription);
            source.Publish("one");
            realm.Update();
            realm.Dispose();
            Assert.That(events, Is.EqualTo(new[] { "enter one", "leave one" }));
        }

        /// <summary>
        /// An arrival callback may dispose its owner and still receive the paired departure.
        /// </summary>
        [Test]
        public void Callback_CanDisposeRealmWhileNotifying()
        {
            Realm realm = Own(new Realm());
            ProbeSource source = new ProbeSource();
            realm.GetOrCreateAnchor("callback", source);
            List<string> events = new List<string>();
            IDisposable subscription = Query.All().OfKind(TestKind).ObserveWithRealm(
                (owner, ghost) =>
                {
                    events.Add("enter");
                    owner.Dispose();
                },
                (owner, key) => events.Add("leave"));
            _subscriptions.Add(subscription);
            source.Publish("one");
            realm.Update();
            Assert.That(events, Is.EqualTo(new[] { "enter", "leave" }));
            Assert.That(Query.All().OfKind(TestKind).Count, Is.Zero);
        }

        private Realm Own(Realm realm)
        {
            _realms.Add(realm);
            return realm;
        }

        private sealed class ProbeSource : PresenceDetector
        {
            internal TestGhost Publish(string id)
            {
                return GetOrCreate<TestGhost>(id, TestKind);
            }

            internal void RemoveId(string id)
            {
                Disappear(TestKind, id);
            }
        }

        private sealed class TestGhost : Ghost
        {
        }
    }
}
