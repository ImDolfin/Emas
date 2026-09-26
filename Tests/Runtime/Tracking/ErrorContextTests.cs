using System;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies diagnostics retain source, operation and entity context alongside the original exception.
    /// </summary>
    public sealed class ErrorContextTests
    {
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
        /// Releases tracking state.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>
        /// Mapping diagnostics identify the anchor, source, operation and entity while retaining the original exception.
        /// </summary>
        [Test]
        public void MappingFailure_IdentifiesOperationAndEntity()
        {
            Exception failure = new InvalidOperationException("SDK rejected item");
            CallbackPresenceDetector<string, TestGhost> source = new CallbackPresenceDetector<string, TestGhost>(new Kind("car"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                    throw failure;
                })
                .Listen((publish, remove) =>
                {
                    publish("42");
                    return null;
                });
            source.Name = "SDK One";
            _realm.GetOrCreateAnchor("vehicles", source);
            ExpectedErrors.Verify(_realm.Update, "vehicles.*SDK One.*Apply.*SDK rejected item");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("anchor 'vehicles'").And.Contain("source 'SDK One'"));
            Assert.That(source.LastErrorContext, Does.Contain("operation 'Apply'").And.Contain("kind 'car'").And.Contain("entity '42'"));
            string recorded = source.LastErrorContext;
            source.Name = "Renamed";
            Assert.That(source.LastErrorContext, Is.EqualTo(recorded));
        }

        /// <summary>
        /// Primary mapping context survives a second failure during unsubscribe and clears on restart.
        /// </summary>
        [Test]
        public void Cleanup_PreservesPrimaryContextAndRestartClearsIt()
        {
            bool failing = true;
            InvalidOperationException primary = new InvalidOperationException("mapping failed");
            CallbackPresenceDetector<string, TestGhost> source = new CallbackPresenceDetector<string, TestGhost>(new Kind("car"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                    if (failing)
                    {
                        throw primary;
                    }
                })
                .Listen((publish, remove) =>
                {
                    publish("42");
                    return () =>
                    {
                        if (failing)
                        {
                            throw new Exception("unsubscribe failed");
                        }
                    };
                });
            source.Name = "Shared SDK";
            Anchor anchor = _realm.GetOrCreateAnchor("vehicles", source);
            ExpectedErrors.Verify(_realm.Update, "operation 'Apply'.*entity '42'.*mapping failed", "operation 'Unsubscribe'.*unsubscribe failed");
            Assert.That(source.LastErrorContext, Does.Contain("Apply").And.Contain("42"));
            Assert.That(source.LastError, Is.SameAs(primary));
            Assert.That(source.IsAttached, Is.True);
            Assert.That(source.IsActive, Is.False);
            failing = false;
            anchor.RestartDetector(source);
            Assert.That(source.LastErrorContext, Is.Null);
            _realm.Update();
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.IsAttached && source.IsActive, Is.True);
        }

        /// <summary>
        /// Empty labels fall back to the readable detector type so applications can identify unlabeled detectors.
        /// </summary>
        [Test]
        public void Name_UsesTypeFallback()
        {
            CallbackPresenceDetector<string, TestGhost> source = new CallbackPresenceDetector<string, TestGhost>(new Kind("car"));
            Assert.That(source.Name, Is.EqualTo("CallbackPresenceDetector"));
            source.Name = "Vehicle SDK";
            Assert.That(source.Name, Is.EqualTo("Vehicle SDK"));
            source.Name = " ";
            Assert.That(source.Name, Is.EqualTo("CallbackPresenceDetector"));
            source.Name = null;
            Assert.That(source.Name, Is.EqualTo("CallbackPresenceDetector"));
        }

        private sealed class TestGhost : Ghost
        {
        }
    }
}
