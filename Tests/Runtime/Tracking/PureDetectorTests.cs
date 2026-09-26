using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies that polling and callback detectors forward SDK data through realm modules.
    /// </summary>
    public sealed class PureDetectorTests
    {
        private static readonly Kind TrackedKind = new Kind("tests.pure.detectors");
        private Realm _realm;
        private double _now;

        /// <summary>Creates an isolated realm and deterministic clock.</summary>
        [SetUp]
        public void SetUp()
        {
            _now = 10;
            _realm = new Realm(() => _now);
            _realm.RegisterPresenceInitializer<ProbeGhost>(TrackedKind, (presence, root) =>
            {
                if (!presence.HasCapability<IValueCapability>())
                {
                    return;
                }

                ValueModule module;
                if (!presence.TryGetModule(out module))
                {
                    presence.AddModule(new ValueModule());
                }
            });
        }

        /// <summary>Releases the realm and its tracked objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// A detector can track a silent Presence when no kind initializer consumes its SDK data.
        /// </summary>
        [Test]
        public void Polling_WithoutInitializerTracksSilentPresence()
        {
            Kind silentKind = new Kind("tests.pure.silent");
            PollingPresenceDetector<Reading> detector = new PollingPresenceDetector<Reading>(silentKind)
                .ReadFrom(() => new[] { new Reading("silent", "Silent SUV", Variant.None, 2) })
                .IdentifyBy(item => item.Id)
                .WithName(item => item.Label);

            _realm.GetOrCreateAnchor("silent", detector);
            Key key = new Key("silent", silentKind, "silent");
            Assert.That(_realm.TryGetPresence(key, out Presence presence), Is.True);
            Assert.That(presence.Name, Is.EqualTo("Silent SUV"));
            Assert.That(presence.Root, Is.TypeOf<DefaultGhost>());
            Assert.That(presence.Modules, Is.Empty);
            Assert.That(_realm.Query().Single(), Is.SameAs(presence.Root));
            Assert.That(_realm.Manifest(presence, DetailLevel.Full), Is.Null);
        }
        /// <summary>
        /// A full snapshot creates and updates one Presence, then removes an omitted ID.
        /// </summary>
        [Test]
        public void Polling_ReportsDataAndRemovesOmittedPresence()
        {
            List<Reading> items = new List<Reading>
            {
                new Reading("one", "First SUV", new Variant("model-a"), 3)
            };
            PollingPresenceDetector<Reading> detector = new PollingPresenceDetector<Reading>(TrackedKind)
                .ReadFrom(() => items)
                .IdentifyBy(item => item.Id)
                .WithName(item => item.Label)
                .WithVariant(item => item.Appearance)
                .WithCapabilities(item => new[] { typeof(IValueCapability) });

            _realm.GetOrCreateAnchor("poll", detector);
            Key key = new Key("poll", TrackedKind, "one");
            Assert.That(_realm.TryGetPresence(key, out Presence first), Is.True);
            ProbeGhost root = (ProbeGhost)first.Root;
            Assert.That(first.Name, Is.EqualTo("First SUV"));
            Assert.That(first.HasCapability<IValueCapability>(), Is.True);
            Assert.That(first.Modules.Count, Is.EqualTo(1));
            Assert.That(root.Value, Is.EqualTo(3));
            Assert.That(_realm.Query().Single(), Is.SameAs(root));

            items[0] = new Reading("one", "Updated SUV", new Variant("model-b"), 8);
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out Presence updated), Is.True);
            Assert.That(updated, Is.SameAs(first));
            Assert.That(updated.Root, Is.SameAs(root));
            Assert.That(updated.Name, Is.EqualTo("Updated SUV"));
            Assert.That(updated.Variant, Is.EqualTo(new Variant("model-b")));
            Assert.That(updated.Modules.Count, Is.EqualTo(1));
            Assert.That(root.Value, Is.EqualTo(8));

            items.Clear();
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out updated), Is.False);
            Assert.That(first.IsRemoved, Is.True);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Queued callbacks preserve a Presence, hide it on removal, and reuse it within grace.
        /// </summary>
        [Test]
        public void Callback_ReportsDataAndHonorsDisappearanceGrace()
        {
            Action<Reading> publish = null;
            Action<string> disappear = null;
            CallbackPresenceDetector<Reading> detector = new CallbackPresenceDetector<Reading>(TrackedKind)
                .IdentifyBy(item => item.Id)
                .WithName(item => item.Label)
                .WithVariant(item => item.Appearance)
                .WithCapabilities(item => new[] { typeof(IValueCapability) })
                .Listen((onPublish, onDisappear) =>
                {
                    publish = onPublish;
                    disappear = onDisappear;
                    return () =>
                    {
                        publish = null;
                        disappear = null;
                    };
                });
            detector.DisappearanceGracePeriod = TimeSpan.FromSeconds(2);
            _realm.GetOrCreateAnchor("callback", detector);
            Key key = new Key("callback", TrackedKind, "one");

            publish(new Reading("one", "First SUV", new Variant("model-a"), 4));
            Assert.That(_realm.TryGetPresence(key, out Presence first), Is.False);
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out first), Is.True);
            ProbeGhost root = (ProbeGhost)first.Root;
            Assert.That(first.HasCapability<IValueCapability>(), Is.True);
            Assert.That(root.Value, Is.EqualTo(4));
            Assert.That(first.IsAvailable, Is.True);

            publish(new Reading("one", "Renamed SUV", new Variant("model-b"), 9));
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out Presence updated), Is.True);
            Assert.That(updated, Is.SameAs(first));
            Assert.That(updated.Root, Is.SameAs(root));
            Assert.That(updated.Name, Is.EqualTo("Renamed SUV"));
            Assert.That(root.Value, Is.EqualTo(9));

            disappear("one");
            _realm.Update();
            Assert.That(first.IsAvailable, Is.False);
            Assert.That(first.IsRemoved, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
            Assert.That(_realm.TryGetPresence(key, out updated), Is.True);

            _now = 11;
            publish(new Reading("one", "Returned SUV", new Variant("model-b"), 12));
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out updated), Is.True);
            Assert.That(updated, Is.SameAs(first));
            Assert.That(updated.Root, Is.SameAs(root));
            Assert.That(updated.IsAvailable, Is.True);
            Assert.That(root.Value, Is.EqualTo(12));

            disappear("one");
            _realm.Update();
            _now = 13;
            _realm.Update();
            Assert.That(_realm.TryGetPresence(key, out updated), Is.False);
            Assert.That(first.IsRemoved, Is.True);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        private interface IValueCapability
        {
        }

        private sealed class Reading
        {
            internal Reading(string id, string label, Variant appearance, int value)
            {
                Id = id;
                Label = label;
                Appearance = appearance;
                Value = value;
            }

            internal readonly string Id;
            internal readonly string Label;
            internal readonly Variant Appearance;
            internal readonly int Value;
        }

        private sealed class ProbeGhost : Ghost
        {
            internal int Value { get; set; }
        }

        private sealed class ValueModule : EntityModule<Reading>
        {
            /// <inheritdoc />
            public override void Apply(Reading data)
            {
                ((ProbeGhost)Presence.Root).Value = data.Value;
            }
        }
    }
}


