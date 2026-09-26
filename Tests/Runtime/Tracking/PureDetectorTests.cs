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

        /// <summary>Creates a public realm configured to consume the SDK payload.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
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
        /// SDK callbacks apply data on the next update, retain identity across changes, and unsubscribe on detachment.
        /// </summary>
        [Test]
        public void Callback_QueuesDataChangesAndReleasesSubscription()
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
            Anchor anchor = _realm.GetOrCreateAnchor("callback", detector);
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
            Assert.That(first.IsAvailable, Is.True);
            _realm.Update();
            Assert.That(first.IsRemoved, Is.True);
            Assert.That(_realm.Query().Count, Is.Zero);
            anchor.RemoveDetector(detector);
            Assert.That(publish, Is.Null);
            Assert.That(disappear, Is.Null);
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
