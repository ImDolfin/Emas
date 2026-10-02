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
        /// Consumers can enumerate disabled root modules, resolve them by type, and retain an immutable membership
        /// snapshot while modules are added or removed. Each module resolves its own root Ghost even while inactive;
        /// a view's child modules do not participate or borrow the parent's Ghost.
        /// </summary>
        [Test]
        public void Modules_ExposeRootModulesAsReadOnlyMembershipSnapshots()
        {
            using (Realm realm = new Realm())
            {
                Probe source = new Probe();
                realm.GetOrCreateAnchor("simulation", source);
                Ghost concrete = source.Publish("modules");
                IGhost ghost = concrete;
                IReadOnlyList<EntityModule> empty = ghost.Modules;
                Assert.That(empty, Is.Empty);

                PipelinePositionModule position = concrete.gameObject.AddComponent<PipelinePositionModule>();
                position.enabled = false;
                Assert.That(concrete.gameObject.activeSelf, Is.False);
                Assert.That(position.Ghost, Is.SameAs(concrete));
                GameObject child = new GameObject("view child");
                child.transform.SetParent(concrete.transform, false);
                PipelineArticulationModule childModule = child.AddComponent<PipelineArticulationModule>();
                Assert.That(childModule.Ghost, Is.Null);
                Ghost childGhost = child.AddComponent<Ghost>();
                Assert.That(childModule.Ghost, Is.SameAs(childGhost));
                UnityEngine.Object.DestroyImmediate(childGhost);
                Assert.That(childModule.Ghost, Is.Null);
                childGhost = child.AddComponent<Ghost>();
                Assert.That(childModule.Ghost, Is.SameAs(childGhost));
                IReadOnlyList<EntityModule> original = ghost.Modules;
                Assert.That(original, Is.EqualTo(new EntityModule[] { position }));
                Assert.That(empty, Is.Empty);
                Assert.That(ghost.GetRequired<PipelinePositionModule>(), Is.SameAs(position));
                Assert.Throws<NotSupportedException>(() => ((IList<EntityModule>)original).Clear());

                PipelineActionModule action = concrete.gameObject.AddComponent<PipelineActionModule>();
                Assert.That(action.Ghost, Is.SameAs(concrete));
                Assert.That(ghost.Modules, Is.EqualTo(new EntityModule[] { position, action }));
                Assert.That(original, Is.EqualTo(new EntityModule[] { position }));

                UnityEngine.Object.DestroyImmediate(position);
                Assert.That(ghost.Modules, Is.EqualTo(new EntityModule[] { action }));
                Assert.That(original.Count, Is.EqualTo(1));
                Assert.That(ghost.TryGet<PipelinePositionModule>(out PipelinePositionModule removed), Is.False);
                Assert.That(removed, Is.Null);
            }
        }

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
    }
}
