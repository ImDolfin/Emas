using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises prefab realm ownership and the public provider/configurator lifecycle.
    /// </summary>
    public sealed class RealmSetupTests
    {
        private sealed class TestProvider : MonoBehaviour, IDetectorProvider
        {
            internal Func<PresenceDetector> Factory;

            /// <summary>
            /// Creates the detector selected by the application scenario.
            /// </summary>
            /// <returns>The detector created by the configured test factory.</returns>
            public PresenceDetector CreateDetector()
            {
                return Factory();
            }
        }

        private sealed class TestConfigurator : MonoBehaviour, IRealmConfigurator
        {
            internal int Calls;

            /// <summary>
            /// Registers a typed root and trait before the detector publishes data.
            /// </summary>
            /// <param name="realm">The Realm being configured before its detectors start.</param>
            public void ConfigureRealm(Realm realm)
            {
                Calls++;
                realm.RegisterPresenceInitializer<TextGhost>(TestKind,
                    (presence, root) => root.GetComponent<TextTrait>().Bind(() => presence.Key.EntityId));
            }
        }

        private sealed class PublishingDetector : PresenceDetector
        {
            /// <summary>Publishes the typed root used to verify authored Realm configuration.</summary>
            protected override void OnStart()
            {
                GetOrCreate<TextGhost>("one", TestKind);
            }
        }

        private sealed class ReadingDetector : PresenceDetector
        {
            /// <summary>Detects the entity whose source reading is mapped by the configured initializer.</summary>
            protected override void OnStart()
            {
                Detect("one", TestKind);
            }
        }

        private sealed class CurrentEntityDetector : PresenceDetector
        {
            private readonly Func<string> _readId;
            private string _previousId;

            internal CurrentEntityDetector(Func<string> readId)
            {
                _readId = readId;
            }

            /// <summary>Publishes the currently selected source entity during startup.</summary>
            protected override void OnStart()
            {
                OnUpdate();
            }

            /// <summary>Retires a changed source identity and detects the current entity.</summary>
            protected override void OnUpdate()
            {
                string id = _readId();
                if (_previousId != null && _previousId != id)
                {
                    Disappear(TestKind, _previousId);
                }

                Detect(id, TestKind);
                _previousId = id;
            }
        }

        private sealed class ContextInitializer : GhostInitializer
        {
            /// <summary>Accepts initialization without bindings so the scenario can inspect the initializer context.</summary>
            /// <param name="presence">The detected presence being initialized.</param>
            /// <param name="ghost">The root created for the detected presence.</param>
            protected override void Initialize(Presence presence, Ghost ghost)
            {
            }
        }

        private sealed class FailingDetector : PresenceDetector
        {
            /// <summary>Raises a startup failure to verify authored setup cleanup and error reporting.</summary>
            protected override void OnStart()
            {
                throw new InvalidOperationException("source startup failed");
            }
        }

        private static readonly Kind TestKind = new Kind("setup.entity");
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        /// <summary>
        /// Releases the scene objects and their owned realms after each scenario.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object value in _objects)
            {
                if (value != null)
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }

            _objects.Clear();
        }

        /// <summary>
        /// Nested prefab realms may reuse anchor IDs; stopping the parent realm leaves the child population usable.
        /// </summary>
        [Test]
        public void NestedRealms_OwnIndependentPopulations()
        {
            // Each setup owns its own tracked objects, even when the setups are nested in the scene.
            RealmSetup environment = CreateRealm("environment");
            RealmSetup screen = CreateRealm("screen");
            screen.transform.SetParent(environment.transform, false);
            CreateAnchor(environment, () => new PublishingDetector());
            CreateAnchor(screen, () => new PublishingDetector());
            environment.gameObject.SetActive(true);
            screen.gameObject.SetActive(true);
            environment.StartRealm();
            screen.StartRealm();

            Assert.That(environment.Realm, Is.Not.SameAs(screen.Realm));
            Assert.That(environment.Realm, Is.Not.SameAs(Realm.Default));
            Assert.That(environment.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(screen.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(Query.All().OfKind(TestKind).Count, Is.EqualTo(2));

            // Stopping the outer Realm must leave the inner Realm and its query results working.
            environment.StopRealm();
            Assert.That(environment.Realm, Is.Null);
            Assert.That(screen.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(Query.All().OfKind(TestKind).Count, Is.EqualTo(1));
        }

        /// <summary>
        /// An anchor enabled on demand configures its root and data trait before detection, once per realm lifetime.
        /// </summary>
        [Test]
        public void EnabledAnchor_ConfiguresPresenceOncePerRealmLifetime()
        {
            RealmSetup setup = CreateRealm("on-demand source");
            TestProvider provider = CreateAnchor(setup, () => new ReadingDetector());
            TestConfigurator configurator = provider.gameObject.AddComponent<TestConfigurator>();
            provider.gameObject.SetActive(false);
            setup.gameObject.SetActive(true);
            setup.StartRealm();

            Assert.That(configurator.Calls, Is.Zero);
            // An inactive source joins the running Realm when its GameObject is enabled.
            provider.gameObject.SetActive(true);
            AssertConfiguredPresence(setup.Realm);
            Assert.That(configurator.Calls, Is.EqualTo(1));

            provider.gameObject.SetActive(false);
            Assert.That(setup.Realm.Query().Count, Is.Zero);
            provider.gameObject.SetActive(true);
            AssertConfiguredPresence(setup.Realm);
            Assert.That(configurator.Calls, Is.EqualTo(1));

            // Starting a new Realm runs setup again. Toggling a source in the same Realm does not.
            setup.StopRealm();
            setup.StartRealm();
            AssertConfiguredPresence(setup.Realm);
            Assert.That(configurator.Calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Moving a live anchor preserves detector and initializer context until disabled; re-enabling binds both to the new realm.
        /// </summary>
        [Test]
        public void ReparentedAnchor_ReenableTransfersOwnership()
        {
            RealmSetup first = CreateRealm("original realm");
            RealmSetup second = CreateRealm("next realm");
            TestProvider provider = CreateAnchor(first, () => new PublishingDetector());
            AnchorSetup anchorSetup = provider.GetComponent<AnchorSetup>();
            ContextInitializer initializer = provider.gameObject.AddComponent<ContextInitializer>();
            Assert.That(initializer.Anchor, Is.Null);
            Assert.That(initializer.Realm, Is.Null);
            first.gameObject.SetActive(true);
            first.StartRealm();
            Anchor original = anchorSetup.Anchor;
            PresenceDetector originalDetector = original.Detectors[0];
            Assert.That(initializer.Anchor, Is.SameAs(original));
            Assert.That(initializer.Realm, Is.SameAs(first.Realm));
            second.gameObject.SetActive(true);
            // Changing the scene parent must not move a running detector or its initializer to another Realm.
            provider.transform.SetParent(second.transform, false);
            second.StartRealm();

            Assert.That(anchorSetup.Anchor, Is.SameAs(original));
            Assert.That(initializer.Anchor, Is.SameAs(original));
            Assert.That(initializer.Realm, Is.SameAs(first.Realm));
            Assert.That(originalDetector.Anchor, Is.SameAs(original));
            Assert.That(originalDetector.Realm, Is.SameAs(first.Realm));
            Assert.That(first.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(second.Realm.Query().Count, Is.Zero);

            // Disable to leave the old Realm. Enable again to join the Realm at the new scene location.
            anchorSetup.enabled = false;
            Assert.That(anchorSetup.Anchor, Is.Null);
            Assert.That(initializer.Anchor, Is.Null);
            Assert.That(initializer.Realm, Is.Null);
            Assert.That(originalDetector.Anchor, Is.Null);
            Assert.That(originalDetector.Realm, Is.Null);
            Assert.That(first.Realm.Anchors, Is.Empty);
            Assert.That(first.Realm.Query().Count, Is.Zero);
            anchorSetup.enabled = true;
            Assert.That(anchorSetup.Anchor.Realm, Is.SameAs(second.Realm));
            Assert.That(initializer.Anchor, Is.SameAs(anchorSetup.Anchor));
            Assert.That(initializer.Realm, Is.SameAs(second.Realm));
            Assert.That(anchorSetup.Anchor.Detectors[0].Anchor, Is.SameAs(anchorSetup.Anchor));
            Assert.That(anchorSetup.Anchor.Detectors[0].Realm, Is.SameAs(second.Realm));
            first.StopRealm();
            Assert.That(second.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(initializer.Realm, Is.SameAs(second.Realm));
            second.StopRealm();
            Assert.That(initializer.Anchor, Is.Null);
            Assert.That(initializer.Realm, Is.Null);
        }

        /// <summary>
        /// A failed provider startup releases its partial realm so applications can correct the source and retry.
        /// </summary>
        [Test]
        public void StartupFailure_ReleasesRealmAndAllowsRetry()
        {
            RealmSetup setup = CreateRealm("retry");
            bool fail = true;
            TestProvider provider = CreateAnchor(setup,
                () => fail ? (PresenceDetector)new FailingDetector() : new PublishingDetector());
            setup.gameObject.SetActive(true);

            Assert.Throws<InvalidOperationException>(() => setup.StartRealm());
            Assert.That(setup.Realm, Is.Null);
            Assert.That(provider.GetComponent<AnchorSetup>().Anchor, Is.Null);

            // Fix the provider and retry the same setup, using the same prefab.
            fail = false;
            setup.StartRealm();
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Enabling a prefab starts and updates its detector without manual calls; disabling releases its population.
        /// </summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator EnabledPrefab_AutomaticallyStartsUpdatesAndStops()
        {
            RealmSetup setup = CreateRealm("automatic lifecycle");
            string currentId = "first";
            TestProvider provider = CreateAnchor(setup, () => new CurrentEntityDetector(() => currentId));
            setup.gameObject.SetActive(true);
            yield return null;

            // Let Unity start, update and stop the components. The test must not call Realm.Update itself.
            Realm realm = setup.Realm;
            Assert.That(realm, Is.Not.Null);
            Assert.That(realm.Query().Single().Key.EntityId, Is.EqualTo("first"));
            currentId = "second";
            yield return null;
            Assert.That(realm.Query().Single().Key.EntityId, Is.EqualTo("second"));

            setup.gameObject.SetActive(false);
            Assert.That(setup.Realm, Is.Null);
            Assert.That(provider.GetComponent<AnchorSetup>().Anchor, Is.Null);
            Assert.That(realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// An explicit stop cancels pending automatic startup while preserving the configuration for a later manual start.
        /// </summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator StopBeforeFirstUpdate_CancelsAutomaticStart()
        {
            RealmSetup setup = CreateRealm("pending");
            CreateAnchor(setup, () => new PublishingDetector());
            setup.gameObject.SetActive(true);
            setup.StopRealm();
            yield return null;

            Assert.That(setup.Realm, Is.Null);
            setup.StartRealm();
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
        }

        private RealmSetup CreateRealm(string name)
        {
            GameObject root = new GameObject(name);
            _objects.Add(root);
            // Finish setting up the inactive prefab before Unity can start it.
            root.SetActive(false);
            return root.AddComponent<RealmSetup>();
        }

        private TestProvider CreateAnchor(RealmSetup realm, Func<PresenceDetector> factory)
        {
            GameObject owner = new GameObject("source");
            _objects.Add(owner);
            owner.transform.SetParent(realm.transform, false);
            TestProvider provider = owner.AddComponent<TestProvider>();
            provider.Factory = factory;
            owner.AddComponent<AnchorSetup>();
            return provider;
        }

        private static void AssertConfiguredPresence(Realm realm)
        {
            Presence presence;
            Assert.That(realm.TryGetPresence(new Key("default", TestKind, "one"), out presence), Is.True);
            Assert.That(realm.Query().Single(), Is.TypeOf<TextGhost>());
            TextTrait trait = presence.Root.GetComponent<TextTrait>();
            Assert.That(trait, Is.Not.Null);
            Assert.That(trait.Value, Is.EqualTo("one"));
        }

    }
}
