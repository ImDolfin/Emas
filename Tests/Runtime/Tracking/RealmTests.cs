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
        /// All anchors share one Kind mapping; replacement refreshes views everywhere and changes only future roots.
        /// </summary>
        [Test]
        public void RealmBlueprint_AppliesToEveryAnchorAndSurvivesAnchorDisposal()
        {
            Kind kind = new Kind("views.shared");
            GameObject firstView = new GameObject("first view");
            GameObject nextView = new GameObject("next view");
            GameObject firstRoot = new GameObject("first root");
            GameObject nextRoot = new GameObject("next root");
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                firstView.SetActive(false);
                nextView.SetActive(false);
                firstRoot.SetActive(false);
                nextRoot.SetActive(false);
                blueprint.Configure(kind, firstRoot.AddComponent<TestGhost>(), null, firstView);
                _realm.RegisterManifestationBlueprint(blueprint);
                TestSource firstSource = new TestSource(kind);
                TestSource secondSource = new TestSource(kind);
                Anchor firstAnchor = _realm.GetOrCreateAnchor("first", firstSource);
                _realm.GetOrCreateAnchor("second", secondSource);
                TestGhost first = firstSource.Publish("one", Variant.None);
                TestGhost second = secondSource.Publish("one", Variant.None);
                _realm.Update();
                foreach (TestGhost ghost in new[] { first, second })
                {
                    Assert.That(ghost.gameObject.name, Does.StartWith("first root"));
                    Assert.That(_realm.Manifest(ghost).gameObject.name, Is.EqualTo("first view"));
                }

                blueprint.Configure(kind, nextRoot.AddComponent<TestGhost>(), null, nextView);
                _realm.RegisterManifestationBlueprint(blueprint);
                _realm.Update();
                foreach (TestGhost ghost in new[] { first, second })
                {
                    Assert.That(ghost.gameObject.name, Does.StartWith("first root"));
                    Assert.That(ghost.GetComponentInChildren<View>().gameObject.name, Is.EqualTo("next view"));
                    Assert.That(_realm.Query().InAnchor(ghost.Key.AnchorId).Single(), Is.SameAs(ghost));
                }

                firstAnchor.Dispose();
                TestSource laterSource = new TestSource(kind);
                _realm.GetOrCreateAnchor("later", laterSource);
                TestGhost later = laterSource.Publish("two", Variant.None);
                _realm.Update();
                Assert.That(later.gameObject.name, Does.StartWith("next root"));
                Assert.That(_realm.Manifest(later).gameObject.name, Is.EqualTo("next view"));
                Assert.That(_realm.Query().Count, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(firstView);
                UnityEngine.Object.DestroyImmediate(nextView);
                UnityEngine.Object.DestroyImmediate(firstRoot);
                UnityEngine.Object.DestroyImmediate(nextRoot);
            }
        }

        /// <summary>
        /// Shared assets keep each realm's previous settings until that realm registers again.
        /// </summary>
        [Test]
        public void ManifestationBlueprint_SharedAssetUsesIndependentRegistrationSnapshots()
        {
            Kind kind = new Kind("views.shared.snapshot");
            GameObject oldPrefab = new GameObject("old view");
            GameObject newPrefab = new GameObject("new view");
            ManifestationBlueprint shared = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            Realm otherRealm = new Realm();
            try
            {
                oldPrefab.SetActive(false);
                newPrefab.SetActive(false);
                shared.Configure(kind, null, new[] { new ManifestationVariant("car", oldPrefab) }, null);
                _realm.RegisterManifestationBlueprint(shared);
                Anchor localAnchor = otherRealm.GetOrCreateAnchor("simulation");
                Anchor globalAnchor = _realm.GetOrCreateAnchor("simulation");
                otherRealm.RegisterManifestationBlueprint(shared);
                TestSource localSource = new TestSource(kind);
                TestSource globalSource = new TestSource(kind);
                localAnchor.AddDetector(localSource);
                globalAnchor.AddDetector(globalSource);
                TestGhost localGhost = localSource.Publish("one", new Variant("car"));
                TestGhost globalGhost = globalSource.Publish("one", new Variant("car"));
                _realm.Update();
                otherRealm.Update();
                Assert.That(otherRealm.Manifest(localGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                shared.Configure(kind, null, new[] { new ManifestationVariant("car", newPrefab) }, null);
                Assert.That(otherRealm.Manifest(localGhost).gameObject.name, Is.EqualTo("old view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                otherRealm.RegisterManifestationBlueprint(shared);
                _realm.Update();
                otherRealm.Update();
                Assert.That(otherRealm.Manifest(localGhost).gameObject.name, Is.EqualTo("new view"));
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("old view"));

                _realm.RegisterManifestationBlueprint(shared);
                _realm.Update();
                otherRealm.Update();
                Assert.That(_realm.Manifest(globalGhost).gameObject.name, Is.EqualTo("new view"));
            }
            finally
            {
                otherRealm.Dispose();
                UnityEngine.Object.DestroyImmediate(shared);
                UnityEngine.Object.DestroyImmediate(oldPrefab);
                UnityEngine.Object.DestroyImmediate(newPrefab);
            }
        }

        /// <summary>
        /// Named LOD variants switch a requested view without replacing the root; demanifesting cancels automatic refresh.
        /// </summary>
        [Test]
        public void VariantChanges_SwitchViewsAndRespectDemanifest()
        {
            Kind kind = new Kind("vehicles.car");
            GameObject fullPrefab = new GameObject("small car");
            GameObject lowPrefab = new GameObject("small car low");
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            try
            {
                fullPrefab.SetActive(false);
                lowPrefab.SetActive(false);
                Variant full = new Variant("small_car");
                Variant low = new Variant("small_car_low");
                blueprint.Configure(kind, null, new[]
                {
                    new ManifestationVariant(full.Id, fullPrefab),
                    new ManifestationVariant(low.Id, lowPrefab)
                }, null);
                _realm.RegisterManifestationBlueprint(blueprint);
                TestSource source = new TestSource(kind);
                _realm.GetOrCreateAnchor("simulation", source);
                TestGhost ghost = source.Publish("42", full);
                _realm.Update();

                View view = _realm.Manifest(ghost);
                Assert.That(view.gameObject.name, Is.EqualTo("small car"));
                Assert.That(_realm.Manifest(ghost), Is.SameAs(view));

                Assert.That(source.Publish("42", low), Is.SameAs(ghost));
                _realm.Update();
                View lowView = ghost.GetComponentInChildren<View>();
                Assert.That(lowView.gameObject.name, Is.EqualTo("small car low"));
                Assert.That(lowView.Ghost, Is.SameAs(ghost));
                Assert.That(lowView, Is.Not.SameAs(view));

                _realm.Demanifest(ghost);
                source.Publish("42", full);
                _realm.Update();
                Assert.That(ghost.GetComponentInChildren<View>(), Is.Null);
                Assert.That(_realm.Query().OfKind(kind).Single(), Is.SameAs(ghost));
                Assert.That(_realm.Manifest(ghost).gameObject.name, Is.EqualTo("small car"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blueprint);
                UnityEngine.Object.DestroyImmediate(fullPrefab);
                UnityEngine.Object.DestroyImmediate(lowPrefab);
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
