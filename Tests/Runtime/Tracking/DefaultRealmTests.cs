using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies the shared realm entry point and its Unity update integration.
    /// </summary>
    public sealed class DefaultRealmTests
    {
        /// <summary>
        /// A disposed shared realm is replaced on next access and accepts active detectors.
        /// </summary>
        [Test]
        public void Default_RecreatesDisposedRealm()
        {
            Realm previous = Realm.Default;
            previous.Dispose();
            Realm current = Realm.Default;
            Assert.That(current, Is.Not.SameAs(previous));
            CountingSource source = new CountingSource();
            using (Anchor anchor = current.GetOrCreateAnchor("tests.default.recreated", source))
            {
                Assert.That(source.IsAttached && source.IsActive, Is.True);
                current.Update();
                Assert.That(source.Updates, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Unity advances a source without a manual realm update.
        /// </summary>
        [UnityTest]
        public IEnumerator Default_UpdatesAutomatically()
        {
            Realm realm = Realm.Default;
            CountingSource source = new CountingSource();
            Anchor anchor = realm.GetOrCreateAnchor("tests.default", source);
            try
            {
                yield return null;
                yield return null;
                Assert.That(source.Updates, Is.GreaterThan(0));
                Assert.That(Realm.Default, Is.SameAs(realm));
            }
            finally
            {
                anchor.Dispose();
            }
        }

        private sealed class CountingSource : PresenceDetector
        {
            internal int Updates;

            protected override void OnUpdate()
            {
                Updates++;
            }
        }
    }
}
