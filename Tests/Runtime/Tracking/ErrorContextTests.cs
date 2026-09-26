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
        /// Report diagnostics identify the anchor, detector, operation and entity while retaining the original exception.
        /// </summary>
        [Test]
        public void ReportFailure_IdentifiesOperationAndEntity()
        {
            Exception failure = new InvalidOperationException("module rejected item");
            _realm.RegisterPresenceInitializer<TestGhost>(VehicleKind, (presence, root) =>
                presence.AddModule(new ReportingModule(() =>
                {
                    throw failure;
                })));
            ReportingDetector source = new ReportingDetector { Name = "SDK One" };
            _realm.GetOrCreateAnchor("vehicles", source);
            ExpectedErrors.Verify(_realm.Update, "vehicles.*SDK One.*Report.*module rejected item");
            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("anchor 'vehicles'").And.Contain("source 'SDK One'"));
            Assert.That(source.LastErrorContext, Does.Contain("operation 'Report'").And.Contain("kind 'car'").And.Contain("entity '42'"));
            string recorded = source.LastErrorContext;
            source.Name = "Renamed";
            Assert.That(source.LastErrorContext, Is.EqualTo(recorded));
        }

        /// <summary>
        /// Primary report context survives a second failure during cleanup and clears on restart.
        /// </summary>
        [Test]
        public void Cleanup_PreservesPrimaryContextAndRestartClearsIt()
        {
            bool failing = true;
            InvalidOperationException primary = new InvalidOperationException("report failed");
            _realm.RegisterPresenceInitializer<TestGhost>(VehicleKind, (presence, root) =>
                presence.AddModule(new ReportingModule(() =>
                {
                    if (failing)
                    {
                        throw primary;
                    }
                })));
            ReportingDetector source = new ReportingDetector
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
            ExpectedErrors.Verify(_realm.Update, "operation 'Report'.*entity '42'.*report failed", "operation 'OnStop'.*cleanup failed");
            Assert.That(source.LastErrorContext, Does.Contain("Report").And.Contain("42"));
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
            ReportingDetector source = new ReportingDetector();
            Assert.That(source.Name, Is.EqualTo("ReportingDetector"));
            source.Name = "Vehicle SDK";
            Assert.That(source.Name, Is.EqualTo("Vehicle SDK"));
            source.Name = " ";
            Assert.That(source.Name, Is.EqualTo("ReportingDetector"));
            source.Name = null;
            Assert.That(source.Name, Is.EqualTo("ReportingDetector"));
        }

        private sealed class ReportingDetector : PresenceDetector
        {
            internal Action Stopping;

            protected override void OnUpdate()
            {
                Report("42", VehicleKind, "42");
            }

            protected override void OnStop()
            {
                Stopping?.Invoke();
            }
        }

        private sealed class ReportingModule : EntityModule<string>
        {
            private readonly Action _apply;

            internal ReportingModule(Action apply)
            {
                _apply = apply;
            }

            /// <summary>Applies the consumer behavior whose failures must retain report context.</summary>
            public override void Apply(string data)
            {
                _apply();
            }
        }

        private sealed class TestGhost : Ghost
        {
        }
    }
}
