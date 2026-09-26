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

        /// <summary>
        /// Creates an isolated realm.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
        }

        /// <summary>
        /// Releases tracked scene objects.
        /// </summary>
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
            IGhost found;
            Assert.That(_realm.TryGetGhost(prepared.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(prepared));

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
        /// Query filters narrow a shared population by name, kind, anchor and root capability without altering the original query.
        /// </summary>
        [Test]
        public void Query_CombinesNameKindAndPartFilters()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource source = new TestSource(kind);
            TestSource otherKind = new TestSource(new Kind("vehicles.aircraft"));
            TestSource otherAnchor = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", source, otherKind);
            _realm.GetOrCreateAnchor("other", otherAnchor);
            TestGhost expected = source.PublishNamed("small", "Small Car", Variant.None);
            source.PublishNamed("large", "Large Car", Variant.None);
            source.PublishNamed("boat", "Boat", Variant.None);
            otherKind.PublishNamed("plane", "Small Car", Variant.None);
            otherAnchor.PublishNamed("remote", "Small Car", Variant.None);
            expected.gameObject.AddComponent<ExtraPart>();
            _realm.Update();

            Query named = _realm.Query("car");
            Assert.That(named.Count, Is.EqualTo(4));
            Query ofKind = named.OfKind(kind);
            Assert.That(ofKind.Count, Is.EqualTo(3));
            Query inAnchor = ofKind.InAnchor("simulation");
            Assert.That(inAnchor.Count, Is.EqualTo(2));
            Assert.That(inAnchor.WithExactName("SMALL CAR").Single(), Is.SameAs(expected));
            Assert.That(inAnchor.With<IExtraPart>().Single(), Is.SameAs(expected));
            Assert.That(inAnchor.With<IExtraPart>().WithExactName("SMALL CAR").Single(), Is.SameAs(expected));
            Assert.That(named.Count, Is.EqualTo(4));
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

            anchor.RemoveDetector(first);
            Assert.That(query.Count, Is.EqualTo(1));
            Assert.That(query.FirstOrDefault(), Is.SameAs(secondGhost));
            Assert.That(query.Single(), Is.SameAs(secondGhost));

            anchor.RemoveDetector(second);
            Assert.That(query.Count, Is.Zero);
            Assert.That(query.FirstOrDefault(), Is.Null);
            InvalidOperationException empty = Assert.Throws<InvalidOperationException>(() => query.Single());
            Assert.That(empty.Message, Is.EqualTo("Expected exactly one ghost, but found 0."));
        }

        /// <summary>
        /// A query description retains its filters when rebound to another realm without sharing its results.
        /// </summary>
        [Test]
        public void QueryDescription_CanBeReusedAcrossRealms()
        {
            Kind kind = new Kind("vehicles.car");
            TestSource originalSource = new TestSource(kind);
            _realm.GetOrCreateAnchor("simulation", originalSource);
            TestGhost original = originalSource.Publish("original", Variant.None);
            _realm.Update();
            Query description = _realm.Query().OfKind(kind).With<ITestPart>();
            using (Realm other = new Realm())
            {
                TestSource matching = new TestSource(kind);
                TestSource excluded = new TestSource(new Kind("vehicles.aircraft"));
                other.GetOrCreateAnchor("simulation", matching, excluded);
                TestGhost expected = matching.Publish("other", Variant.None);
                excluded.Publish("excluded", Variant.None);
                other.Update();
                Assert.That(other.Query(description).Single(), Is.SameAs(expected));
                Assert.That(description.Single(), Is.SameAs(original));
            }
        }

        /// <summary>
        /// A realm can manifest its own ghost but rejects a different realm's ghost with the same key.
        /// </summary>
        [Test]
        public void Manifest_DoesNotAcceptEqualKeyFromAnotherRealm()
        {
            Kind kind = new Kind("vehicles.car");
            GameObject prefab = new GameObject("Car view");
            prefab.SetActive(false);
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            Realm secondRealm = new Realm();
            try
            {
                blueprint.Configure(kind, null, null, prefab);
                _realm.RegisterManifestationBlueprint(blueprint);
                TestSource firstSource = new TestSource(kind);
                _realm.GetOrCreateAnchor("simulation", firstSource);
                TestGhost own = firstSource.Publish("42", Variant.None);
                _realm.Update();
                TestSource secondSource = new TestSource(kind);
                secondRealm.GetOrCreateAnchor("simulation", secondSource);
                TestGhost foreign = secondSource.Publish("42", Variant.None);
                secondRealm.Update();

                Assert.That(own.Key, Is.EqualTo(foreign.Key));
                Assert.That(_realm.Manifest(foreign), Is.Null);
                Assert.That(_realm.Manifest(own), Is.Not.Null);
                Assert.That(foreign.GetComponentInChildren<View>(), Is.Null);
            }
            finally
            {
                secondRealm.Dispose();
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(prefab);
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
            anchor.ReplaceDetector(first, replacement);
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
                localAnchor.AddDetector(localSource);
                globalAnchor.AddDetector(globalSource);
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
                anchor.AddDetector(source);
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
                localAnchor.AddDetector(localSource);
                globalAnchor.AddDetector(globalSource);
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

        private sealed class TestSource : PresenceDetector
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

            internal TestGhost Publish(string entityId, Variant variant)
            {
                return GetOrCreate<TestGhost>(entityId, Kind, variant);
            }

            internal TestGhost PublishNamed(string entityId, string name, Variant? variant)
            {
                return GetOrCreate<TestGhost>(entityId, Kind, variant, name);
            }
        }
    }
}
