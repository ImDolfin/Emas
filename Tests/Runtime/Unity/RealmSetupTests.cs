using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies prefab-owned realms, anchors, source providers and reference frames.
    /// </summary>
    public sealed class RealmSetupTests
    {
        private static readonly Kind FirstKind = new Kind("setup.first");
        private static readonly Kind SecondKind = new Kind("setup.second");
        private static readonly Kind SpatialKind = new Kind("setup.spatial");
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        /// <summary>
        /// Releases all prefab objects and their isolated realms.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            DisableRealmWhenEnabled.Target = null;
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
        /// Prefab instances with the same anchor IDs and kinds own independent populations and views.
        /// </summary>
        [Test]
        public void MultipleRealms_KeepAnchorsAndManifestationBlueprintsIndependent()
        {
            RealmSetup first = CreateRealm("screen one");
            RealmSetup second = CreateRealm("screen two");
            ManifestationBlueprint firstManifestationBlueprint = CreateManifestationBlueprint(FirstKind, "first view");
            ManifestationBlueprint secondManifestationBlueprint = CreateManifestationBlueprint(FirstKind, "second view");
            SetField(first, "_blueprints", new[] { firstManifestationBlueprint });
            SetField(second, "_blueprints", new[] { secondManifestationBlueprint });
            CreateAnchor(first, "screen", () => new PublishingSource(FirstKind));
            CreateAnchor(second, "screen", () => new PublishingSource(FirstKind));
            first.gameObject.SetActive(true);
            second.gameObject.SetActive(true);
            first.StartRealm();
            second.StartRealm();
            first.Realm.Update();
            second.Realm.Update();

            Assert.That(first.Realm, Is.Not.SameAs(second.Realm));
            Assert.That(first.Realm, Is.Not.SameAs(Realm.Default));
            Assert.That(first.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(second.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(Query.All().OfKind(FirstKind).Count, Is.EqualTo(2));
            Assert.That(ViewName(first, "screen", FirstKind), Is.EqualTo("first view"));
            Assert.That(ViewName(second, "screen", FirstKind), Is.EqualTo("second view"));

            first.StopRealm();
            Assert.That(first.Realm, Is.Null);
            Assert.That(second.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(Query.All().OfKind(FirstKind).Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A nested screen prefab keeps its anchors in its own realm when the environment realm stops.
        /// </summary>
        [Test]
        public void NestedRealmSetup_KeepsScreenAnchorsSeparate()
        {
            RealmSetup environment = CreateRealm("environment");
            RealmSetup screen = CreateRealm("screen");
            screen.transform.SetParent(environment.transform, false);
            CreateAnchor(environment, "shared", () => new PublishingSource(FirstKind));
            CreateAnchor(screen, "shared", () => new PublishingSource(FirstKind));
            environment.gameObject.SetActive(true);
            screen.gameObject.SetActive(true);
            environment.StartRealm();
            screen.StartRealm();

            Assert.That(environment.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(screen.Realm.Query().Count, Is.EqualTo(1));
            environment.StopRealm();
            Assert.That(screen.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// One prefab realm supports several anchors, one source per anchor and several blueprints per anchor.
        /// </summary>
        [Test]
        public void MultipleAnchors_UseOneSourceEachAndIndependentManifestationBlueprints()
        {
            RealmSetup setup = CreateRealm("environment");
            ManifestationBlueprint first = CreateManifestationBlueprint(FirstKind, "first view");
            ManifestationBlueprint second = CreateManifestationBlueprint(SecondKind, "second view");
            ManifestationBlueprint right = CreateManifestationBlueprint(FirstKind, "right view");
            SetField(setup, "_blueprints", new[] { first });
            TestProvider leftProvider = CreateAnchor(setup, "left",
                () => new PublishingSource(FirstKind, SecondKind), second);
            TestProvider rightProvider = CreateAnchor(setup, "right",
                () => new PublishingSource(FirstKind), right);
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            setup.Realm.Update();

            Assert.That(setup.Realm.Anchors.Count, Is.EqualTo(2));
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(3));
            Assert.That(leftProvider.Calls, Is.EqualTo(1));
            Assert.That(rightProvider.Calls, Is.EqualTo(1));
            Assert.That(ViewName(setup, "left", FirstKind), Is.EqualTo("first view"));
            Assert.That(ViewName(setup, "left", SecondKind), Is.EqualTo("second view"));
            Assert.That(ViewName(setup, "right", FirstKind), Is.EqualTo("right view"));

            AnchorSetup left = leftProvider.GetComponent<AnchorSetup>();
            left.enabled = false;
            Assert.That(left.Anchor, Is.Null);
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
            left.enabled = true;
            setup.Realm.Update();
            Assert.That(leftProvider.Calls, Is.EqualTo(2));
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(3));

            leftProvider.gameObject.SetActive(false);
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
            leftProvider.gameObject.SetActive(true);
            setup.Realm.Update();
            Assert.That(leftProvider.Calls, Is.EqualTo(3));
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Late-enabled anchors receive their root and modules once per realm lifetime.
        /// </summary>
        [Test]
        public void LateEnabledAnchor_ConfiguresBeforeDetectionAndOncePerLifetime()
        {
            RealmSetup setup = CreateRealm("late configuration");
            TestProvider active = CreateConfiguredAnchor(setup, "active", FirstKind);
            TestProvider late = CreateConfiguredAnchor(setup, "late", SecondKind);
            late.gameObject.SetActive(false);
            TestConfigurator activeConfigurator = active.GetComponent<TestConfigurator>();
            TestConfigurator lateConfigurator = late.GetComponent<TestConfigurator>();
            setup.gameObject.SetActive(true);
            setup.StartRealm();

            Assert.That(activeConfigurator.Calls, Is.EqualTo(1));
            Assert.That(lateConfigurator.Calls, Is.Zero);
            late.gameObject.SetActive(true);
            AssertConfiguredPresence(setup.Realm, "late", SecondKind);
            Assert.That(activeConfigurator.Calls, Is.EqualTo(1));
            Assert.That(lateConfigurator.Calls, Is.EqualTo(1));

            late.gameObject.SetActive(false);
            late.gameObject.SetActive(true);
            AssertConfiguredPresence(setup.Realm, "late", SecondKind);
            Assert.That(lateConfigurator.Calls, Is.EqualTo(1));
            Assert.That(late.Calls, Is.EqualTo(2));

            setup.StopRealm();
            setup.StartRealm();
            AssertConfiguredPresence(setup.Realm, "active", FirstKind);
            AssertConfiguredPresence(setup.Realm, "late", SecondKind);
            Assert.That(activeConfigurator.Calls, Is.EqualTo(2));
            Assert.That(lateConfigurator.Calls, Is.EqualTo(2));
        }

        /// <summary>
        /// A provider and configurator added after AnchorSetup can initialize during late activation.
        /// </summary>
        [Test]
        public void LateEnabledAnchor_ProviderAfterAnchorSetupConfiguresBeforeDetection()
        {
            RealmSetup setup = CreateRealm("late provider order");
            GameObject owner = new GameObject("source");
            _objects.Add(owner);
            owner.SetActive(false);
            owner.transform.SetParent(setup.transform, false);
            AnchorSetup anchorSetup = owner.AddComponent<AnchorSetup>();
            SetField(anchorSetup, "_anchorId", "source");
            ConfiguredProvider provider = owner.AddComponent<ConfiguredProvider>();
            setup.gameObject.SetActive(true);
            setup.StartRealm();

            owner.SetActive(true);
            AssertConfiguredPresence(setup.Realm, "source", FirstKind);
            Assert.That(provider.ConfigurationCalls, Is.EqualTo(1));
            Assert.That(provider.DetectorCalls, Is.EqualTo(1));

            owner.SetActive(false);
            owner.SetActive(true);
            AssertConfiguredPresence(setup.Realm, "source", FirstKind);
            Assert.That(provider.ConfigurationCalls, Is.EqualTo(1));
            Assert.That(provider.DetectorCalls, Is.EqualTo(2));
        }

        /// <summary>
        /// Configurators can enable another anchor before either detector starts.
        /// </summary>
        [Test]
        public void LateConfiguration_EnablesAndConfiguresAnotherAnchor()
        {
            RealmSetup setup = CreateRealm("configuration callback");
            TestProvider target = CreateConfiguredAnchor(setup, "target", SecondKind);
            TestProvider trigger = CreateConfiguredAnchor(setup, "trigger", FirstKind);
            target.gameObject.SetActive(false);
            trigger.gameObject.SetActive(false);
            trigger.GetComponent<TestConfigurator>().OnConfigure = realm => target.gameObject.SetActive(true);
            setup.gameObject.SetActive(true);
            setup.StartRealm();

            trigger.gameObject.SetActive(true);
            AssertConfiguredPresence(setup.Realm, "trigger", FirstKind);
            AssertConfiguredPresence(setup.Realm, "target", SecondKind);
            Assert.That(trigger.GetComponent<TestConfigurator>().Calls, Is.EqualTo(1));
            Assert.That(target.GetComponent<TestConfigurator>().Calls, Is.EqualTo(1));
            Assert.That(trigger.Calls, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Anchors enabled during source startup configure and attach before startup completes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SourceStartup_CanEnableAnotherConfiguredAnchor(bool lateStart)
        {
            RealmSetup setup = CreateRealm("source enables anchor");
            TestProvider target = CreateConfiguredAnchor(setup, "target", SecondKind);
            TestProvider trigger = CreateAnchor(setup, "trigger", () =>
                new PollingPresenceDetector<string, Probe>(FirstKind)
                    .ReadFrom(() =>
                    {
                        target.gameObject.SetActive(true);
                        return new[] { "one" };
                    })
                    .IdentifyBy(value => value)
                    .Apply((value, ghost) => { }));
            target.gameObject.SetActive(false);
            trigger.gameObject.SetActive(!lateStart);
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            if (lateStart)
            {
                trigger.gameObject.SetActive(true);
            }

            AssertConfiguredPresence(setup.Realm, "target", SecondKind);
            Assert.That(target.GetComponent<TestConfigurator>().Calls, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
            Assert.That(trigger.Calls, Is.EqualTo(1));
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// A configurator enabled by an earlier detector runs before its active anchor attaches.
        /// </summary>
        [Test]
        public void SourceStartup_CanEnableConfiguratorOnAnotherActiveAnchor()
        {
            RealmSetup setup = CreateRealm("source enables configurator");
            TestConfigurator targetConfigurator = null;
            TestProvider trigger = CreateAnchor(setup, "trigger", () =>
                new PollingPresenceDetector<string, Probe>(FirstKind)
                    .ReadFrom(() =>
                    {
                        targetConfigurator.enabled = true;
                        return new[] { "one" };
                    })
                    .IdentifyBy(value => value)
                    .Apply((value, ghost) => { }));
            TestProvider target = CreateConfiguredAnchor(setup, "target", SecondKind);
            targetConfigurator = target.GetComponent<TestConfigurator>();
            targetConfigurator.enabled = false;
            setup.gameObject.SetActive(true);
            setup.StartRealm();

            AssertConfiguredPresence(setup.Realm, "target", SecondKind);
            Assert.That(targetConfigurator.Calls, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
            Assert.That(trigger.Calls, Is.EqualTo(1));
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Stopping during configuration prevents detector startup and permits a fresh configuration.
        /// </summary>
        [Test]
        public void Configuration_CanStopRealmAndRetry()
        {
            RealmSetup setup = CreateRealm("stopped configuration");
            TestProvider provider = CreateConfiguredAnchor(setup, "source", FirstKind);
            TestConfigurator configurator = provider.GetComponent<TestConfigurator>();
            configurator.OnConfigure = realm => setup.StopRealm();
            setup.gameObject.SetActive(true);

            Assert.Throws<InvalidOperationException>(() => setup.StartRealm());
            Assert.That(setup.Realm, Is.Null);
            Assert.That(provider.Calls, Is.Zero);
            Assert.That(provider.GetComponent<AnchorSetup>().Anchor, Is.Null);

            configurator.OnConfigure = null;
            setup.StartRealm();
            AssertConfiguredPresence(setup.Realm, "source", FirstKind);
            Assert.That(configurator.Calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Disabling a moved anchor releases the realm that originally attached it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ReparentedAnchor_DisablingDetachesOriginalRealm(bool disableGameObject)
        {
            RealmSetup setup = CreateRealm("original owner");
            PublishingSource source = null;
            TestProvider provider = CreateAnchor(setup, "source", () =>
            {
                source = new PublishingSource(FirstKind);
                return source;
            });
            AnchorSetup anchorSetup = provider.GetComponent<AnchorSetup>();
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            Anchor original = anchorSetup.Anchor;
            provider.transform.SetParent(null, false);
            Assert.That(anchorSetup.Anchor, Is.SameAs(original));

            if (disableGameObject)
            {
                provider.gameObject.SetActive(false);
            }
            else
            {
                anchorSetup.enabled = false;
            }

            Assert.That(anchorSetup.Anchor, Is.Null);
            Assert.That(source.IsAttached, Is.False);
            Assert.That(original.Detectors, Is.Empty);
            Assert.That(setup.Realm.ContainsAnchor("source"), Is.False);
            Assert.That(setup.Realm.Query().Count, Is.Zero);

            provider.transform.SetParent(setup.transform, false);
            provider.gameObject.SetActive(true);
            anchorSetup.enabled = true;
            Assert.That(provider.Calls, Is.EqualTo(2));
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Moving an attached anchor into another realm does not attach it twice.
        /// </summary>
        [Test]
        public void ReparentedAnchor_ChangesRealmAfterReenable()
        {
            RealmSetup first = CreateRealm("original realm");
            RealmSetup second = CreateRealm("next realm");
            TestProvider provider = CreateAnchor(first, "source", () => new PublishingSource(FirstKind));
            AnchorSetup setup = provider.GetComponent<AnchorSetup>();
            first.gameObject.SetActive(true);
            first.StartRealm();
            Anchor original = setup.Anchor;
            second.gameObject.SetActive(true);
            provider.transform.SetParent(second.transform, false);
            second.StartRealm();

            Assert.That(setup.Anchor, Is.SameAs(original));
            Assert.That(provider.Calls, Is.EqualTo(1));
            Assert.That(first.Realm.Query().Count, Is.EqualTo(1));
            Assert.That(second.Realm.Query().Count, Is.Zero);

            setup.enabled = false;
            Assert.That(first.Realm.Query().Count, Is.Zero);
            setup.enabled = true;
            Assert.That(setup.Anchor.Realm, Is.SameAs(second.Realm));
            Assert.That(provider.Calls, Is.EqualTo(2));
            first.StopRealm();
            Assert.That(setup.Anchor.Realm, Is.SameAs(second.Realm));
            Assert.That(second.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// An empty anchor override keeps its ghosts silent while another anchor uses the realm view.
        /// </summary>
        [Test]
        public void EmptyManifestationBlueprintOverride_SkipsAutomaticViews()
        {
            RealmSetup setup = CreateRealm("silent override");
            ManifestationBlueprint visible = CreateManifestationBlueprint(FirstKind, "realm view");
            ManifestationBlueprint silent = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            silent.Configure(FirstKind, null, null, null);
            _objects.Add(silent);
            SetField(setup, "_blueprints", new[] { visible });
            CreateAnchor(setup, "silent", () => new PublishingSource(FirstKind), silent);
            CreateAnchor(setup, "visible", () => new PublishingSource(FirstKind));
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
                Application.logMessageReceived += onLog;
                setup.gameObject.SetActive(true);
                setup.StartRealm();
                setup.Realm.Update();

                Ghost silentGhost = (Ghost)setup.Realm.Query().InAnchor("silent").Single();
                Assert.That(silentGhost.IsAvailable, Is.True);
                Assert.That(silentGhost.gameObject.activeInHierarchy, Is.True);
                Assert.That(silentGhost.GetComponentInChildren<View>(true), Is.Null);
                Assert.That(ViewName(setup, "visible", FirstKind), Is.EqualTo("realm view"));
                Assert.That(warnings, Is.Empty);
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        /// <summary>
        /// A prefab reference frame can follow a spatial ghost without converting global doubles to floats.
        /// </summary>
        [Test]
        public void FollowedReference_ProjectsSpatialGhosts()
        {
            RealmSetup setup = CreateRealm("spatial screen");
            SetField(setup, "_useReferenceFrame", true);
            SetField(setup, "_followGhost", true);
            SetField(setup, "_referenceAnchorId", "world");
            SetField(setup, "_referenceKind", SpatialKind);
            SetField(setup, "_referenceEntityId", "ego");
            SetField(setup, "_limitDistance", true);
            SetField(setup, "_maxDistance", 50.0);
            CreateAnchor(setup, "world", () => new SpatialSource());
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            setup.Realm.Update();

            Assert.That(setup.Realm.ReferenceFrame.FollowedGhost,
                Is.EqualTo(new Key("world", SpatialKind, "ego")));
            Assert.That(setup.Realm.ReferenceFrame.IsReferenceAvailable, Is.True);
            SpatialProbe ego = GetSpatial(setup.Realm, "ego");
            SpatialProbe traffic = GetSpatial(setup.Realm, "traffic");
            Assert.That(ego.transform.position.sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(traffic.transform.position.x, Is.EqualTo(20.25f).Within(0.0001f));
        }

        /// <summary>
        /// A manual reference in the prefab maps nearby double positions correctly.
        /// </summary>
        [Test]
        public void ManualReference_UsesDoublePosition()
        {
            RealmSetup setup = CreateRealm("manual frame");
            SetField(setup, "_useReferenceFrame", true);
            SetField(setup, "_position", new Double3(1000000000.125, 0.0, 1000000000.375));
            CreateAnchor(setup, "world", () => new SpatialSource());
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            setup.Realm.Update();

            Assert.That(setup.Realm.ReferenceFrame.HasPosition, Is.True);
            SpatialProbe traffic = GetSpatial(setup.Realm, "traffic");
            Assert.That(traffic.transform.position.x, Is.EqualTo(20.25f).Within(0.0001f));
        }

        /// <summary>
        /// Explicit stop before the first frame cancels automatic startup without losing the prefab settings.
        /// </summary>
        [UnityTest]
        public IEnumerator StopBeforeFirstUpdate_CancelsAutomaticStart()
        {
            RealmSetup setup = CreateRealm("pending");
            TestProvider provider = CreateAnchor(setup, "pending",
                () => new PublishingSource(FirstKind));
            setup.gameObject.SetActive(true);
            setup.StopRealm();
            yield return null;

            Assert.That(setup.Realm, Is.Null);
            Assert.That(provider.Calls, Is.Zero);
            setup.StartRealm();
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Source startup failure disposes the partial realm and allows a corrected restart.
        /// </summary>
        [Test]
        public void StartupFailure_CleansUpAndCanRetry()
        {
            RealmSetup setup = CreateRealm("retry");
            bool fail = true;
            TestProvider provider = CreateAnchor(setup, "retry",
                () => fail ? (PresenceDetector)new FailingSource() : new PublishingSource(FirstKind));
            setup.gameObject.SetActive(true);
            Assert.Throws<InvalidOperationException>(() => setup.StartRealm());
            Assert.That(setup.Realm, Is.Null);
            Assert.That(provider.GetComponent<AnchorSetup>().Anchor, Is.Null);

            fail = false;
            setup.StartRealm();
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
            setup.StopRealm();
            setup.StopRealm();
            Assert.That(setup.Realm, Is.Null);
        }

        /// <summary>
        /// A source callback can stop the prefab realm during startup without retaining an attachment.
        /// </summary>
        [Test]
        public void StopDuringSourceStartup_CleansUpAndCanRetry()
        {
            RealmSetup setup = CreateRealm("stopping source");
            bool stop = true;
            TestProvider provider = CreateAnchor(setup, "source", () =>
                new PollingPresenceDetector<string, Probe>(FirstKind)
                    .ReadFrom(() =>
                    {
                        if (stop)
                        {
                            setup.StopRealm();
                        }

                        return new[] { "one" };
                    })
                    .IdentifyBy(id => id)
                    .Apply((id, ghost) => { }));
            setup.gameObject.SetActive(true);
            Assert.Throws<InvalidOperationException>(() => setup.StartRealm());
            Assert.That(setup.Realm, Is.Null);
            Assert.That(provider.GetComponent<AnchorSetup>().Anchor, Is.Null);
            stop = false;
            setup.StartRealm();
            Assert.That(setup.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Disabling the realm from a new view releases its population during the update.
        /// </summary>
        [Test]
        public void ViewActivation_CanDisableRealm()
        {
            RealmSetup setup = CreateRealm("view callback");
            ManifestationBlueprint blueprint = CreateManifestationBlueprint(FirstKind, "callback view");
            blueprint.FallbackViewPrefab.AddComponent<DisableRealmWhenEnabled>();
            SetField(setup, "_blueprints", new[] { blueprint });
            bool publish = false;
            CreateAnchor(setup, "source", () =>
                new PollingPresenceDetector<string, Probe>(FirstKind)
                    .ReadFrom(() => publish ? new[] { "one" } : new string[0])
                    .IdentifyBy(id => id)
                    .Apply((id, ghost) => { }));
            setup.gameObject.SetActive(true);
            setup.StartRealm();
            Realm realm = setup.Realm;
            DisableRealmWhenEnabled.Target = setup;
            publish = true;
            realm.Update();
            Assert.That(setup.Realm, Is.Null);
            Assert.That(realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Invalid anchor and frame settings fail before creating an isolated realm.
        /// </summary>
        [Test]
        public void Validation_RejectsMissingSourceDuplicateIdsAndBadFrame()
        {
            RealmSetup setup = CreateRealm("validation");
            GameObject missing = new GameObject("missing provider");
            _objects.Add(missing);
            missing.transform.SetParent(setup.transform, false);
            AnchorSetup anchor = missing.AddComponent<AnchorSetup>();
            SetField(anchor, "_anchorId", "left");
            setup.gameObject.SetActive(true);
            Assert.That(setup.GetConfigurationError(), Does.Contain("exactly one IDetectorProvider"));
            Assert.Throws<InvalidOperationException>(() => setup.StartRealm());
            Assert.That(setup.Realm, Is.Null);

            TestProvider provider = missing.AddComponent<TestProvider>();
            provider.Factory = () => new PublishingSource(FirstKind);
            CreateAnchor(setup, "left", () => new PublishingSource(SecondKind));
            Assert.That(setup.GetConfigurationError(), Does.Contain("duplicate anchor ID"));
            SetField(anchor, "_anchorId", "other");
            SetField(setup, "_useReferenceFrame", true);
            SetField(setup, "_followGhost", true);
            Assert.That(setup.GetConfigurationError(), Does.Contain("followed ghost"));
            SetField(setup, "_useReferenceFrame", false);
            ManifestationBlueprint blueprint = CreateManifestationBlueprint(FirstKind, "realm default");
            SetField(setup, "_blueprints", new[] { blueprint, blueprint });
            Assert.That(setup.GetConfigurationError(), Does.Contain("duplicates kind"));
            SetField(setup, "_blueprints", new[] { blueprint });
            Assert.That(setup.GetConfigurationError(), Is.Null);
        }

        private RealmSetup CreateRealm(string name)
        {
            GameObject root = new GameObject(name);
            _objects.Add(root);
            root.SetActive(false);
            return root.AddComponent<RealmSetup>();
        }

        private TestProvider CreateAnchor(RealmSetup realm, string id,
            Func<PresenceDetector> factory, params ManifestationBlueprint[] blueprints)
        {
            GameObject owner = new GameObject(id);
            _objects.Add(owner);
            owner.transform.SetParent(realm.transform, false);
            TestProvider provider = owner.AddComponent<TestProvider>();
            provider.Factory = factory;
            AnchorSetup setup = owner.AddComponent<AnchorSetup>();
            SetField(setup, "_anchorId", id);
            SetField(setup, "_blueprints", blueprints);
            return provider;
        }

        private TestProvider CreateConfiguredAnchor(RealmSetup realm, string id, Kind kind)
        {
            TestProvider provider = CreateAnchor(realm, id, () => new PollingPresenceDetector<string>(kind)
                .ReadFrom(() => new[] { "one" }).IdentifyBy(value => value));
            TestConfigurator configurator = provider.gameObject.AddComponent<TestConfigurator>();
            configurator.Kind = kind;
            return provider;
        }

        private static void AssertConfiguredPresence(Realm realm, string anchorId, Kind kind)
        {
            Presence presence;
            Assert.That(realm.TryGetPresence(new Key(anchorId, kind, "one"), out presence), Is.True);
            Assert.That(realm.Query().InAnchor(anchorId).Single(), Is.TypeOf<Probe>());
            ReadingModule module;
            Assert.That(presence.TryGetModule(out module), Is.True);
            Assert.That(module.Value, Is.EqualTo("one"));
        }

        private ManifestationBlueprint CreateManifestationBlueprint(Kind kind, string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            _objects.Add(prefab);
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(kind, null, null, prefab);
            _objects.Add(blueprint);
            return blueprint;
        }

        private static SpatialProbe GetSpatial(Realm realm, string id)
        {
            IGhost ghost;
            Assert.That(realm.TryGetGhost(new Key("world", SpatialKind, id), out ghost), Is.True);
            return (SpatialProbe)ghost;
        }

        private static string ViewName(RealmSetup setup, string anchorId, Kind kind)
        {
            Ghost ghost = (Ghost)setup.Realm.Query().InAnchor(anchorId).OfKind(kind).Single();
            return ghost.GetComponentInChildren<View>().gameObject.name;
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private sealed class DisableRealmWhenEnabled : MonoBehaviour
        {
            internal static RealmSetup Target;

            private void OnEnable()
            {
                if (Target != null)
                {
                    Target.enabled = false;
                }
            }
        }

        private sealed class TestProvider : MonoBehaviour, IDetectorProvider
        {
            internal Func<PresenceDetector> Factory;
            internal int Calls;

            /// <summary>
            /// Creates this test anchor's source.
            /// </summary>
            public PresenceDetector CreateDetector()
            {
                Calls++;
                return Factory();
            }
        }

        private sealed class ConfiguredProvider : MonoBehaviour, IDetectorProvider, IRealmConfigurator
        {
            internal int ConfigurationCalls;
            internal int DetectorCalls;

            /// <summary>
            /// Installs the test root and module before this provider starts detection.
            /// </summary>
            public void ConfigureRealm(Realm realm)
            {
                ConfigurationCalls++;
                realm.RegisterPresenceInitializer<Probe>(FirstKind,
                    (presence, root) => presence.AddModule(new ReadingModule()));
            }

            /// <summary>
            /// Creates a detector after the provider has configured its root.
            /// </summary>
            public PresenceDetector CreateDetector()
            {
                Assert.That(ConfigurationCalls, Is.EqualTo(1));
                DetectorCalls++;
                return new PollingPresenceDetector<string>(FirstKind)
                    .ReadFrom(() => new[] { "one" }).IdentifyBy(value => value);
            }
        }

        private sealed class TestConfigurator : MonoBehaviour, IRealmConfigurator
        {
            internal Kind Kind;
            internal int Calls;
            internal Action<Realm> OnConfigure;

            /// <summary>
            /// Installs this test anchor's root and data module.
            /// </summary>
            public void ConfigureRealm(Realm realm)
            {
                Calls++;
                realm.RegisterPresenceInitializer<Probe>(Kind,
                    (presence, root) => presence.AddModule(new ReadingModule()));
                OnConfigure?.Invoke(realm);
            }
        }

        private sealed class ReadingModule : EntityModule<string>
        {
            internal string Value;

            /// <summary>
            /// Records the data delivered after root initialization.
            /// </summary>
            public override void Apply(string data)
            {
                Value = data;
            }
        }

        private sealed class PublishingSource : PresenceDetector
        {
            private readonly Kind[] _kinds;

            internal PublishingSource(params Kind[] kinds)
            {
                _kinds = kinds;
            }

            protected override void OnStart()
            {
                foreach (Kind kind in _kinds)
                {
                    GetOrCreate<Probe>(kind.Id, kind);
                }
            }
        }

        private sealed class FailingSource : PresenceDetector
        {
            protected override void OnStart()
            {
                throw new InvalidOperationException("source startup failed");
            }
        }

        private sealed class SpatialSource : PresenceDetector
        {
            protected override void OnStart()
            {
                SpatialProbe ego = GetOrCreate<SpatialProbe>("ego", SpatialKind);
                ego.GetComponent<Spatial>().SetPosition(
                    new Double3(1000000000.125, 0.0, 1000000000.375));
                SpatialProbe traffic = GetOrCreate<SpatialProbe>("traffic", SpatialKind);
                traffic.GetComponent<Spatial>().SetPosition(
                    new Double3(1000000020.375, 0.0, 1000000000.375));
            }
        }

        private sealed class Probe : Ghost
        {
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class SpatialProbe : Ghost
        {
        }
    }
}
