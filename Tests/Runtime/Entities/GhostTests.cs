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
        /// Lookup refuses an ambiguous contract and resolves the remaining provider after a component is removed.
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

                UnityEngine.Object.DestroyImmediate(second);
                Assert.That(ghost.TryGet<IPart>(out part), Is.True);
                Assert.That(part, Is.SameAs(first));
            }
        }

        /// <summary>
        /// Required lookup identifies a missing contract by ghost key, then returns the newly attached provider.
        /// Consumers can use the same contract access without knowing the concrete ghost type.
        /// </summary>
        [Test]
        public void GetRequired_ReturnsProviderOrIdentifiesMissingContract()
        {
            using (Realm realm = new Realm())
            {
                Probe source = new Probe();
                realm.GetOrCreateAnchor("simulation", source);
                Ghost concrete = source.Publish("43");
                realm.Update();
                IGhost ghost = concrete;
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => ghost.GetRequired<IPart>());
                Assert.That(error.Message, Does.Contain(ghost.Key.ToString()).And.Contain(typeof(IPart).FullName));

                FirstPart first = concrete.gameObject.AddComponent<FirstPart>();
                Assert.That(ghost.GetRequired<IPart>(), Is.SameAs(first));
            }
        }

        /// <summary>
        /// Consumers can enumerate disabled root modules, resolve them by type, and retain an immutable membership
        /// snapshot while modules are added or removed, without including a view's child modules.
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
                GameObject child = new GameObject("view child");
                child.transform.SetParent(concrete.transform, false);
                child.AddComponent<PipelineArticulationModule>();
                IReadOnlyList<EntityModule> original = ghost.Modules;
                Assert.That(original, Is.EqualTo(new EntityModule[] { position }));
                Assert.That(empty, Is.Empty);
                Assert.That(ghost.GetRequired<PipelinePositionModule>(), Is.SameAs(position));
                Assert.Throws<NotSupportedException>(() => ((IList<EntityModule>)original).Clear());

                PipelineActionModule action = concrete.gameObject.AddComponent<PipelineActionModule>();
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
