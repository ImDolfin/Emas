using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises complete-snapshot polling and direct realm tracking against Unity.
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
        /// An incomplete polling source explains its required setup and can be attached once configured.
        /// </summary>
        [Test]
        public void Polling_ConfigurationCanBeCompletedAfterRejectedAttachment()
        {
            PollingPresenceDetector<string, Probe> source = new PollingPresenceDetector<string, Probe>(Population);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                _realm.GetOrCreateAnchor("poll", source));
            Assert.That(error.Message, Does.Contain("ReadFrom").And.Contain("IdentifyBy").And.Contain("Apply"));
            Assert.That(_realm.Anchors, Is.Empty);
            source.ReadFrom(() => new[] { "a" }).IdentifyBy(id => id)
                .Apply((item, ghost) => ghost.Value = 3);
            _realm.GetOrCreateAnchor("poll", source);
            Assert.That(((Probe)_realm.Query().Single()).Value, Is.EqualTo(3));
        }

        /// <summary>
        /// Polling configuration remains fixed until detachment and may then be reused with new mapping.
        /// </summary>
        [Test]
        public void Polling_ConfigurationIsLockedUntilDetached()
        {
            PollingPresenceDetector<string, Probe> source = Source(() => new[] { "a" });
            Anchor anchor = _realm.GetOrCreateAnchor("poll", source);

            Assert.Throws<InvalidOperationException>(() => source.ReadFrom(() => new[] { "b" }));
            Assert.Throws<InvalidOperationException>(() => source.IdentifyBy(id => "changed"));
            Assert.Throws<InvalidOperationException>(() => source.Apply((item, ghost) => ghost.Value = 99));
            Assert.Throws<InvalidOperationException>(() => source.WithVariant(item => new Variant("changed")));
            Assert.Throws<InvalidOperationException>(() => source.PollEvery(TimeSpan.FromSeconds(1)));
            anchor.RemoveDetector(source);
            source.ReadFrom(() => new[] { "b" }).IdentifyBy(id => id)
                .Apply((item, ghost) => ghost.Value = 3).WithVariant(item => new Variant("new")).PollEvery(TimeSpan.Zero);
            anchor.AddDetector(source);
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
            _realm.GetOrCreateAnchor("poll", new PollingPresenceDetector<string, Probe>(Population)
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
            _realm.GetOrCreateAnchor("poll", new PollingPresenceDetector<string, Probe>(Population)
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
            PollingPresenceDetector<string, Probe> first = Source(() => new[] { "a", "b" });
            Anchor anchor = _realm.GetOrCreateAnchor("poll", first);
            IGhost retained = _realm.Query("a").Single();
            IGhost removed = _realm.Query("b").Single();
            anchor.ReplaceDetector(first, Source(() => new[] { "a" }));
            Assert.That(_realm.Query().Single(), Is.SameAs(retained));
            Assert.That(removed.IsAvailable, Is.False);
        }

        /// <summary>
        /// Malformed polling results stop the source and remove its population.
        /// </summary>
        [TestCase("null", TestName = "Polling_RejectsMissingSnapshot")]
        [TestCase("duplicate", TestName = "Polling_RejectsDuplicateEntityIds")]
        public void Polling_InvalidSnapshotStopsAndRemovesPopulation(string failure)
        {
            bool fail = false;
            Func<IEnumerable<string>> read = () => !fail ? new[] { "a", "b" } :
                failure == "null" ? null : new[] { "a", "a" };
            PollingPresenceDetector<string, Probe> source = Source(read);
            Anchor anchor = _realm.GetOrCreateAnchor("poll", source);
            IGhost retained = _realm.Query("a").Single();
            fail = true;
            string expected = failure == "null" ? "must return a complete snapshot, not null"
                : "contains an empty or duplicate entity ID";
            ExpectedErrors.Verify(_realm.Update, expected);
            Assert.That(retained.IsAvailable, Is.False);
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            IGhost found;
            Assert.That(_realm.TryGetGhost(retained.Key, out found), Is.False);
            Assert.That(_realm.TryGetGhost(new Key("poll", Population, "b"), out found), Is.False);
            anchor.ReplaceDetector(source, Source(() => new[] { "a" }));
            Assert.That(_realm.Query().Single(), Is.Not.SameAs(retained));
            Assert.That(_realm.Query().Single().Key, Is.EqualTo(retained.Key));
        }

        /// <summary>
        /// Reusing an anchor preserves its frame and starts only newly attached sources.
        /// </summary>
        [Test]
        public void GetOrCreateAnchor_ReusesAnchorAndStartsOnlyNewSources()
        {
            int reads = 0;
            PollingPresenceDetector<string, Probe> first = Source(() =>
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

        private static PollingPresenceDetector<string, Probe> Source(Func<IEnumerable<string>> read)
        {
            return new PollingPresenceDetector<string, Probe>(Population)
                .ReadFrom(read)
                .IdentifyBy(id => id)
                .Apply((item, ghost) =>
                {
                });
        }

        private sealed class Probe : Ghost
        {
            internal int Value;
        }
    }
}
