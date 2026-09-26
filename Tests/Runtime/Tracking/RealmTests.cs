using System;
using System.Collections.Generic;
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
            Assert.That(initialized.IsAvailable, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
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
        /// Root contract lookup and typed queries reflect components added and removed after publication.
        /// </summary>
        [Test]
        public void TryGet_ReflectsRootComponentChanges()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource source = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", source);
            TestGhost ghost = source.Publish("42", Variant.None);
            _realm.Update();
            Query query = _realm.Query().With<IExtraPart>();
            IExtraPart part;

            Assert.That(ghost.TryGet<IExtraPart>(out part), Is.False);
            Assert.That(part, Is.Null);
            Assert.That(query.Count, Is.Zero);

            ExtraPart component = ghost.gameObject.AddComponent<ExtraPart>();
            Assert.That(ghost.TryGet<IExtraPart>(out part), Is.True);
            Assert.That(part, Is.SameAs(component));
            Assert.That(query.Count, Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(component);
            Assert.That(ghost.TryGet<IExtraPart>(out part), Is.False);
            Assert.That(part, Is.Null);
            Assert.That(query.Count, Is.Zero);
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
        /// Scalar query results follow changing membership and report the full match count.
        /// </summary>
        [Test]
        public void Query_ScalarResultsFollowMembership()
        {
            TestSource first = new TestSource(new Kind("vehicles.car"));
            TestSource second = new TestSource(new Kind("vehicles.aircraft"));
            Anchor anchor = _realm.GetOrCreateAnchor("simulation", first, second);
            TestGhost firstGhost = first.Publish("one", Variant.None);
            TestGhost secondGhost = second.Publish("two", Variant.None);
            _realm.Update();
            Query query = _realm.Query();

            Assert.That(query.Count, Is.EqualTo(2));
            IGhost firstMatch = query.FirstOrDefault();
            Assert.That(ReferenceEquals(firstMatch, firstGhost) || ReferenceEquals(firstMatch, secondGhost), Is.True);
            InvalidOperationException multiple = Assert.Throws<InvalidOperationException>(() => query.Single());
            Assert.That(multiple.Message, Is.EqualTo("Expected exactly one ghost, but found 2."));

            anchor.RemoveSource(first);
            Assert.That(query.Count, Is.EqualTo(1));
            Assert.That(query.FirstOrDefault(), Is.SameAs(secondGhost));
            Assert.That(query.Single(), Is.SameAs(secondGhost));

            anchor.RemoveSource(second);
            Assert.That(query.Count, Is.Zero);
            Assert.That(query.FirstOrDefault(), Is.Null);
            InvalidOperationException empty = Assert.Throws<InvalidOperationException>(() => query.Single());
            Assert.That(empty.Message, Is.EqualTo("Expected exactly one ghost, but found 0."));
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
        public void RegisterManifestationBlueprint_RefreshesLateAndReplacementViews()
        {
            Kind kind = new Kind("views.late");
            GameObject firstPrefab = new GameObject("first view");
            GameObject secondPrefab = new GameObject("second view");
            ManifestationBlueprint first = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationBlueprint second = ScriptableObject.CreateInstance<ManifestationBlueprint>();
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

                _realm.RegisterManifestationBlueprint(first);
                _realm.Update();
                View firstView = ghost.GetComponentInChildren<View>();
                Assert.That(firstView, Is.Not.Null);
                Assert.That(firstView.gameObject.name, Is.EqualTo("first view"));

                _realm.RegisterManifestationBlueprint(second);
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
        public void AnchorManifestationBlueprint_OverridesRealmDefault()
        {
            Kind kind = new Kind("views.scoped");
            GameObject globalPrefab = new GameObject("global view");
            GameObject localPrefab = new GameObject("local view");
            GameObject replacementPrefab = new GameObject("replacement view");
            GameObject globalRoot = new GameObject("global root");
            GameObject localRoot = new GameObject("local root");
            GameObject replacementRoot = new GameObject("replacement root");
            ManifestationBlueprint global = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationBlueprint local = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationBlueprint replacement = ScriptableObject.CreateInstance<ManifestationBlueprint>();
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
                _realm.RegisterManifestationBlueprint(global);

                Anchor localAnchor = _realm.GetOrCreateAnchor("local");
                Anchor globalAnchor = _realm.GetOrCreateAnchor("global");
                Assert.Throws<ArgumentException>(() => localAnchor.RegisterManifestationBlueprint(null));
                localAnchor.RegisterManifestationBlueprint(local);
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

                localAnchor.RegisterManifestationBlueprint(replacement);
                _realm.Update();
                Assert.That(localGhost.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("replacement view"));
                Assert.That(globalGhost.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("global view"));
                Assert.That(localGhost.gameObject.name, Does.StartWith("local root"));
                TestGhost later = localSource.Publish("three", Variant.None);
                _realm.Update();
                Assert.That(later.gameObject.name, Does.StartWith("replacement root"));
                Assert.That(_realm.Query().Count, Is.EqualTo(3));

                localAnchor.Dispose();
                Assert.Throws<ObjectDisposedException>(() => localAnchor.RegisterManifestationBlueprint(local));
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
        /// Removing an anchor override restores the realm default without replacing the ghost root.
        /// </summary>
        [Test]
        public void AnchorManifestationBlueprint_UnregisterRestoresRealmDefault()
        {
            Kind kind = new Kind("views.unregister");
            GameObject defaultPrefab = new GameObject("default view");
            GameObject localPrefab = new GameObject("local view");
            ManifestationBlueprint defaultManifestationBlueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationBlueprint localManifestationBlueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                defaultPrefab.SetActive(false);
                localPrefab.SetActive(false);
                defaultManifestationBlueprint.Configure(kind, null, null, defaultPrefab);
                localManifestationBlueprint.Configure(kind, null, null, localPrefab);
                _realm.RegisterManifestationBlueprint(defaultManifestationBlueprint);
                Anchor anchor = _realm.GetOrCreateAnchor("simulation");
                anchor.RegisterManifestationBlueprint(localManifestationBlueprint);
                TestSource source = new TestSource(kind);
                anchor.AddSource(source);
                TestGhost ghost = source.Publish("42", Variant.None);
                _realm.Update();
                View localView = _realm.Manifest(ghost);
                Assert.That(localView.gameObject.name, Is.EqualTo("local view"));

                anchor.UnregisterManifestationBlueprint(kind);
                _realm.Update();
                View defaultView = _realm.Manifest(ghost);
                Assert.That(defaultView.gameObject.name, Is.EqualTo("default view"));
                Assert.That(defaultView, Is.Not.SameAs(localView));
                Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
                Assert.DoesNotThrow(() => anchor.UnregisterManifestationBlueprint(kind));
                Assert.Throws<ArgumentException>(() => anchor.UnregisterManifestationBlueprint(default(Kind)));
                anchor.Dispose();
                Assert.Throws<ObjectDisposedException>(() => anchor.UnregisterManifestationBlueprint(kind));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaultManifestationBlueprint);
                UnityEngine.Object.DestroyImmediate(localManifestationBlueprint);
                UnityEngine.Object.DestroyImmediate(defaultPrefab);
                UnityEngine.Object.DestroyImmediate(localPrefab);
            }
        }

        /// <summary>
        /// Shared assets keep each scope's previous settings until that scope registers again.
        /// </summary>
        [Test]
        public void ManifestationBlueprint_SharedAssetUsesIndependentRegistrationSnapshots()
        {
            Kind kind = new Kind("views.shared.snapshot");
            GameObject oldPrefab = new GameObject("old view");
            GameObject newPrefab = new GameObject("new view");
            ManifestationBlueprint shared = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                oldPrefab.SetActive(false);
                newPrefab.SetActive(false);
                shared.Configure(kind, null, null, oldPrefab);
                _realm.RegisterManifestationBlueprint(shared);
                Anchor localAnchor = _realm.GetOrCreateAnchor("local");
                Anchor globalAnchor = _realm.GetOrCreateAnchor("global");
                localAnchor.RegisterManifestationBlueprint(shared);
                TestSource localSource = new TestSource(kind);
                TestSource globalSource = new TestSource(kind);
                localAnchor.AddSource(localSource);
                globalAnchor.AddSource(globalSource);
                TestGhost localGhost = localSource.Publish("one", Variant.None);
                TestGhost globalGhost = globalSource.Publish("two", Variant.None);
                _realm.Update();
                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                shared.Configure(kind, null, null, newPrefab);
                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                localAnchor.RegisterManifestationBlueprint(shared);
                _realm.Update();
                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("new view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                _realm.RegisterManifestationBlueprint(shared);
                _realm.Update();
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("new view"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shared);
                UnityEngine.Object.DestroyImmediate(oldPrefab);
                UnityEngine.Object.DestroyImmediate(newPrefab);
            }
        }

        /// <summary>
        /// Re-registering an asset under a new kind releases its old mapping and refreshes both kinds.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ManifestationBlueprint_ReconfiguredKindRebindsOldAndNewKinds(bool anchorScoped)
        {
            Kind oldKind = new Kind("views.old");
            Kind newKind = new Kind("views.new");
            GameObject oldPrefab = new GameObject("old view");
            GameObject fallbackPrefab = new GameObject("realm fallback");
            GameObject newPrefab = new GameObject("new view");
            ManifestationBlueprint changing = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationBlueprint fallback = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                oldPrefab.SetActive(false);
                fallbackPrefab.SetActive(false);
                newPrefab.SetActive(false);
                changing.Configure(oldKind, null, null, oldPrefab);
                fallback.Configure(oldKind, null, null, fallbackPrefab);
                if (anchorScoped)
                {
                    _realm.RegisterManifestationBlueprint(fallback);
                }

                Anchor anchor = _realm.GetOrCreateAnchor("simulation");
                if (anchorScoped)
                {
                    anchor.RegisterManifestationBlueprint(changing);
                }
                else
                {
                    _realm.RegisterManifestationBlueprint(changing);
                }

                TestSource oldSource = new TestSource(oldKind);
                TestSource newSource = new TestSource(newKind);
                anchor.AddSource(oldSource);
                anchor.AddSource(newSource);
                TestGhost oldGhost = oldSource.Publish("old", Variant.None);
                TestGhost newGhost = newSource.Publish("new", Variant.None);
                _realm.Update();
                Assert.That(_realm.Manifest(oldGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(newGhost), Is.Null);

                changing.Configure(newKind, null, null, newPrefab);
                Assert.That(_realm.Manifest(oldGhost).gameObject.name, Is.EqualTo("old view"));
                if (anchorScoped)
                {
                    anchor.RegisterManifestationBlueprint(changing);
                }
                else
                {
                    _realm.RegisterManifestationBlueprint(changing);
                }

                _realm.Update();
                View oldView = _realm.Manifest(oldGhost);
                if (anchorScoped)
                {
                    Assert.That(oldView.gameObject.name, Is.EqualTo("realm fallback"));
                }
                else
                {
                    Assert.That(oldView, Is.Null);
                }

                Assert.That(_realm.Manifest(newGhost).gameObject.name, Is.EqualTo("new view"));
                Assert.That(_realm.Query().Count, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(changing);
                UnityEngine.Object.DestroyImmediate(fallback);
                UnityEngine.Object.DestroyImmediate(oldPrefab);
                UnityEngine.Object.DestroyImmediate(fallbackPrefab);
                UnityEngine.Object.DestroyImmediate(newPrefab);
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
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant manifestationVariant = ScriptableObject.CreateInstance<ManifestationVariant>();
            try
            {
                ghostTemplate.SetActive(false);
                ghostTemplate.AddComponent<TestGhost>();
                viewPrefab.SetActive(false);
                manifestationVariant.Configure(new Variant("small-car"), new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, viewPrefab),
                    new ManifestationVariant.DetailMapping(DetailLevel.Minimal, viewPrefab)
                });
                blueprint.Configure(kind, ghostTemplate.GetComponent<TestGhost>(),
                    new[] { manifestationVariant }, null);
                _realm.RegisterManifestationBlueprint(blueprint);

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
                UnityEngine.Object.DestroyImmediate(manifestationVariant);
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
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            ManifestationVariant manifestationVariant = ScriptableObject.CreateInstance<ManifestationVariant>();
            try
            {
                manifestationVariant.Configure(Variant.None, new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Minimal, prefab)
                });
                blueprint.Configure(kind, null, new[] { manifestationVariant }, null);
                _realm.RegisterManifestationBlueprint(blueprint);
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
                UnityEngine.Object.DestroyImmediate(manifestationVariant);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// Unassigned kinds and an intentionally empty blueprint keep ordinary ghost roots without warnings.
        /// </summary>
        [Test]
        public void SilentDefaults_CreateGhostsWithoutViewsOrMissingPrefabWarnings()
        {
            Kind unassignedKind = new Kind("silent.unassigned");
            Kind configuredKind = new Kind("silent.configured");
            ManifestationBlueprint empty = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            List<string> warnings = new List<string>();
            Application.LogCallback onLog = (message, stackTrace, type) =>
            {
                if (type == LogType.Warning && message.Contains("No Emas view prefab"))
                {
                    warnings.Add(message);
                }
            };

            try
            {
                empty.Configure(configuredKind, null, null, null);
                _realm.RegisterManifestationBlueprint(empty);
                TestSource unassignedSource = new TestSource(unassignedKind);
                TestSource configuredSource = new TestSource(configuredKind);
                _realm.GetOrCreateAnchor("simulation", unassignedSource, configuredSource);
                Application.logMessageReceived += onLog;

                TestGhost unassigned = unassignedSource.Publish("one", Variant.None);
                TestGhost configured = configuredSource.Publish("two", Variant.None);
                _realm.Update();
                Assert.That(_realm.Manifest(unassigned), Is.Null);
                Assert.That(_realm.Manifest(configured), Is.Null);

                Assert.That(unassigned.IsAvailable && configured.IsAvailable, Is.True);
                Assert.That(unassigned.gameObject.activeInHierarchy && configured.gameObject.activeInHierarchy,
                    Is.True);
                Assert.That(unassigned.GetComponentInChildren<View>(true), Is.Null);
                Assert.That(configured.GetComponentInChildren<View>(true), Is.Null);
                Assert.That(_realm.Query().Count, Is.EqualTo(2));
                Assert.That(warnings, Is.Empty);
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                UnityEngine.Object.DestroyImmediate(empty);
            }
        }

        /// <summary>
        /// Editing a referenced variant asset does not change registered scopes until each registers again.
        /// </summary>
        [Test]
        public void ManifestationVariant_EditRequiresReregistrationForEachScope()
        {
            Kind kind = new Kind("views.variant.snapshot");
            Variant appearance = new Variant("small");
            GameObject oldPrefab = new GameObject("old view");
            GameObject newPrefab = new GameObject("new view");
            ManifestationVariant variant = ScriptableObject.CreateInstance<ManifestationVariant>();
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                oldPrefab.SetActive(false);
                newPrefab.SetActive(false);
                variant.Configure(appearance, new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, oldPrefab)
                });
                blueprint.Configure(kind, null, new[] { variant }, null);
                _realm.RegisterManifestationBlueprint(blueprint);
                Anchor localAnchor = _realm.GetOrCreateAnchor("local");
                Anchor globalAnchor = _realm.GetOrCreateAnchor("global");
                localAnchor.RegisterManifestationBlueprint(blueprint);
                TestSource localSource = new TestSource(kind);
                TestSource globalSource = new TestSource(kind);
                localAnchor.AddSource(localSource);
                globalAnchor.AddSource(globalSource);
                TestGhost localGhost = localSource.Publish("one", appearance);
                TestGhost globalGhost = globalSource.Publish("two", appearance);
                _realm.Update();
                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                variant.Configure(appearance, new[]
                {
                    new ManifestationVariant.DetailMapping(DetailLevel.Full, newPrefab)
                });
                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                localAnchor.RegisterManifestationBlueprint(blueprint);
                _realm.Update();
                Assert.That(_realm.Manifest(localGhost).gameObject.name, Is.EqualTo("new view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                _realm.RegisterManifestationBlueprint(blueprint);
                _realm.Update();
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("new view"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(oldPrefab);
                UnityEngine.Object.DestroyImmediate(newPrefab);
            }
        }

        private interface ITestPart
        {
        }

        private interface IExtraPart
        {
        }

        private sealed class TestGhost : Ghost, ITestPart
        {
        }

        private sealed class ExtraPart : MonoBehaviour, IExtraPart
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
