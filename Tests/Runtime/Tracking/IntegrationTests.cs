using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises complete-snapshot polling and Inspector-owned tracking against Unity.
    /// </summary>
    public sealed class IntegrationTests
    {
        private static readonly Kind Population = new Kind("tests.polling");
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private Realm _realm;

        /// <summary>
        /// Creates an empty default realm for scene integration.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            Realm.Default.Dispose();
            _realm = Realm.Default;
        }

        /// <summary>
        /// Releases test objects and tracking state.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            StopSetupWhenEnabled.Target = null;
            _realm.Dispose();
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
        /// Invalid configuration inputs fail before a source is attached.
        /// </summary>
        [Test]
        public void Polling_ValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new PollingPresenceSource<string, Probe>(default(Kind)));
            PollingPresenceSource<string, Probe> source = new PollingPresenceSource<string, Probe>(Population);
            Assert.Throws<ArgumentNullException>(() => source.ReadFrom(null));
            Assert.Throws<ArgumentNullException>(() => source.IdentifyBy(null));
            Assert.Throws<ArgumentNullException>(() => source.Apply(null));
            Assert.Throws<ArgumentNullException>(() => source.WithVariant(null));
        }

        /// <summary>
        /// Missing required steps fail before reading data and leave setup ready to retry.
        /// </summary>
        [TestCase("ReadFrom")]
        [TestCase("IdentifyBy")]
        [TestCase("Apply")]
        [TestCase("ReadFrom, IdentifyBy, Apply")]
        public void Polling_RequiresCompleteConfiguration(string missing)
        {
            int reads = 0;
            PollingPresenceSource<string, Probe> source = new PollingPresenceSource<string, Probe>(Population);
            if (!missing.Contains("ReadFrom"))
            {
                source.ReadFrom(() =>
                {
                    reads++;
                    return new[] { "a" };
                });
            }

            if (!missing.Contains("IdentifyBy"))
            {
                source.IdentifyBy(id => id);
            }

            if (!missing.Contains("Apply"))
            {
                source.Apply((item, ghost) =>
                {
                });
            }

            SceneSetup setup = CreateSetup();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => setup.Track(source));
            Assert.That(error.Message, Is.EqualTo("Polling source is missing required steps: " + missing + ". Configure them before tracking."));
            Assert.That(reads, Is.Zero);
            Assert.That(setup.Anchor, Is.Null);
            Assert.That(_realm.ContainsAnchor("default"), Is.False);
            source.ReadFrom(() => new[] { "a" }).IdentifyBy(id => id).Apply((item, ghost) =>
            {
            });
            setup.Track(source);
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Callbacks remain stable while attached, including after a polling failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Polling_ConfigurationIsLockedUntilDetached(bool fail)
        {
            bool broken = false;
            PollingPresenceSource<string, Probe> source = Source(() => broken ? null : new[] { "a" });
            Anchor anchor = _realm.GetOrCreateAnchor("poll", source);
            if (fail)
            {
                broken = true;
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
                _realm.Update();
            }

            Assert.Throws<InvalidOperationException>(() => source.ReadFrom(() => new[] { "b" }));
            Assert.Throws<InvalidOperationException>(() => source.IdentifyBy(id => "changed"));
            Assert.Throws<InvalidOperationException>(() => source.Apply((item, ghost) => ghost.Value = 99));
            Assert.Throws<InvalidOperationException>(() => source.WithVariant(item => new Variant("changed")));
            anchor.RemoveSource(source);
            source.ReadFrom(() => new[] { "b" }).IdentifyBy(id => id)
                .Apply((item, ghost) => ghost.Value = 3).WithVariant(item => new Variant("new"));
            anchor.AddSource(source);
            Probe ghost = (Probe)_realm.Query().Single();
            Assert.That(ghost.Key.EntityId, Is.EqualTo("b"));
            Assert.That(ghost.Value, Is.EqualTo(3));
            Assert.That(ghost.Variant, Is.EqualTo(new Variant("new")));
        }

        /// <summary>
        /// Polling preserves identity, maps data and removes missing entities.
        /// </summary>
        [Test]
        public void Polling_ReconcilesCompleteSnapshots()
        {
            List<string> ids = new List<string> { "a", "b" };
            int value = 1;
            _realm.GetOrCreateAnchor("poll", new PollingPresenceSource<string, Probe>(Population)
                .ReadFrom(() => ids)
                .IdentifyBy(id => id)
                .Apply((item, ghost) => ghost.Value = value));
            Probe first = (Probe)_realm.Query("a").Single();
            IGhost second = _realm.Query("b").Single();
            ids.Remove("b");
            ids.Add("c");
            value = 2;
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            Assert.That(_realm.Query("a").Single(), Is.SameAs(first));
            Assert.That(first.Value, Is.EqualTo(2));
            Assert.That(second.IsAvailable, Is.False);
            ids.Clear();
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Optional appearance selectors update the retained ghost.
        /// </summary>
        [Test]
        public void Polling_MapsVariants()
        {
            Variant variant = new Variant("first");
            _realm.GetOrCreateAnchor("poll", new PollingPresenceSource<string, Probe>(Population)
                .ReadFrom(() => new[] { "a" })
                .IdentifyBy(id => id)
                .Apply((item, ghost) =>
                {
                })
                .WithVariant(item => variant));
            IGhost ghost = _realm.Query().Single();
            variant = new Variant("second");
            _realm.Update();
            Assert.That(ghost.Variant, Is.EqualTo(variant));
        }

        /// <summary>
        /// Replacement reconciles transferred identities even on its first poll.
        /// </summary>
        [Test]
        public void Polling_ReplacementRemovesAbsentTransferredGhosts()
        {
            PollingPresenceSource<string, Probe> first = Source(() => new[] { "a", "b" });
            Anchor anchor = _realm.GetOrCreateAnchor("poll", first);
            IGhost retained = _realm.Query("a").Single();
            IGhost removed = _realm.Query("b").Single();
            anchor.ReplaceSource(first, Source(() => new[] { "a" }));
            Assert.That(_realm.Query().Single(), Is.SameAs(retained));
            Assert.That(removed.IsAvailable, Is.False);
        }

        /// <summary>
        /// Malformed snapshots cannot delete the retained population.
        /// </summary>
        [TestCase("null")]
        [TestCase("duplicate")]
        [TestCase("empty-id")]
        [TestCase("enumeration")]
        public void Polling_InvalidSnapshotStopsWithoutDeleting(string failure)
        {
            bool fail = false;
            Func<IEnumerable<string>> read = () => !fail ? new[] { "a", "b" } :
                failure == "null" ? null : failure == "duplicate" ? new[] { "a", "a" } :
                failure == "empty-id" ? new[] { "" } : BrokenSnapshot();
            PollingPresenceSource<string, Probe> source = Source(read);
            Anchor anchor = _realm.GetOrCreateAnchor("poll", source);
            IGhost retained = _realm.Query("a").Single();
            fail = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            _realm.Update();
            Assert.That(_realm.GetOwnedGhosts(source).Count, Is.EqualTo(2));
            Assert.That(retained.IsAvailable, Is.False);
            anchor.ReplaceSource(source, Source(() => new[] { "a" }));
            Assert.That(_realm.Query().Single(), Is.SameAs(retained));
        }

        /// <summary>
        /// A failed mapper does not execute departures from a partial poll.
        /// </summary>
        [Test]
        public void Polling_MappingFailureKeepsUnpublishedDepartures()
        {
            bool fail = false;
            PollingPresenceSource<string, Probe> source = new PollingPresenceSource<string, Probe>(Population)
                .ReadFrom(() => fail ? new[] { "a" } : new[] { "a", "b" })
                .IdentifyBy(id => id)
                .Apply((item, ghost) =>
                {
                    if (fail)
                    {
                        throw new InvalidOperationException("mapper failed");
                    }
                });
            _realm.GetOrCreateAnchor("poll", source);
            fail = true;
            LogAssert.Expect(LogType.Exception, new Regex("mapper failed"));
            _realm.Update();
            Assert.That(_realm.GetOwnedGhosts(source).Count, Is.EqualTo(2));
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// A mapper can end its own registration without publishing remaining entries.
        /// </summary>
        [Test]
        public void Polling_CanRemoveAnchorDuringMapping()
        {
            bool remove = false;
            PollingPresenceSource<string, Probe> source = new PollingPresenceSource<string, Probe>(Population)
                .ReadFrom(() => new[] { "a", "b" })
                .IdentifyBy(id => id)
                .Apply((item, ghost) =>
                {
                    if (remove)
                    {
                        _realm.RemoveAnchor("poll");
                    }
                });
            _realm.GetOrCreateAnchor("poll", source);
            remove = true;
            _realm.Update();
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Scene ownership cleans up immediately on disable and can be started again.
        /// </summary>
        [Test]
        public void Setup_DisableCleansUpAndAllowsRestart()
        {
            SceneSetup setup = CreateSetup();
            Anchor anchor = setup.Track(Source(() => new[] { "a" }));
            Assert.That(setup.Anchor, Is.SameAs(anchor));
            Assert.That(anchor.Transform.parent, Is.EqualTo(setup.transform));
            setup.enabled = false;
            Assert.That(setup.Anchor, Is.Null);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => setup.Track());
            setup.enabled = true;
            Assert.That(setup.Track(Source(() => new[] { "b" })), Is.Not.SameAs(anchor));
        }

        /// <summary>
        /// Setup refuses to take over another owner's anchor.
        /// </summary>
        [Test]
        public void Setup_RejectsDuplicateOwnershipAndRepeatedStart()
        {
            Anchor existing = _realm.GetOrCreateAnchor("default", Source(() => new[] { "a" }));
            SceneSetup setup = CreateSetup();
            Assert.Throws<InvalidOperationException>(() => setup.Track());
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
            existing.Dispose();
            setup.Track();
            Assert.Throws<InvalidOperationException>(() => setup.Track());
        }

        /// <summary>
        /// Inspector-assigned blueprints produce views only when enabled.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Setup_AutomaticViewsRespectInspectorSetting(bool automatic)
        {
            SceneSetup setup = CreateSetup();
            GameObject prefab = new GameObject("view prefab");
            _objects.Add(prefab);
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            _objects.Add(blueprint);
            blueprint.Configure(Population, null, new Blueprint.ViewMapping[0], prefab);
            SetField(setup, "_blueprints", new[] { blueprint });
            SetField(setup, "_automaticViews", automatic);
            setup.Track(Source(() => new[] { "a" }));
            _realm.Update();
            Probe ghost = (Probe)_realm.Query().Single();
            Assert.That(ghost.GetComponentInChildren<View>() != null, Is.EqualTo(automatic));
            setup.enabled = false;
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Configuration errors identify the offending entry and leave setup ready to retry.
        /// </summary>
        [TestCase("null", "is null")]
        [TestCase("kind", "requires a non-empty kind ID")]
        [TestCase("duplicate", "duplicates kind 'tests.polling'")]
        public void Setup_ValidatesBlueprintsBeforeStarting(string failure, string expectedReason)
        {
            SceneSetup setup = CreateSetup();
            Blueprint first = ScriptableObject.CreateInstance<Blueprint>();
            _objects.Add(first);
            first.Configure(Population, null, null, null);
            Blueprint invalid = null;
            if (failure != "null")
            {
                invalid = ScriptableObject.CreateInstance<Blueprint>();
                invalid.name = "Invalid blueprint";
                _objects.Add(invalid);
                if (failure == "duplicate")
                {
                    invalid.Configure(Population, null, null, null);
                }
            }

            SetField(setup, "_blueprints", new[] { first, invalid });
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => setup.Track());
            Assert.That(error.Message, Does.Contain("index 1"));
            Assert.That(error.Message, Does.Contain(expectedReason));
            Assert.That(_realm.ContainsAnchor("default"), Is.False);
            Assert.That(setup.Anchor, Is.Null);
            SetField(setup, "_blueprints", new[] { first });
            Assert.That(setup.Track(), Is.Not.Null);
        }

        /// <summary>
        /// Reusing an anchor preserves its frame and starts only newly attached sources.
        /// </summary>
        [Test]
        public void GetOrCreateAnchor_ReusesAnchorAndStartsOnlyNewSources()
        {
            int reads = 0;
            PollingPresenceSource<string, Probe> first = Source(() =>
            {
                reads++;
                return new[] { "a" };
            });
            GameObject frame = new GameObject("source frame");
            _objects.Add(frame);
            Anchor anchor = _realm.GetOrCreateAnchor("shared", frame.transform, first);
            Anchor reused = _realm.GetOrCreateAnchor("shared", frame.transform, first, Source(() => new[] { "b" }));
            Assert.That(reused, Is.SameAs(anchor));
            Assert.That(reused.Transform.parent, Is.SameAs(frame.transform));
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// A startup exception rolls back the scene owner's population.
        /// </summary>
        [Test]
        public void Setup_StartupFailureCanBeRetried()
        {
            SceneSetup setup = CreateSetup();
            Assert.Throws<InvalidOperationException>(() => setup.Track(Source(() => null)));
            Assert.That(setup.Anchor, Is.Null);
            Assert.That(_realm.ContainsAnchor("default"), Is.False);
            Assert.That(setup.Track(Source(() => new[] { "a" })), Is.Not.Null);
        }

        /// <summary>
        /// Disabling from a startup callback cannot leave tracking behind.
        /// </summary>
        [Test]
        public void Setup_DisableDuringStartupCleansUp()
        {
            SceneSetup setup = CreateSetup();
            PollingPresenceSource<string, Probe> source = new PollingPresenceSource<string, Probe>(Population)
                .ReadFrom(() => new[] { "a" })
                .IdentifyBy(id => id)
                .Apply((item, ghost) => setup.enabled = false);
            Assert.Throws<InvalidOperationException>(() => setup.Track(source));
            Assert.That(setup.Anchor, Is.Null);
            Assert.That(_realm.ContainsAnchor("default"), Is.False);
        }

        /// <summary>
        /// A view activation callback can disable setup without leaving views or subscriptions alive.
        /// </summary>
        [Test]
        public void Setup_ViewCallbackCanDisableOwner()
        {
            SceneSetup setup = CreateSetup();
            GameObject prefab = new GameObject("callback view");
            _objects.Add(prefab);
            prefab.SetActive(false);
            prefab.AddComponent<StopSetupWhenEnabled>();
            Blueprint blueprint = ScriptableObject.CreateInstance<Blueprint>();
            _objects.Add(blueprint);
            blueprint.Configure(Population, null, new Blueprint.ViewMapping[0], prefab);
            SetField(setup, "_blueprints", new[] { blueprint });
            bool publish = false;
            setup.Track(Source(() => publish ? new[] { "a" } : new string[0]));
            StopSetupWhenEnabled.Target = setup;
            publish = true;
            _realm.Update();
            Assert.That(setup.Anchor, Is.Null);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(_realm.ContainsAnchor("default"), Is.False);
        }

        private sealed class StopSetupWhenEnabled : MonoBehaviour
        {
            internal static SceneSetup Target;

            private void OnEnable()
            {
                if (Target != null)
                {
                    Target.enabled = false;
                }
            }
        }

        /// <summary>
        /// Another component can begin tracking during the object's enable callbacks.
        /// </summary>
        [Test]
        public void Setup_CanStartFromAnotherComponentsOnEnable()
        {
            GameObject owner = new GameObject("early bootstrap");
            _objects.Add(owner);
            owner.SetActive(false);
            owner.AddComponent<EarlyBootstrap>();
            SceneSetup setup = owner.AddComponent<SceneSetup>();
            owner.SetActive(true);
            Assert.That(setup.Anchor, Is.Not.Null);
            Assert.That(_realm.Query().Count, Is.EqualTo(1));
        }

        [DefaultExecutionOrder(-100)]
        private sealed class EarlyBootstrap : MonoBehaviour
        {
            private void OnEnable()
            {
                GetComponent<SceneSetup>().Track(Source(() => new[] { "a" }));
            }
        }

        private static IEnumerable<string> BrokenSnapshot()
        {
            yield return "a";
            throw new InvalidOperationException("snapshot failed");
        }

        private static PollingPresenceSource<string, Probe> Source(Func<IEnumerable<string>> read)
        {
            return new PollingPresenceSource<string, Probe>(Population)
                .ReadFrom(read)
                .IdentifyBy(id => id)
                .Apply((item, ghost) =>
                {
                });
        }

        private SceneSetup CreateSetup()
        {
            GameObject target = new GameObject("scene setup");
            _objects.Add(target);
            return target.AddComponent<SceneSetup>();
        }

        private static void SetField(SceneSetup setup, string name, object value)
        {
            typeof(SceneSetup).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(setup, value);
        }

        private sealed class Probe : Ghost
        {
            internal int Value;
        }
    }
}
