using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies exact identity lookup independently of query availability.
    /// </summary>
    public sealed class LookupTests
    {
        private static readonly Kind Kind = new Kind("lookup");
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
        /// Releases tracked objects.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Missing and incomplete identities return null without creating tracking state.
        /// </summary>
        [Test]
        public void MissingKeys_ReturnFalseWithoutSideEffects()
        {
            Key[] keys =
            {
                default(Key),
                new Key(null, Kind, "one"),
                new Key("anchor", default(Kind), "one"),
                new Key("anchor", Kind, null),
                new Key("anchor", Kind, "missing")
            };
            foreach (Key key in keys)
            {
                IGhost ghost;
                Assert.That(_realm.TryGetGhost(key, out ghost), Is.False);
                Assert.That(ghost, Is.Null);
            }

            Assert.That(_realm.Anchors, Is.Empty);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Every key component is exact and case-sensitive, even when display names match.
        /// </summary>
        [Test]
        public void Lookup_UsesWholeIdentity()
        {
            Probe source = new Probe();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost expected = source.Publish("one");
            source.Publish("One");
            _realm.GetOrCreateAnchor("Anchor");
            _realm.Prepare<TestGhost>("Anchor", Kind, "one");
            _realm.Prepare<TestGhost>("anchor", new Kind("Lookup"), "one");
            _realm.Update();

            IGhost found;
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "one"), out found), Is.True);
            Assert.That(found, Is.SameAs(expected));
            Assert.That(found.IsAvailable, Is.True);
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "ONE"), out found), Is.False);
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// Prepared roots are discoverable without activating or publishing them.
        /// </summary>
        [Test]
        public void PreparedGhost_RemainsUnavailable()
        {
            _realm.GetOrCreateAnchor("anchor");
            TestGhost prepared = _realm.Prepare<TestGhost>("anchor", Kind, "one");
            IGhost found;
            Assert.That(_realm.TryGetGhost(prepared.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(prepared));
            Assert.That(found.IsAvailable, Is.False);
            Assert.That(prepared.gameObject.activeSelf, Is.False);
            Assert.That(_realm.Query().Count, Is.Zero);
        }

        /// <summary>
        /// Failed sources retain a discoverable identity through restart and publication.
        /// </summary>
        [Test]
        public void FailureAndRestart_RetainLookupIdentity()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            TestGhost expected = source.Publish("one");
            _realm.Update();
            source.Fail = true;
            ExpectedErrors.Verify(_realm.Update, "lookup update failed");
            IGhost found;
            Assert.That(_realm.TryGetGhost(expected.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(expected));
            Assert.That(found.IsAvailable, Is.False);
            source.Fail = false;
            anchor.RestartSource(source);
            Assert.That(_realm.TryGetGhost(expected.Key, out found), Is.True);
            Assert.That(found.IsAvailable, Is.False);
            source.Publish("one");
            _realm.Update();
            Assert.That(_realm.TryGetGhost(expected.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(expected));
            Assert.That(found.IsAvailable, Is.True);
        }

        /// <summary>
        /// Compatible replacement preserves lookup while awaiting new data.
        /// </summary>
        [Test]
        public void Replacement_RetainsLookupIdentity()
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            TestGhost expected = source.Publish("one");
            _realm.Update();
            Probe replacement = new Probe();
            anchor.ReplaceSource(source, replacement);
            IGhost found;
            Assert.That(_realm.TryGetGhost(expected.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(expected));
            Assert.That(found.IsAvailable, Is.False);
            replacement.Publish("one");
            _realm.Update();
            Assert.That(_realm.TryGetGhost(expected.Key, out found), Is.True);
            Assert.That(found, Is.SameAs(expected));
            Assert.That(found.IsAvailable, Is.True);
        }

        /// <summary>
        /// Removal stops lookup immediately, before Unity destroys the detached object.
        /// </summary>
        [TestCase("entity")]
        [TestCase("source")]
        [TestCase("anchor")]
        [TestCase("realm")]
        public void Removal_ReturnsFalseImmediately(string removal)
        {
            Probe source = new Probe();
            Anchor anchor = _realm.GetOrCreateAnchor("anchor", source);
            TestGhost ghost = source.Publish("one");
            Key key = ghost.Key;
            _realm.Update();
            switch (removal)
            {
                case "entity":
                    source.Delete("one");
                    break;
                case "source":
                    anchor.RemoveSource(source);
                    break;
                case "anchor":
                    anchor.Dispose();
                    break;
                case "realm":
                    _realm.Dispose();
                    break;
            }

            IGhost found = ghost;
            Assert.That(_realm.TryGetGhost(key, out found), Is.False);
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// Destroyed Unity roots are not returned as live interface references.
        /// </summary>
        [Test]
        public void DestroyedGhost_ReturnsFalse()
        {
            _realm.GetOrCreateAnchor("anchor");
            TestGhost prepared = _realm.Prepare<TestGhost>("anchor", Kind, "one");
            Key key = prepared.Key;
            UnityEngine.Object.DestroyImmediate(prepared.gameObject);
            IGhost found;
            Assert.That(_realm.TryGetGhost(key, out found), Is.False);
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// Equal keys in separate realms resolve only their respective roots.
        /// </summary>
        [Test]
        public void Lookup_StaysWithinItsRealm()
        {
            using (Realm other = new Realm())
            {
                _realm.GetOrCreateAnchor("anchor");
                other.GetOrCreateAnchor("anchor");
                TestGhost first = _realm.Prepare<TestGhost>("anchor", Kind, "one");
                TestGhost second = other.Prepare<TestGhost>("anchor", Kind, "one");
                IGhost found;
                Assert.That(_realm.TryGetGhost(second.Key, out found), Is.True);
                Assert.That(found, Is.SameAs(first));
                Assert.That(other.TryGetGhost(first.Key, out found), Is.True);
                Assert.That(found, Is.SameAs(second));
            }
        }

        private sealed class TestGhost : Ghost
        {
        }

        private sealed class Probe : PresenceSource
        {
            internal bool Fail;

            internal TestGhost Publish(string id)
            {
                return GetOrCreate<TestGhost>(id, Kind, null, "Shared name");
            }

            internal void Delete(string id)
            {
                Remove(Kind, id);
            }

            /// <inheritdoc />
            protected override void OnUpdate()
            {
                if (Fail)
                {
                    throw new InvalidOperationException("lookup update failed");
                }
            }
        }
    }
}
