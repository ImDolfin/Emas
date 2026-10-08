using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Verifies scene adapters share the code-based detector and trait contracts.</summary>
    public sealed class ComponentSetupTests
    {
        private static readonly Kind TestKind = new Kind("components.entity");
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private RealmSetup _setup;
        private SceneDetector _source;
        private SceneInitializer _initializer;

        /// <summary>Creates an inactive authored source beneath a running, configured realm.</summary>
        [SetUp]
        public void SetUp()
        {
            GameObject root = new GameObject("component realm");
            _objects.Add(root);
            root.SetActive(false);
            _setup = root.AddComponent<RealmSetup>();
            GameObject owner = new GameObject("component source");
            owner.SetActive(false);
            owner.transform.SetParent(root.transform, false);
            _source = owner.AddComponent<SceneDetector>();
            owner.AddComponent<AnchorSetup>();
            _initializer = owner.AddComponent<SceneInitializer>();
            root.SetActive(true);
            _setup.StartRealm();

            GameObject prefab = new GameObject("authored root");
            _objects.Add(prefab);
            prefab.SetActive(false);
            Ghost ghost = prefab.AddComponent<TextGhost>();
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            _objects.Add(blueprint);
            blueprint.Configure(TestKind, ghost, null, null);
            _setup.Realm.RegisterManifestationBlueprint(blueprint);
        }

        /// <summary>Releases realms, source components and authored roots.</summary>
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

        /// <summary>Local mapping sees bound Anchor/Realm context before availability, reads updated values, and reconnects the same root to a new source.</summary>
        [Test]
        public void Initializer_BindsAuthoredTraitsAndRebindsSource()
        {
            Assert.That(_initializer.Anchor, Is.Null);
            Assert.That(_initializer.Realm, Is.Null);
            Assert.That(_source.Anchor, Is.Null);
            Assert.That(_source.Realm, Is.Null);
            string observed = null;
            using (_setup.Realm.Query().OnAvailable(ghost => observed = ((Ghost)ghost).GetComponent<TextTrait>().Value))
            {
                _source.gameObject.SetActive(true);
                _setup.Realm.Update();
            }
            Assert.That(observed, Is.EqualTo("first"));
            Assert.That(_initializer.Anchor, Is.SameAs(_source.Anchor));
            Assert.That(_initializer.Realm, Is.SameAs(_setup.Realm));
            Assert.That(_source.Realm, Is.SameAs(_setup.Realm));
            Assert.That(_source.Detector.Anchor, Is.SameAs(_source.Anchor));
            Assert.That(_source.Detector.Realm, Is.SameAs(_setup.Realm));
            Presence presence = _source.Current;
            TextTrait trait = presence.Root.GetComponent<TextTrait>();
            _source.BeforeRead = () => _source.Source.Value = "updated";
            _setup.Realm.Update();
            Assert.That(trait.Value, Is.EqualTo("updated"));
            Assert.That(_initializer.Calls, Is.EqualTo(1));

            _source.BeforeRead = null;
            _source.Source = new Reading { Value = "replacement" };
            _source.Publish();
            _setup.Realm.Update();
            Assert.That(_source.Current, Is.SameAs(presence));
            Assert.That(_source.Current.Root.GetComponent<TextTrait>(), Is.SameAs(trait));
            Assert.That(trait.Value, Is.EqualTo("replacement"));
            Assert.That(_initializer.Calls, Is.EqualTo(2));
            Assert.That(presence.Source, Is.SameAs(_source.Source));
        }

        /// <summary>A local initializer overrides only its Anchor; plain detectors retain the Realm fallback.</summary>
        [Test]
        public void LocalMapping_DoesNotReplaceRealmRegistration()
        {
            _setup.Realm.RegisterPresenceInitializer<TextGhost>(TestKind,
                (presence, root) => root.GetComponent<TextTrait>().Bind(() => "realm mapping"));
            _source.gameObject.SetActive(true);
            _setup.Realm.GetOrCreateAnchor("code").AddDetector(new CodeDetector());
            Ghost code = (Ghost)_setup.Realm.Query().InAnchor("code").Single();
            Assert.That(code.GetComponent<TextTrait>().Value, Is.EqualTo("realm mapping"));
            Assert.That(_source.Current.Root.GetComponent<TextTrait>().Value, Is.EqualTo("first"));

            _source.gameObject.SetActive(false);
            Assert.That(_initializer.Anchor, Is.Null);
            Assert.That(_initializer.Realm, Is.Null);
            Assert.That(_source.Anchor, Is.Null);
            Assert.That(_source.Realm, Is.Null);
            _initializer.enabled = false;
            _source.gameObject.SetActive(true);
            Assert.That(_source.Current.Root.GetComponent<TextTrait>().Value, Is.EqualTo("realm mapping"));
            Assert.That(_initializer.Calls, Is.EqualTo(1));
            Assert.That(_initializer.Anchor, Is.Null);
            Assert.That(_initializer.Realm, Is.Null);
        }

        /// <summary>Restart retains bound context and identities; direct Anchor disposal releases initializer and detector context.</summary>
        [Test]
        public void Restart_UsesTheExistingDetectorLifecycle()
        {
            _source.gameObject.SetActive(true);
            Presence original = _source.Current;
            Action<Action> stale = _source.Captured;
            _source.Queue(() => _source.Source.Value = "stale queued work");
            _source.GetComponent<AnchorSetup>().Anchor.RestartDetector(_source.Detector);
            int called = 0;
            stale(() => called++);
            _source.Captured(() => called++);
            _source.Queue(() => called++);
            _setup.Realm.Update();
            Assert.That(called, Is.EqualTo(2));
            Assert.That(_source.Current, Is.SameAs(original));
            Assert.That(_source.Current.Root.GetComponent<TextTrait>().Value, Is.EqualTo("first"));
            Assert.That(_source.Starts, Is.EqualTo(2));
            Assert.That(_source.Stops, Is.EqualTo(1));
            Assert.That(_source.OwnedCount, Is.EqualTo(1));
            Assert.That(_source.Anchor, Is.SameAs(_source.GetComponent<AnchorSetup>().Anchor));
            Assert.That(_initializer.Anchor, Is.SameAs(_source.Anchor));
            Assert.That(_initializer.Realm, Is.SameAs(_setup.Realm));
            _source.Anchor.Dispose();
            Assert.That(_source.Anchor, Is.Null);
            Assert.That(_source.Realm, Is.Null);
            Assert.That(_initializer.Anchor, Is.Null);
            Assert.That(_initializer.Realm, Is.Null);
        }

        /// <summary>A disabled detector opts out of scene startup; enable, disable and destruction own its attachment.</summary>
        [Test]
        public void ComponentActivation_StartsAndReleasesItsAnchor()
        {
            _setup.StopRealm();
            _initializer.enabled = false;
            _source.enabled = false;
            _source.gameObject.SetActive(true);
            _setup.StartRealm();
            Assert.That(_setup.Realm.Anchors, Is.Empty);
            _source.enabled = true;
            Assert.That(_setup.Realm.Query().Count, Is.EqualTo(1));
            _source.enabled = false;
            Assert.That(_setup.Realm.Anchors, Is.Empty);
            Assert.That(_source.Stops, Is.EqualTo(1));
            _source.enabled = true;
            PresenceDetector detector = _source.Detector;
            UnityEngine.Object.DestroyImmediate(_source);
            Assert.That(detector.IsAttached, Is.False);
            Assert.That(_setup.Realm.Query().Count, Is.Zero);
        }

        /// <summary>Grace configuration on the underlying detector retains a disappearing entity until it returns.</summary>
        [Test]
        public void Disappear_UsesConfiguredGraceAndRebindsOnReturn()
        {
            _source.Detector.DisappearanceGracePeriod = TimeSpan.FromMinutes(1);
            _source.gameObject.SetActive(true);
            Presence original = _source.Current;
            _source.Remove();
            Assert.That(_setup.Realm.Query().Count, Is.Zero);
            Assert.That(original.Source, Is.Null);
            _source.Publish();
            _setup.Realm.Update();
            Assert.That(_source.Current, Is.SameAs(original));
            Assert.That(_source.Current.IsAvailable, Is.True);
            Assert.That(_initializer.Calls, Is.EqualTo(2));
        }

        /// <summary>Component update failures stop their registration and release its population once.</summary>
        [Test]
        public void UpdateFailure_StopsAndCanRestart()
        {
            _source.gameObject.SetActive(true);
            _source.BeforeRead = () => throw new InvalidOperationException("component update failed");
            LogAssert.Expect(LogType.Exception, new Regex("component update failed"));
            _setup.Realm.Update();
            Assert.That(_source.Detector.IsActive, Is.False);
            Assert.That(_source.Detector.LastError, Is.TypeOf<InvalidOperationException>());
            Assert.That(_setup.Realm.Query().Count, Is.Zero);
            Assert.That(_source.Stops, Is.EqualTo(1));
            _source.BeforeRead = null;
            _source.GetComponent<AnchorSetup>().Anchor.RestartDetector(_source.Detector);
            Assert.That(_source.Detector.LastError, Is.Null);
            Assert.That(_setup.Realm.Query().Count, Is.EqualTo(1));
        }

        /// <summary>Initializer failure during restart rolls back detection and still invokes detector cleanup.</summary>
        [Test]
        public void InitializerFailure_CleansUpTheAttachment()
        {
            _source.gameObject.SetActive(true);
            _initializer.Fail = true;
            Anchor anchor = _source.GetComponent<AnchorSetup>().Anchor;
            Assert.Throws<InvalidOperationException>(() => anchor.RestartDetector(_source.Detector));
            Assert.That(_source.Detector.IsActive, Is.False);
            Assert.That(_source.Detector.LastErrorContext, Does.Contain("Detect"));
            Assert.That(_setup.Realm.Query().Count, Is.Zero);
            Assert.That(_source.Stops, Is.EqualTo(2));
        }

        private sealed class Reading
        {
            internal string Value = "first";
        }

        private sealed class SceneDetector : PresenceDetectorComponent
        {
            internal Reading Source = new Reading();
            internal Presence Current;
            internal Action BeforeRead;
            internal Action<Action> Captured;
            internal int Starts;
            internal int Stops;
            internal int OwnedCount { get { return OwnedPresences.Count; } }

            protected override void OnStart()
            {
                Assert.That(Anchor, Is.Not.Null);
                Assert.That(Realm, Is.SameAs(Anchor.Realm));
                Starts++;
                Captured = CaptureDispatcher();
                Publish();
            }
            protected override void OnUpdate()
            {
                BeforeRead?.Invoke();
            }
            protected override void OnStop()
            {
                Assert.That(Anchor, Is.Not.Null);
                Assert.That(Realm, Is.SameAs(Anchor.Realm));
                Stops++;
            }
            internal void Publish()
            {
                Current = Detect("one", TestKind, source: Source);
            }
            internal void Remove()
            {
                Disappear(TestKind, "one");
            }
            internal void Queue(Action action)
            {
                Dispatch(action);
            }
        }

        private sealed class SceneInitializer : GhostInitializer
        {
            internal int Calls;
            internal bool Fail;

            protected override void Initialize(Presence presence, Ghost ghost)
            {
                Assert.That(Anchor, Is.Not.Null);
                Assert.That(Realm, Is.SameAs(Anchor.Realm));
                Assert.That(Anchor.Id, Is.EqualTo(presence.Key.AnchorId));
                Assert.That(Realm.Ghosts, Does.Contain(ghost));
                Calls++;
                if (Fail)
                {
                    throw new InvalidOperationException("mapping failed");
                }
                ghost.GetComponent<TextTrait>().Bind(() => ((Reading)presence.Source).Value);
            }
        }

        private sealed class CodeDetector : PresenceDetector
        {
            protected override void OnStart()
            {
                Detect("one", TestKind);
            }
        }
    }
}
