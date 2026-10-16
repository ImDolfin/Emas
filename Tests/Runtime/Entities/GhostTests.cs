using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies how consumers resolve application contracts from a published ghost.
    /// </summary>
    public sealed class GhostTests
    {
        private interface IPart
        {
        }

        private sealed class FirstPart : MonoBehaviour, IPart
        {
        }

        private sealed class SecondPart : MonoBehaviour, IPart
        {
        }

        private sealed class Probe : PresenceDetector
        {
            internal Ghost Publish(string id)
            {
                return GetOrCreate<Ghost>(id, new Kind("vehicles.car"));
            }
        }

        /// <summary>
        /// Ghost ownership distinguishes identical keys in separate realms and survives scene reparenting;
        /// consumers can reach the current reference frame until the owning realm removes the root.
        /// </summary>
        [Test]
        public void Anchor_RetainsOwningRealmAfterReparenting()
        {
            using (Realm realm = new Realm())
            using (Realm otherRealm = new Realm())
            {
                realm.ReferenceFrame = new ReferenceFrame();
                otherRealm.ReferenceFrame = new ReferenceFrame();
                Probe source = new Probe();
                Anchor anchor = realm.GetOrCreateAnchor("simulation", source);
                Anchor otherAnchor = otherRealm.GetOrCreateAnchor("simulation");
                Ghost concrete = source.Publish("42");
                Ghost other = otherRealm.Prepare<Ghost>("simulation", concrete.Key.Kind, "42");
                IGhost ghost = concrete;
                Assert.That(ghost.Key, Is.EqualTo(other.Key));
                Assert.That(concrete.Anchor, Is.SameAs(anchor));
                Assert.That(ghost.Anchor.Realm.ReferenceFrame, Is.SameAs(realm.ReferenceFrame));

                concrete.enabled = false;
                concrete.transform.SetParent(otherAnchor.Transform, false);
                realm.Update();
                Assert.That(ghost.Anchor, Is.SameAs(anchor));
                Assert.That(ghost.Anchor.Realm, Is.SameAs(realm));
                Assert.That(other.Anchor, Is.SameAs(otherAnchor));
                ReferenceFrame replacement = new ReferenceFrame();
                realm.ReferenceFrame = replacement;
                Assert.That(ghost.Anchor.Realm.ReferenceFrame, Is.SameAs(replacement));

                realm.Dispose();
                Assert.That(ghost.Anchor, Is.Null);
                Assert.That(other.Anchor, Is.SameAs(otherAnchor));
            }
        }

        /// <summary>
        /// Lookup refuses an ambiguous contract, required access identifies both matching components, and removal resolves the remaining provider.
        /// This prevents prefab composition from silently selecting the wrong application component.
        /// </summary>
        [Test]
        public void TryGet_RejectsAmbiguousProvidersAndResolvesAfterRemoval()
        {
            using (Realm realm = new Realm())
            {
                Probe source = new Probe();
                realm.GetOrCreateAnchor("simulation", source);
                Ghost ghost = source.Publish("42");
                FirstPart first = ghost.gameObject.AddComponent<FirstPart>();
                SecondPart second = ghost.gameObject.AddComponent<SecondPart>();
                realm.Update();

                LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(ghost.Key.ToString()) + ".*" + Regex.Escape(typeof(IPart).FullName)));
                Assert.That(ghost.TryGet<IPart>(out IPart part), Is.False);
                Assert.That(part, Is.Null);
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => ghost.GetRequired<IPart>());
                Assert.That(error.Message, Does.Contain("found 2 matching root components")
                    .And.Contain(typeof(FirstPart).FullName).And.Contain(typeof(SecondPart).FullName)
                    .And.Contain("instance " + first.GetInstanceID()).And.Contain("instance " + second.GetInstanceID()));

                UnityEngine.Object.DestroyImmediate(second);
                Assert.That(ghost.TryGet<IPart>(out part), Is.True);
                Assert.That(part, Is.SameAs(first));
            }
        }

        /// <summary>
        /// Required lookup searches the Ghost root, directs missing composition to the Kind's blueprint and Ghost Prefab field, and resolves a disabled root provider.
        /// </summary>
        [Test]
        public void GetRequired_ReturnsProviderOrIdentifiesMissingContract()
        {
            using (Realm realm = new Realm())
            {
                Probe source = new Probe();
                Anchor anchor = realm.GetOrCreateAnchor("simulation", source);
                anchor.Transform.gameObject.AddComponent<FirstPart>();
                Ghost concrete = source.Publish("43");
                GameObject child = new GameObject("child provider");
                child.transform.SetParent(concrete.transform, false);
                child.AddComponent<FirstPart>();
                realm.Update();
                IGhost ghost = concrete;
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => ghost.GetRequired<IPart>());
                Assert.That(error.Message, Does.Contain(ghost.Key.ToString()).And.Contain(typeof(IPart).FullName)
                    .And.Contain("[" + typeof(IPart).Assembly.GetName().Name + "]")
                    .And.Contain("found 0 matching root components").And.Contain("GameObject '43'")
                    .And.Contain(typeof(Ghost).FullName).And.Contain("Manifestation Blueprint for Kind 'vehicles.car'")
                    .And.Contain("Ghost Prefab field").And.Contain("Ghost Prefab is unassigned")
                    .And.Contain("Anchor, parents, children or views"));

                FirstPart first = concrete.gameObject.AddComponent<FirstPart>();
                first.enabled = false;
                Assert.That(ghost.GetRequired<IPart>(), Is.SameAs(first));
            }
        }

        /// <summary>
        /// Consumers can retrieve disabled root traits directly and retain immutable membership snapshots through
        /// component changes. Required trait access rejects missing or ambiguous matches and recovers after removal.
        /// Each trait resolves its own root Ghost while inactive; child traits do not participate in root lookup.
        /// </summary>
        [Test]
        public void Traits_ExposeRootTraitsAsReadOnlyMembershipSnapshots()
        {
            using (Realm realm = new Realm())
            {
                Probe source = new Probe();
                realm.GetOrCreateAnchor("simulation", source);
                Ghost concrete = source.Publish("traits");
                IGhost ghost = concrete;
                IReadOnlyList<Trait> empty = ghost.Traits;
                Assert.That(empty, Is.Empty);

                PipelinePositionTrait position = concrete.gameObject.AddComponent<PipelinePositionTrait>();
                position.enabled = false;
                Assert.That(concrete.gameObject.activeSelf, Is.False);
                Assert.That(position.Ghost, Is.SameAs(concrete));
                GameObject child = new GameObject("view child");
                child.transform.SetParent(concrete.transform, false);
                PipelineArticulationTrait childTrait = child.AddComponent<PipelineArticulationTrait>();
                Assert.That(childTrait.Ghost, Is.Null);
                InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => ghost.Trait<PipelineArticulationTrait>());
                Assert.That(missing.Message, Does.Contain(ghost.Key.ToString()).And.Contain("found 0 matching root components"));
                Ghost childGhost = child.AddComponent<Ghost>();
                Assert.That(childGhost.Anchor, Is.Null);
                Assert.That(childTrait.Ghost, Is.SameAs(childGhost));
                UnityEngine.Object.DestroyImmediate(childGhost);
                Assert.That(childTrait.Ghost, Is.Null);
                childGhost = child.AddComponent<Ghost>();
                Assert.That(childTrait.Ghost, Is.SameAs(childGhost));
                IReadOnlyList<Trait> original = ghost.Traits;
                Assert.That(original, Is.EqualTo(new Trait[] { position }));
                Assert.That(empty, Is.Empty);
                Assert.That(ghost.Trait<PipelinePositionTrait>(), Is.SameAs(position));
                Assert.That(concrete.Trait<PipelinePositionTrait>().enabled, Is.False);
                Assert.Throws<NotSupportedException>(() => ((IList<Trait>)original).Clear());

                PipelineActionTrait action = concrete.gameObject.AddComponent<PipelineActionTrait>();
                Assert.That(action.Ghost, Is.SameAs(concrete));
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { position, action }));
                Assert.That(original, Is.EqualTo(new Trait[] { position }));

                UnityEngine.Object.DestroyImmediate(position);
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { action }));
                Assert.That(original.Count, Is.EqualTo(1));
                Assert.That(ghost.TryGet<PipelinePositionTrait>(out PipelinePositionTrait removed), Is.False);
                Assert.That(removed, Is.Null);
                Assert.Throws<InvalidOperationException>(() => ghost.Trait<PipelinePositionTrait>());

                PipelineActionTrait duplicate = concrete.gameObject.AddComponent<PipelineActionTrait>();
                LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(ghost.Key.ToString()) + ".*" + Regex.Escape(typeof(PipelineActionTrait).FullName)));
                InvalidOperationException ambiguous = Assert.Throws<InvalidOperationException>(() => ghost.Trait<PipelineActionTrait>());
                Assert.That(ambiguous.Message, Does.Contain(ghost.Key.ToString()).And.Contain("found 2 matching root components")
                    .And.Contain(typeof(PipelineActionTrait).FullName));
                UnityEngine.Object.DestroyImmediate(duplicate);
                Assert.That(ghost.Trait<PipelineActionTrait>(), Is.SameAs(action));
            }
        }
    }
}
