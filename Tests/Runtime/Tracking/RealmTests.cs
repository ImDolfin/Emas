using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Exercises identity, automatic ghost creation and query behavior.</summary>
    public sealed class RealmTests
    {
        private Realm _realm;

        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
        }

        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>Different kinds may use the same source identifier.</summary>
        [Test]
        public void SameEntityIdAcrossKinds_CreatesTwoGhosts()
        {
            var first = new TestSource(new Kind("vehicles.car"));
            var second = new TestSource(new Kind("vehicles.aircraft"));
            _realm.CreateAnchorFor("simulation", first, second);

            first.Publish("42", new Variant("car"));
            second.Publish("42", new Variant("aircraft"));
            _realm.Update();

            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            Assert.That(_realm.Query().OfKind(first.Kind).Count, Is.EqualTo(1));
            Assert.That(_realm.Query().OfKind(second.Kind).Count, Is.EqualTo(1));
        }

        /// <summary>Prepared ghosts remain unavailable until the source initializes them.</summary>
        [Test]
        public void Prepare_IsUnavailableUntilSourceUsesIt()
        {
            var kind = new Kind("vehicles.car");
            _realm.CreateAnchorFor("simulation");
            var prepared = _realm.Prepare<TestGhost>("simulation", kind, "42", new Variant("small-car"));
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(0));

            var source = new TestSource(kind);
            _realm.CreateAnchorFor("simulation", source);
            var initialized = source.Publish("42", new Variant("car"));
            _realm.Update();

            Assert.That(initialized, Is.SameAs(prepared));
            Assert.That(initialized.IsAvailable, Is.True);
            Assert.That(_realm.Query().OfKind(kind).With<ITestPart>().Count, Is.EqualTo(1));
        }

        /// <summary>Subscriptions report current and later available matches once each.</summary>
        [Test]
        public void Subscription_ReportsCurrentAndLateGhost()
        {
            var kind = new Kind("vehicles.car");
            var source = new TestSource(kind);
            _realm.CreateAnchorFor("simulation", source);
            source.Publish("1", new Variant("one"));
            _realm.Update();

            var calls = 0;
            var subscription = _realm.Query().OfKind(kind).OnAvailable(ghost => calls++);
            Assert.That(calls, Is.EqualTo(1));

            source.Publish("2", new Variant("two"));
            _realm.Update();
            Assert.That(calls, Is.EqualTo(2));

            _realm.Update();
            Assert.That(calls, Is.EqualTo(2));
            subscription.Dispose();
        }

        /// <summary>Partial names and interfaces combine as filters.</summary>
        [Test]
        public void Query_CombinesNameKindAndPartFilters()
        {
            var kind = new Kind("vehicles.car");
            var source = new TestSource(kind);
            _realm.CreateAnchorFor("simulation", source);
            source.Publish("1", new Variant("Small Car"));
            _realm.Update();

            var result = _realm.Query("1")
                .OfKind(kind)
                .InAnchor("simulation")
                .With<ITestPart>()
                .WithExactName("1");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Single().Name, Is.EqualTo("1"));
        }

        /// <summary>Empty queries return safe empty results.</summary>
        [Test]
        public void EmptyQuery_ReturnsEmptyAndNullFirst()
        {
            Assert.That(_realm.Query("missing").Count, Is.EqualTo(0));
            Assert.That(_realm.Query("missing").FirstOrDefault(), Is.Null);
        }

        /// <summary>Single rejects a result that is not unique.</summary>
        [Test]
        public void Single_ThrowsWhenNoMatchExists()
        {
            Assert.Throws<InvalidOperationException>(() => _realm.Query().Single());
        }

        /// <summary>Default ghost kinds are rejected by filters and registration paths.</summary>
        [Test]
        public void InvalidKind_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => _realm.Query().OfKind(default(Kind)));
        }

        /// <summary>Reuses a query description against a different realm.</summary>
        [Test]
        public void QueryDescription_CanBeReusedAcrossRealms()
        {
            var kind = new Kind("vehicles.car");
            var description = _realm.Query().OfKind(kind).With<ITestPart>();
            using (var other = new Realm())
            {
                Assert.That(other.Query(description).Count, Is.EqualTo(0));
            }
        }

        /// <summary>Rejects a ghost object that belongs to another realm.</summary>
        [Test]
        public void Manifest_DoesNotAcceptEqualKeyFromAnotherRealm()
        {
            var kind = new Kind("vehicles.car");
            var firstSource = new TestSource(kind);
            var secondRealm = new Realm();
            try
            {
                _realm.CreateAnchorFor("simulation", firstSource);
                var firstGhost = firstSource.Publish("42", new Variant("small-car"));
                _realm.Update();

                var secondSource = new TestSource(kind);
                secondRealm.CreateAnchorFor("simulation", secondSource);
                secondSource.Publish("42", new Variant("small-car"));
                secondRealm.Update();

                Assert.That(_realm.Manifest(secondSource.LastPublished), Is.Null);
                Assert.That(firstGhost, Is.Not.Null);
            }
            finally
            {
                secondRealm.Dispose();
            }
        }

        /// <summary>Allows sources to expose source display names to partial-name queries.</summary>
        [Test]
        public void NamedPublication_IsAvailableToNameQueries()
        {
            var kind = new Kind("vehicles.car");
            var source = new TestSource(kind);
            _realm.CreateAnchorFor("simulation", source);
            source.PublishNamed("42", "Car 42", new Variant("small-car"));
            _realm.Update();

            Assert.That(_realm.Query("car").Count, Is.EqualTo(1));
            Assert.That(_realm.Query().WithExactName("CAR 42").Count, Is.EqualTo(1));
        }

        /// <summary>Notifies a subscription again after replacement and reinitialization.</summary>
        [Test]
        public void Subscription_ReportsReplacementRecovery()
        {
            var kind = new Kind("vehicles.car");
            var first = new TestSource(kind);
            var anchor = _realm.CreateAnchorFor("simulation", first);
            first.Publish("42", new Variant("small-car"));
            _realm.Update();

            var calls = 0;
            var subscription = _realm.Query().OfKind(kind).OnAvailable(ghost => calls++);
            var replacement = new TestSource(kind);
            anchor.ReplaceSource(first, replacement);
            replacement.Publish("42", new Variant("small-car"));
            _realm.Update();

            Assert.That(calls, Is.EqualTo(2));
            subscription.Dispose();
        }

        /// <summary>Removes prepared records when their anchor is removed.</summary>
        [Test]
        public void RemovingAnchor_RemovesPreparedIdentity()
        {
            var kind = new Kind("vehicles.car");
            _realm.CreateAnchorFor("simulation");
            var prepared = _realm.Prepare<TestGhost>("simulation", kind, "42");
            _realm.RemoveAnchor("simulation");

            var source = new TestSource(kind);
            _realm.CreateAnchorFor("simulation", source);
            var discovered = source.Publish("42", Variant.None);
            _realm.Update();

            Assert.That(discovered, Is.Not.SameAs(prepared));
            Assert.That(discovered.IsAvailable, Is.True);
        }

        /// <summary>Sets a named display value without changing the typed appearance.</summary>
        [Test]
        public void NamedPublication_CanRetainVariantWhenOmitted()
        {
            var kind = new Kind("vehicles.car");
            var source = new TestSource(kind);
            _realm.CreateAnchorFor("simulation", source);
            source.PublishNamed("42", "Car 42", new Variant("small-car"));
            source.PublishNamed("42", "Car 42 updated", null);
            _realm.Update();

            Assert.That(_realm.Query().WithVariant(new Variant("small-car")).Count, Is.EqualTo(1));
            Assert.That(_realm.Query().WithExactName("Car 42 updated").Count, Is.EqualTo(1));
        }
        /// <summary>Removes a requested view for None while retaining the available ghost.</summary>
        [Test]
        public void ManifestNone_RemovesViewButRetainsGhost()
        {
            var kind = new Kind("vehicles.car");
            var ghostTemplate = new GameObject("Ghost Template");
            var viewPrefab = new GameObject("Car View");
            var blueprint = ScriptableObject.CreateInstance<Blueprint>();
            try
            {
                ghostTemplate.SetActive(false);
                ghostTemplate.AddComponent<TestGhost>();
                viewPrefab.SetActive(false);
                blueprint.Configure(
                    kind,
                    ghostTemplate.GetComponent<TestGhost>(),
                    new[]
                    {
                        new Blueprint.ViewMapping(new Variant("small-car"), DetailLevel.Full, viewPrefab),
                        new Blueprint.ViewMapping(new Variant("small-car"), DetailLevel.Minimal, viewPrefab)
                    },
                    null);
                _realm.RegisterBlueprint(blueprint);

                var source = new TestSource(kind);
                _realm.CreateAnchorFor("simulation", source);
                var ghost = source.Publish("42", new Variant("small-car"));
                _realm.Update();

                var view = _realm.Manifest(ghost);
                Assert.That(view, Is.Not.Null);
                _realm.SetDegree(ghost, DetailLevel.Minimal);
                Assert.That(_realm.Manifest(ghost), Is.SameAs(view));
                Assert.That(view.Degree, Is.EqualTo(DetailLevel.Minimal));

                _realm.Manifest(ghost, DetailLevel.None);
                Assert.That(_realm.Query().OfKind(kind).Count, Is.EqualTo(1));
                Assert.That(_realm.Manifest(ghost, DetailLevel.None), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(ghostTemplate);
                UnityEngine.Object.DestroyImmediate(viewPrefab);
            }
        }

        /// <summary>Replaces a selected child view when a live ghost changes variant.</summary>
        [Test]
        public void VariantChange_ReplacesRequestedView()
        {
            var kind = new Kind("vehicles.car");
            var ghostTemplate = new GameObject("Ghost Template");
            var smallView = new GameObject("Small View");
            var largeView = new GameObject("Large View");
            var blueprint = ScriptableObject.CreateInstance<Blueprint>();
            try
            {
                ghostTemplate.SetActive(false);
                ghostTemplate.AddComponent<TestGhost>();
                smallView.SetActive(false);
                largeView.SetActive(false);
                blueprint.Configure(
                    kind,
                    ghostTemplate.GetComponent<TestGhost>(),
                    new[]
                    {
                        new Blueprint.ViewMapping(new Variant("small-car"), DetailLevel.Full, smallView),
                        new Blueprint.ViewMapping(new Variant("large-car"), DetailLevel.Full, largeView)
                    },
                    null);
                _realm.RegisterBlueprint(blueprint);

                var source = new TestSource(kind);
                _realm.CreateAnchorFor("simulation", source);
                var ghost = source.Publish("42", new Variant("small-car"));
                _realm.Update();
                _realm.Manifest(ghost);

                source.Publish("42", new Variant("large-car"));
                var replacement = _realm.Manifest(ghost);
                Assert.That(replacement, Is.Not.Null);
                Assert.That(replacement.gameObject.name, Is.EqualTo("Large View"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(ghostTemplate);
                UnityEngine.Object.DestroyImmediate(smallView);
                UnityEngine.Object.DestroyImmediate(largeView);
            }
        }

        /// <summary>Discards dispatched work from a source registration that was replaced.</summary>
        [Test]
        public void Dispatch_FromStoppedRegistrationIsDiscarded()
        {
            var source = new DispatchSource();
            var anchor = _realm.CreateAnchorFor("simulation", source);
            var calls = 0;
            source.QueueAction(() => calls++);
            anchor.ReplaceSource(source, new TestSource(new Kind("vehicles.car")));
            _realm.Update();

            Assert.That(calls, Is.EqualTo(0));
        }
        private interface ITestPart
        {
        }

        private sealed class TestGhost : Ghost, ITestPart
        {
        }

        private sealed class DispatchSource : PresenceSource
        {
            internal void QueueAction(Action action)
            {
                Dispatch(action);
            }
        }
        private sealed class TestSource : PresenceSource
        {
            internal TestSource(Kind kind)
            {
                Kind = kind;
            }

            internal Kind Kind { get; private set; }

            internal TestGhost LastPublished
            {
                get { return _lastPublished; }
            }

            internal TestGhost Publish(string entityId, Variant variant)
            {
                _lastPublished = GetOrCreate<TestGhost>(entityId, Kind, variant);
                return _lastPublished;
            }

            internal TestGhost PublishNamed(string entityId, string name, Variant? variant)
            {
                _lastPublished = GetOrCreate<TestGhost>(entityId, Kind, variant, name);
                return _lastPublished;
            }

            private TestGhost _lastPublished;
        }
    }
}
