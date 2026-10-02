using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies exact identity lookup and searchable root membership independently of query availability.
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
        /// Case-sensitive anchor, kind and source IDs keep separate roots discoverable even when display names match.
        /// </summary>
        [Test]
        public void Lookup_UsesWholeIdentity()
        {
            Probe source = new Probe();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost expected = source.Publish("one");
            TestGhost otherId = source.Publish("One");
            TestGhost otherKind = source.Publish("one", new Kind("Lookup"));
            Probe otherSource = new Probe();
            _realm.GetOrCreateAnchor("Anchor", otherSource);
            TestGhost otherAnchor = otherSource.Publish("one");
            _realm.Update();

            IGhost found;
            foreach (IGhost ghost in new IGhost[] { expected, otherId, otherKind, otherAnchor })
            {
                Key key = new Key(ghost.Key.AnchorId, ghost.Key.Kind, ghost.Key.EntityId);
                Assert.That(_realm.TryGetGhost(key, out found), Is.True);
                Assert.That(found, Is.SameAs(ghost));
                Assert.That(found.IsAvailable, Is.True);
            }

            Assert.That(_realm.Query().Count, Is.EqualTo(4));
            Assert.That(_realm.Query().OfKind(Kind).Count, Is.EqualTo(3));
            Assert.That(_realm.TryGetGhost(new Key("anchor", Kind, "ONE"), out found), Is.False);
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// An initializer can find a pending attachment parent by partial ID through a read-only membership snapshot.
        /// Snapshot enumeration stays stable across discovery, removal and disposal while new reads exclude removed or destroyed roots.
        /// </summary>
        [Test]
        public void Ghosts_LinqSearchFindsPendingParentDuringInitialization()
        {
            Probe source = new Probe();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost parent = source.Publish("vehicle-main-123");
            _realm.GetOrCreateAnchor("other");
            TestGhost otherAnchor = _realm.Prepare<TestGhost>("other", Kind, "vehicle-main-456");
            _realm.Prepare<TestGhost>("anchor", Kind, "vehicle-spare-789");
            IReadOnlyList<IGhost> original = _realm.Ghosts;
            Assert.That(original.Count, Is.EqualTo(3));
            Assert.Throws<NotSupportedException>(() => ((IList<IGhost>)original).Clear());

            Kind partKind = new Kind("lookup.part");
            Vector3 offset = new Vector3(1, 2, 3);
            _realm.RegisterPresenceInitializer<AttachedGhost>(partKind, (presence, root) =>
            {
                IGhost found = _realm.Ghosts.SingleOrDefault(ghost =>
                    ghost.Key.AnchorId == presence.Key.AnchorId
                    && ghost.Key.Kind == Kind
                    && ghost.Key.EntityId.Contains("vehicle-main-"));
                Assert.That(found, Is.SameAs(parent));
                Assert.That(found.IsAvailable, Is.False);
                Assert.That(_realm.Query().Count, Is.Zero);
                Assert.That(_realm.Ghosts.OfType<AttachedGhost>().Single(), Is.SameAs(root));
                root.GetRequired<Spatial>().Attach(found.Key, offset);
            });
            Presence part = source.PublishMetadata("part-1", partKind);
            _realm.Update();
            Assert.That(part.Root.GetRequired<Spatial>().AttachedTo, Is.EqualTo(parent.Key));
            Assert.That(part.Root.transform.position, Is.EqualTo(offset));
            Assert.That(_realm.Ghosts.Count, Is.EqualTo(4));
            Assert.That(original.Count, Is.EqualTo(3));

            source.Delete("vehicle-main-123");
            Assert.That(_realm.Ghosts.Contains(parent), Is.False);
            Assert.That(original.Contains(parent), Is.True);
            UnityEngine.Object.DestroyImmediate(otherAnchor.gameObject);
            Assert.That(_realm.Ghosts.Contains(otherAnchor), Is.False);
            Assert.That(_realm.Ghosts.Count, Is.EqualTo(2));

            _realm.Dispose();
            Assert.That(_realm.Ghosts, Is.Empty);
            Assert.That(original.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Explicit disappearance removes an identity from public lookup before Unity destroys its object.
        /// </summary>
        [Test]
        public void Removal_ReturnsFalseImmediately()
        {
            Probe source = new Probe();
            _realm.GetOrCreateAnchor("anchor", source);
            TestGhost ghost = source.Publish("one");
            Key key = ghost.Key;
            _realm.Update();
            source.Delete("one");
            IGhost found;
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
                Assert.That(_realm.Ghosts, Is.EqualTo(new IGhost[] { first }));
                Assert.That(other.Ghosts, Is.EqualTo(new IGhost[] { second }));
            }
        }

        private sealed class TestGhost : Ghost
        {
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class AttachedGhost : Ghost
        {
        }

        private sealed class Probe : PresenceDetector
        {
            internal TestGhost Publish(string id, Kind? kind = null)
            {
                return GetOrCreate<TestGhost>(id, kind ?? Kind, null, "Shared name");
            }

            internal Presence PublishMetadata(string id, Kind kind)
            {
                return Detect(id, kind, name: "Shared name");
            }

            internal void Delete(string id)
            {
                Disappear(Kind, id);
            }
        }
    }
}
