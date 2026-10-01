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
        /// Module diagnostics identify the anchor, unlabeled detector, operation and entity, retaining the original exception and label.
        /// </summary>
        [Test]
        public void ModuleFailure_IdentifiesOperationAndEntity()
        {
            Exception failure = new InvalidOperationException("module rejected item");
            _realm.RegisterPresenceInitializer<TextGhost>(VehicleKind, (presence, root) =>
                root.GetComponent<TextModule>().Bind(() =>
                {
                    throw failure;
                }));
            ArrivalDetector source = new ArrivalDetector { Name = " " };
            _realm.GetOrCreateAnchor("vehicles", source);
            ExpectedErrors.Verify(_realm.Update, "vehicles.*ArrivalDetector.*Update modules.*module rejected item");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("anchor 'vehicles'").And.Contain("source 'ArrivalDetector'"));
            Assert.That(source.LastErrorContext, Does.Contain("operation 'Update modules'").And.Contain("kind 'car'").And.Contain("entity '42'"));
            string recorded = source.LastErrorContext;
            source.Name = "Renamed";
            Assert.That(source.LastErrorContext, Is.EqualTo(recorded));
        }

        /// <summary>
        /// Primary module context survives a second failure during cleanup and clears on restart.
        /// </summary>
        [Test]
        public void Cleanup_PreservesPrimaryContextAndRestartClearsIt()
        {
            bool failing = true;
            InvalidOperationException primary = new InvalidOperationException("report failed");
            _realm.RegisterPresenceInitializer<TextGhost>(VehicleKind, (presence, root) =>
                root.GetComponent<TextModule>().Bind(() =>
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
            ExpectedErrors.Verify(_realm.Update, "operation 'Update modules'.*entity '42'.*report failed", "operation 'OnStop'.*cleanup failed");
            Assert.That(source.LastErrorContext, Does.Contain("Update modules").And.Contain("42"));
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

            protected override void OnUpdate()
            {
                Detect("42", VehicleKind);
            }

            protected override void OnStop()
            {
                Stopping?.Invoke();
            }
        }

    }
}
