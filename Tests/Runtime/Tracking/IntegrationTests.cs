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
        /// Invalid configuration inputs fail before a source is attached.
        /// </summary>
        [Test]
        public void Polling_ValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new PollingPresenceDetector<string, Probe>(default(Kind)));
            PollingPresenceDetector<string, Probe> source = new PollingPresenceDetector<string, Probe>(Population);
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
            PollingPresenceDetector<string, Probe> source = new PollingPresenceDetector<string, Probe>(Population);
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

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                _realm.GetOrCreateAnchor("default", source));
            Assert.That(error.Message, Is.EqualTo("Polling source is missing required steps: " + missing + ". Configure them before tracking."));
            Assert.That(reads, Is.Zero);
            Assert.That(_realm.ContainsAnchor("default"), Is.False);
            source.ReadFrom(() => new[] { "a" }).IdentifyBy(id => id).Apply((item, ghost) =>
            {
            });
            _realm.GetOrCreateAnchor("default", source);
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
            PollingPresenceDetector<string, Probe> source = Source(() => broken ? null : new[] { "a" });
            Anchor anchor = _realm.GetOrCreateAnchor("poll", source);
            if (fail)
            {
                broken = true;
                ExpectedErrors.Verify(_realm.Update, "must return a complete snapshot, not null");
            }

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
        [TestCase("null")]
        [TestCase("duplicate")]
        [TestCase("empty-id")]
        [TestCase("enumeration")]
        public void Polling_InvalidSnapshotStopsAndRemovesPopulation(string failure)
        {
            bool fail = false;
            Func<IEnumerable<string>> read = () => !fail ? new[] { "a", "b" } :
                failure == "null" ? null : failure == "duplicate" ? new[] { "a", "a" } :
                failure == "empty-id" ? new[] { "" } : BrokenSnapshot();
            PollingPresenceDetector<string, Probe> source = Source(read);
            Anchor anchor = _realm.GetOrCreateAnchor("poll", source);
            IGhost retained = _realm.Query("a").Single();
            fail = true;
            string expected = failure == "null" ? "must return a complete snapshot, not null"
                : failure == "enumeration" ? "snapshot failed" : "contains an empty or duplicate entity ID";
            ExpectedErrors.Verify(_realm.Update, expected);
            if (failure == "enumeration")
            {
                Assert.That(source.LastErrorContext, Does.Contain("ReadFrom").And.Not.Contain("entity '"));
            }

            Assert.That(_realm.GetOwnedGhosts(source), Is.Empty);
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
        /// A failed mapper removes the entire source population, including entities absent from the partial poll.
        /// </summary>
        [Test]
        public void Polling_MappingFailureRemovesEntirePopulation()
        {
            bool fail = false;
            PollingPresenceDetector<string, Probe> source = new PollingPresenceDetector<string, Probe>(Population)
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
            ExpectedErrors.Verify(_realm.Update, "mapper failed");
            Assert.That(_realm.GetOwnedGhosts(source), Is.Empty);
            Assert.That(_realm.Query().Count, Is.Zero);
            IGhost found;
            Assert.That(_realm.TryGetGhost(new Key("poll", Population, "a"), out found), Is.False);
            Assert.That(_realm.TryGetGhost(new Key("poll", Population, "b"), out found), Is.False);
        }

        /// <summary>
        /// A mapper can end its own registration without publishing remaining entries.
        /// </summary>
        [Test]
        public void Polling_CanRemoveAnchorDuringMapping()
        {
            bool remove = false;
            PollingPresenceDetector<string, Probe> source = new PollingPresenceDetector<string, Probe>(Population)
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

        private static IEnumerable<string> BrokenSnapshot()
        {
            yield return "a";
            throw new InvalidOperationException("snapshot failed");
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
