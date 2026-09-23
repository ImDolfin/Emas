using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises identity, automatic ghost creation and query behavior.
    /// </summary>
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

        /// <summary>
        /// Different kinds may use the same source identifier.
        /// </summary>
        [Test]
        public void SameEntityIdAcrossKinds_CreatesTwoGhosts()
        {
            TestSource first = new TestSource(new Kind("vehicles.car"));
            TestSource second = new TestSource(new Kind("vehicles.aircraft"));
            _realm.GetOrCreateAnchor("simulation", first, second);

            first.Publish("42", new Variant("car"));
            second.Publish("42", new Variant("aircraft"));
            _realm.Update();

            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            Assert.That(_realm.Query().OfKind(first.Kind).Count, Is.EqualTo(1));
            Assert.That(_realm.Query().OfKind(second.Kind).Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Prepared ghosts remain unavailable until the source initializes them.
        /// </summary>
        [Test]
        public void Prepare_IsUnavailableUntilSourceUsesIt()
        {
            Kind kind = new Kind("vehicles.car");
            _realm.GetOrCreateAnchor("simulation");
            TestGhost prepared = _realm.Prepare<TestGhost>("simulation", kind, "42", new Variant("small-car"));
            Assert.That(prepared.IsAvailable, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(0));

            TestSource source = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", source);
            TestGhost initialized = source.Publish("42", new Variant("car"));
            _realm.Update();

            Assert.That(initialized, Is.SameAs(prepared));
            Assert.That(initialized.IsAvailable, Is.True);
            Assert.That(_realm.Query().OfKind(kind).With<ITestPart>().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Subscriptions report current and later available matches once each.
        /// </summary>
        [Test]
        public void Subscription_ReportsCurrentAndLateGhost()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource source = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", source);
            source.Publish("1", new Variant("one"));
            _realm.Update();

            int calls = 0;
            IDisposable subscription = _realm.Query().OfKind(kind).OnAvailable(ghost => calls++);
            Assert.That(calls, Is.EqualTo(1));

            source.Publish("2", new Variant("two"));
            _realm.Update();
            Assert.That(calls, Is.EqualTo(2));

            _realm.Update();
            Assert.That(calls, Is.EqualTo(2));
            subscription.Dispose();
        }

        /// <summary>
        /// Partial names and interfaces combine as filters.
        /// </summary>
        [Test]
        public void Query_CombinesNameKindAndPartFilters()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource source = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", source);
            source.PublishNamed("1", "Small Car", new Variant("small-car"));
            _realm.Update();

            Assert.Throws<ArgumentException>(() => _realm.Query().OfKind(default(Kind)));
            Query result = _realm.Query("car")
                .OfKind(kind)
                .InAnchor("simulation")
                .With<ITestPart>()
                .WithExactName("SMALL CAR");

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Single().Name, Is.EqualTo("Small Car"));
        }

        /// <summary>
        /// Empty queries return safe empty results.
        /// </summary>
        [Test]
        public void EmptyQuery_ReturnsEmptyAndNullFirst()
        {
            Assert.That(_realm.Query("missing").Count, Is.EqualTo(0));
            Assert.That(_realm.Query("missing").FirstOrDefault(), Is.Null);
            Assert.Throws<InvalidOperationException>(() => _realm.Query("missing").Single());
        }

        /// <summary>
        /// Reuses a query description against a different realm.
        /// </summary>
        [Test]
        public void QueryDescription_CanBeReusedAcrossRealms()
        {
            Kind kind = new Kind("vehicles.car");
            Query description = _realm.Query().OfKind(kind).With<ITestPart>();
            using (Realm other = new Realm())
            {
                Assert.That(other.Query(description).Count, Is.EqualTo(0));
            }
        }

        /// <summary>
        /// Rejects a ghost object that belongs to another realm.
        /// </summary>
        [Test]
        public void Manifest_DoesNotAcceptEqualKeyFromAnotherRealm()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource firstSource = new TestSource(kind);
            Realm secondRealm = new Realm();
            try
            {
                _realm.GetOrCreateAnchor("simulation", firstSource);
                TestGhost firstGhost = firstSource.Publish("42", new Variant("small-car"));
                _realm.Update();

                TestSource secondSource = new TestSource(kind);
                secondRealm.GetOrCreateAnchor("simulation", secondSource);
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

        /// <summary>
        /// Notifies a subscription again after replacement and reinitialization.
        /// </summary>
        [Test]
        public void Subscription_ReportsReplacementRecovery()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource first = new TestSource(kind);
            Anchor anchor = _realm.GetOrCreateAnchor("simulation", first);
            first.Publish("42", new Variant("small-car"));
            _realm.Update();

            int calls = 0;
            IDisposable subscription = _realm.Query().OfKind(kind).OnAvailable(ghost => calls++);
            TestSource replacement = new TestSource(kind);
            anchor.ReplaceSource(first, replacement);
            replacement.Publish("42", new Variant("small-car"));
            _realm.Update();

            Assert.That(calls, Is.EqualTo(2));
            subscription.Dispose();
        }

        /// <summary>
        /// Sets a named display value without changing the typed appearance.
        /// </summary>
        [Test]
        public void NamedPublication_CanRetainVariantWhenOmitted()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource source = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", source);
            source.PublishNamed("42", "Car 42", new Variant("small-car"));
            source.PublishNamed("42", "Car 42 updated", null);
            _realm.Update();

            Assert.That(_realm.Query().WithVariant(new Variant("small-car")).Count, Is.EqualTo(1));
            Assert.That(_realm.Query().WithExactName("Car 42 updated").Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Registering a blueprint after publication refreshes a request, and replacing it updates the view without replacing the root.
        /// </summary>
        [Test]
        public void RegisterBlueprint_RefreshesLateAndReplacementViews()
        {
            Kind kind = new Kind("views.late");
            GameObject firstPrefab = new GameObject("first view");
            GameObject secondPrefab = new GameObject("second view");
            Blueprint first = ScriptableObject.CreateInstance<Blueprint>();
            Blueprint second = ScriptableObject.CreateInstance<Blueprint>();
            try
            {
                firstPrefab.SetActive(false);
                secondPrefab.SetActive(false);
                first.Configure(kind, null, null, firstPrefab);
                second.Configure(kind, null, null, secondPrefab);

                TestSource source = new TestSource(kind);
                _realm.GetOrCreateAnchor("simulation", source);
                TestGhost ghost = source.Publish("42", Variant.None);
                _realm.Update();
                Assert.That(_realm.Manifest(ghost), Is.Null);

                _realm.RegisterBlueprint(first);
                _realm.Update();
                View firstView = ghost.GetComponentInChildren<View>();
                Assert.That(firstView, Is.Not.Null);
                Assert.That(firstView.gameObject.name, Is.EqualTo("first view"));

                _realm.RegisterBlueprint(second);
                _realm.Update();
                View secondView = ghost.GetComponentInChildren<View>();
                Assert.That(secondView, Is.Not.Null);
                Assert.That(secondView, Is.Not.SameAs(firstView));
                Assert.That(secondView.gameObject.name, Is.EqualTo("second view"));
                Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(firstPrefab);
                UnityEngine.Object.DestroyImmediate(secondPrefab);
            }
        }

        /// <summary>
        /// An anchor blueprint overrides the realm default and can replace its own requested views.
        /// </summary>
        [Test]
        public void AnchorBlueprint_OverridesRealmDefault()
        {
            Kind kind = new Kind("views.scoped");
            GameObject globalPrefab = new GameObject("global view");
            GameObject localPrefab = new GameObject("local view");
            GameObject replacementPrefab = new GameObject("replacement view");
            GameObject globalRoot = new GameObject("global root");
            GameObject localRoot = new GameObject("local root");
            GameObject replacementRoot = new GameObject("replacement root");
            Blueprint global = ScriptableObject.CreateInstance<Blueprint>();
            Blueprint local = ScriptableObject.CreateInstance<Blueprint>();
            Blueprint replacement = ScriptableObject.CreateInstance<Blueprint>();
            try
            {
                globalPrefab.SetActive(false);
                localPrefab.SetActive(false);
                replacementPrefab.SetActive(false);
                globalRoot.SetActive(false);
                localRoot.SetActive(false);
                replacementRoot.SetActive(false);
                global.Configure(kind, globalRoot.AddComponent<TestGhost>(), null, globalPrefab);
                local.Configure(kind, localRoot.AddComponent<TestGhost>(), null, localPrefab);
                replacement.Configure(kind, replacementRoot.AddComponent<TestGhost>(), null, replacementPrefab);
                _realm.RegisterBlueprint(global);

                Anchor localAnchor = _realm.GetOrCreateAnchor("local");
                Anchor globalAnchor = _realm.GetOrCreateAnchor("global");
                Assert.Throws<ArgumentException>(() => localAnchor.RegisterBlueprint(null));
                localAnchor.RegisterBlueprint(local);
                TestSource localSource = new TestSource(kind);
                TestSource globalSource = new TestSource(kind);
                localAnchor.AddSource(localSource);
                globalAnchor.AddSource(globalSource);
                TestGhost localGhost = localSource.Publish("one", Variant.None);
                TestGhost globalGhost = globalSource.Publish("two", Variant.None);
                _realm.Update();

                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("local view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("global view"));
                Assert.That(localGhost.gameObject.name, Does.StartWith("local root"));
                Assert.That(globalGhost.gameObject.name, Does.StartWith("global root"));

                localAnchor.RegisterBlueprint(replacement);
                _realm.Update();
                Assert.That(localGhost.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("replacement view"));
                Assert.That(globalGhost.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("global view"));
                Assert.That(localGhost.gameObject.name, Does.StartWith("local root"));
                TestGhost later = localSource.Publish("three", Variant.None);
                _realm.Update();
                Assert.That(later.gameObject.name, Does.StartWith("replacement root"));
                Assert.That(_realm.Query().Count, Is.EqualTo(3));

                localAnchor.Dispose();
                Assert.Throws<ObjectDisposedException>(() => localAnchor.RegisterBlueprint(local));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(global);
                UnityEngine.Object.DestroyImmediate(local);
                UnityEngine.Object.DestroyImmediate(replacement);
                UnityEngine.Object.DestroyImmediate(globalPrefab);
                UnityEngine.Object.DestroyImmediate(localPrefab);
                UnityEngine.Object.DestroyImmediate(replacementPrefab);
                UnityEngine.Object.DestroyImmediate(globalRoot);
                UnityEngine.Object.DestroyImmediate(localRoot);
                UnityEngine.Object.DestroyImmediate(replacementRoot);
            }
        }

        /// <summary>
        /// Removes a requested view for None while retaining the available ghost.
        /// </summary>
        [Test]
        public void ManifestNone_RemovesViewButRetainsGhost()
        {
            Kind kind = new Kind("vehicles.car");
            GameObject ghostTemplate = new GameObject("Ghost Template");
            GameObject viewPrefab = new GameObject("Car View");
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
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

                TestSource source = new TestSource(kind);
                _realm.GetOrCreateAnchor("simulation", source);
                TestGhost ghost = source.Publish("42", new Variant("small-car"));
                _realm.Update();

                View view = _realm.Manifest(ghost);
                Assert.That(view, Is.Not.Null);
                _realm.SetDetailLevel(ghost, DetailLevel.Minimal);
                Assert.That(_realm.Manifest(ghost), Is.SameAs(view));
                Assert.That(view.RequestedDetailLevel, Is.EqualTo(DetailLevel.Minimal));

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

        /// <summary>
        /// A lower-detail prefab keeps the caller's requested level on its instantiated view.
        /// </summary>
        [Test]
        public void View_ReportsRequestedDetailLevelWhenUsingLowerDetailPrefab()
        {
            Kind kind = new Kind("vehicles.car");
            GameObject prefab = new GameObject("Minimal view");
            prefab.SetActive(false);
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            try
            {
                blueprint.Configure(kind, null, new[]
                {
                    new Blueprint.ViewMapping(Variant.None, DetailLevel.Minimal, prefab)
                }, null);
                _realm.RegisterBlueprint(blueprint);
                TestSource source = new TestSource(kind);
                _realm.GetOrCreateAnchor("simulation", source);
                TestGhost ghost = source.Publish("42", Variant.None);
                _realm.Update();
                View view = _realm.Manifest(ghost, DetailLevel.Full);
                Assert.That(view, Is.Not.Null);
                Assert.That(view.gameObject.name, Is.EqualTo("Minimal view"));
                Assert.That(view.RequestedDetailLevel, Is.EqualTo(DetailLevel.Full));
                _realm.SetDetailLevel(ghost, DetailLevel.Reduced);
                Assert.That(_realm.Manifest(ghost), Is.SameAs(view));
                Assert.That(view.RequestedDetailLevel, Is.EqualTo(DetailLevel.Reduced));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        private interface ITestPart
        {
        }

        private sealed class TestGhost : Ghost, ITestPart
        {
        }

        private sealed class TestSource : PresenceSource
        {
            internal TestSource(Kind kind)
            {
                Kind = kind;
            }

            internal Kind Kind
            {
                get;
                private set;
            }

            internal TestGhost LastPublished
            {
                get
                {
                    return _lastPublished;
                }
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
