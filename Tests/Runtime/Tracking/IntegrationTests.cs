using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Exercises anchor reuse and detector startup against Unity scene objects.
    /// </summary>
    public sealed class IntegrationTests
    {
        private static readonly Kind Population = new Kind("tests.anchor.integration");
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
        /// Reusing an anchor preserves its frame and starts only newly attached sources.
        /// </summary>
        [Test]
        public void GetOrCreateAnchor_ReusesAnchorAndStartsOnlyNewSources()
        {
            SeedDetector first = new SeedDetector("a");
            SeedDetector second = new SeedDetector("b");
            GameObject frame = new GameObject("source frame");
            _objects.Add(frame);
            Anchor anchor = _realm.GetOrCreateAnchor("shared", frame.transform, first);
            Anchor reused = _realm.GetOrCreateAnchor("shared", frame.transform, first, second);
            Assert.That(reused, Is.SameAs(anchor));
            Assert.That(reused.Transform.parent, Is.SameAs(frame.transform));
            Assert.That(first.Starts, Is.EqualTo(1));
            Assert.That(second.Starts, Is.EqualTo(1));
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
        }

        private sealed class SeedDetector : PresenceDetector
        {
            private readonly string _id;
            internal int Starts;

            internal SeedDetector(string id)
            {
                _id = id;
            }

            protected override void OnStart()
            {
                Starts++;
                Detect(_id, Population);
            }
        }
    }
}
