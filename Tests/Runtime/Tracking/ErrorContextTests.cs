using System;
using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies diagnostics retain source, operation and entity context alongside the original exception.
    /// </summary>
    public sealed class ErrorContextTests
    {
        private static readonly Kind VehicleKind = new Kind("car");
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
        /// Trait diagnostics identify the anchor, unlabeled detector, operation and entity, retaining the original exception and label.
        /// </summary>
        [Test]
        public void TraitFailure_IdentifiesOperationAndEntity()
        {
            Exception failure = new InvalidOperationException("trait rejected item");
            _realm.RegisterPresenceInitializer<TextGhost>(VehicleKind, (presence, root) =>
                root.GetComponent<TextTrait>().Bind(() =>
                {
                    throw failure;
                }));
            ArrivalDetector source = new ArrivalDetector { Name = " " };
            _realm.GetOrCreateAnchor("vehicles", source);
            ExpectedErrors.Verify(_realm.Update, "vehicles.*ArrivalDetector.*Update traits.*trait rejected item");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("anchor 'vehicles'").And.Contain("source 'ArrivalDetector'"));
            Assert.That(source.LastErrorContext, Does.Contain("operation 'Update traits'").And.Contain("kind 'car'").And.Contain("entity '42'"));
            string recorded = source.LastErrorContext;
            source.Name = "Renamed";
            Assert.That(source.LastErrorContext, Is.EqualTo(recorded));
        }

        /// <summary>
        /// Primary trait context survives a second failure during cleanup and clears on restart.
        /// </summary>
        [Test]
        public void Cleanup_PreservesPrimaryContextAndRestartClearsIt()
        {
            bool failing = true;
            InvalidOperationException primary = new InvalidOperationException("report failed");
            _realm.RegisterPresenceInitializer<TextGhost>(VehicleKind, (presence, root) =>
                root.GetComponent<TextTrait>().Bind(() =>
                {
                    if (failing)
                    {
                        throw primary;
                    }

                    return "42";
                }));
            ArrivalDetector source = new ArrivalDetector
            {
                Name = "Shared SDK",
                Stopping = () =>
                {
                    if (failing)
                    {
                        throw new Exception("cleanup failed");
                    }
                }
            };
            Anchor anchor = _realm.GetOrCreateAnchor("vehicles", source);
            ExpectedErrors.Verify(_realm.Update, "operation 'Update traits'.*entity '42'.*report failed", "operation 'OnStop'.*cleanup failed");
            Assert.That(source.LastErrorContext, Does.Contain("Update traits").And.Contain("42"));
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

        private sealed class ArrivalDetector : PresenceDetector
        {
            internal Action Stopping;

            /// <summary>Publishes the vehicle used to test error context during initialization.</summary>
            protected override void OnUpdate()
            {
                Detect("42", VehicleKind);
            }

            /// <summary>Runs the cleanup action whose failure must retain detector context.</summary>
            protected override void OnStop()
            {
                Stopping?.Invoke();
            }
        }

    }
}
