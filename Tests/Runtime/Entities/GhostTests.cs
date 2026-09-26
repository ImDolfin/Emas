using System;
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
                TestGhost ghost = source.Publish("42");
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
                TestGhost concrete = source.Publish("43");
                realm.Update();
                IGhost ghost = concrete;
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => ghost.GetRequired<IPart>());
                Assert.That(error.Message, Does.Contain(ghost.Key.ToString()).And.Contain(typeof(IPart).FullName));

                FirstPart first = concrete.gameObject.AddComponent<FirstPart>();
                Assert.That(ghost.GetRequired<IPart>(), Is.SameAs(first));
            }
        }

        private interface IPart
        {
        }

        private sealed class TestGhost : Ghost
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
            internal TestGhost Publish(string id)
            {
                return GetOrCreate<TestGhost>(id, new Kind("vehicles.car"));
            }
        }
    }
}
