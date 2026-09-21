using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Verifies the shared context entry point and its Unity update integration.</summary>
    public sealed class DefaultContextTests
    {
        /// <summary>Repeated access returns the live shared context.</summary>
        [Test]
        public void Default_ReusesLiveContext()
        {
            Assert.That(Context.Default, Is.SameAs(Context.Default));
        }

        /// <summary>A disposed shared context is replaced on next access.</summary>
        [Test]
        public void Default_RecreatesDisposedContext()
        {
            var previous = Context.Default;
            previous.Dispose();
            var current = Context.Default;
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(current.IsDisposed, Is.False);
        }

        /// <summary>Unity advances a coordinator without a manual context update.</summary>
        [UnityTest]
        public IEnumerator Default_UpdatesAutomatically()
        {
            var context = Context.Default;
            var source = new CountingSource();
            var origin = context.CreateOriginFor("tests.default", source);
            try
            {
                yield return null;
                yield return null;
                Assert.That(source.Updates, Is.GreaterThan(0));
                Assert.That(Context.Default, Is.SameAs(context));
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
