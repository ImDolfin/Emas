using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Verifies the shared realm entry point and its Unity update integration.</summary>
    public sealed class DefaultRealmTests
    {
        /// <summary>Repeated access returns the live shared realm.</summary>
        [Test]
        public void Default_ReusesLiveRealm()
        {
            Assert.That(Realm.Default, Is.SameAs(Realm.Default));
        }

        /// <summary>A disposed shared realm is replaced on next access.</summary>
        [Test]
        public void Default_RecreatesDisposedRealm()
        {
            var previous = Realm.Default;
            previous.Dispose();
            var current = Realm.Default;
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(current.IsDisposed, Is.False);
        }

        /// <summary>Unity advances a coordinator without a manual realm update.</summary>
        [UnityTest]
        public IEnumerator Default_UpdatesAutomatically()
        {
            var realm = Realm.Default;
            var source = new CountingSource();
            var origin = realm.CreateOriginFor("tests.default", source);
            try
            {
                yield return null;
                yield return null;
                Assert.That(source.Updates, Is.GreaterThan(0));
                Assert.That(Realm.Default, Is.SameAs(realm));
            }
            finally
            {
                origin.Dispose();
            }
        }

        private sealed class CountingSource : Coordinator
        {
            internal int Updates;

            protected override void OnUpdate()
            {
                Updates++;
            }
        }
    }
}
