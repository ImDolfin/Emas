using System;
using NUnit.Framework;
using UnityEngine;

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
        /// Builder failures identify the operation and known entity without replacing the original exception.
        /// </summary>
        [TestCase(false, "IdentifyBy")]
        [TestCase(false, "WithVariant")]
        [TestCase(false, "Apply")]
        [TestCase(true, "IdentifyBy")]
        [TestCase(true, "WithVariant")]
        [TestCase(true, "Apply")]
        public void BuilderFailure_CapturesOperationAndEntity(bool callback, string operation)
        {
            Exception failure = new InvalidOperationException("SDK rejected item");
            Func<string, string> identify = id => operation == "IdentifyBy" ? throw failure : id;
            Func<string, Variant> variant = id => operation == "WithVariant" ? throw failure : Variant.None;
            Action<string, TestGhost> apply = (id, ghost) =>
            {
                if (operation == "Apply")
                {
                    throw failure;
                }
            };
            PresenceSource source;
            Anchor anchor = _realm.GetOrCreateAnchor("vehicles");
            if (callback)
            {
                source = new CallbackPresenceSource<string, TestGhost>(new Kind("car"))
                    .IdentifyBy(identify).WithVariant(variant).Apply(apply)
                    .Listen((publish, remove) =>
                    {
                        publish("42");
                        return null;
                    });
                source.Name = "SDK One";
                anchor.AddSource(source);
                ExpectedErrors.Verify(_realm.Update, "vehicles.*SDK One.*" + operation + ".*SDK rejected item");
            }
            else
            {
                source = new PollingPresenceSource<string, TestGhost>(new Kind("car"))
                    .ReadFrom(() => new[] { "42" }).IdentifyBy(identify).WithVariant(variant).Apply(apply);
                source.Name = "SDK One";
                Assert.That(Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source)), Is.SameAs(failure));
            }

            Assert.That(source.LastError, Is.SameAs(failure));
            Assert.That(source.LastErrorContext, Does.Contain("anchor 'vehicles'").And.Contain("source 'SDK One'"));
            Assert.That(source.LastErrorContext, Does.Contain("operation '" + operation + "'").And.Contain("kind 'car'"));
            if (operation != "IdentifyBy")
            {
                Assert.That(source.LastErrorContext, Does.Contain("entity '42'"));
            }

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
            CallbackPresenceSource<string, TestGhost> source = new CallbackPresenceSource<string, TestGhost>(new Kind("car"))
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
            anchor.RestartSource(source);
            Assert.That(source.LastErrorContext, Is.Null);
            _realm.Update();
            Assert.That(source.LastError, Is.Null);
            Assert.That(source.IsAttached && source.IsActive, Is.True);
        }

        /// <summary>
        /// Subscription startup and teardown failures retain their specific operation names.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Callback_IdentifiesSubscriptionFailures(bool cleanup)
        {
            CallbackPresenceSource<string, TestGhost> source = new CallbackPresenceSource<string, TestGhost>(new Kind("car"))
                .IdentifyBy(id => id).Apply((id, ghost) =>
                {
                })
                .Listen((publish, remove) =>
                {
                    if (!cleanup)
                    {
                        throw new InvalidOperationException("listen failed");
                    }

                    return () =>
                    {
                        throw new InvalidOperationException("cleanup failed");
                    };
                });
            Anchor anchor = _realm.GetOrCreateAnchor("vehicles");
            if (cleanup)
            {
                anchor.AddSource(source);
                ExpectedErrors.Verify(() => anchor.RemoveSource(source), "vehicles.*Unsubscribe.*cleanup failed");
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => anchor.AddSource(source));
            }

            Assert.That(source.LastErrorContext, Does.Contain(cleanup ? "Unsubscribe" : "Listen"));
            Assert.That(source.LastErrorContext, Does.Contain("vehicles"));
            Assert.That(source.LastError.Message, Is.EqualTo(cleanup ? "cleanup failed" : "listen failed"));
            Assert.That(source.IsAttached || source.IsActive, Is.False);
        }

        /// <summary>
        /// Empty labels fall back to the readable source type and do not change source identity.
        /// </summary>
        [Test]
        public void Name_UsesTypeFallback()
        {
            CallbackPresenceSource<string, TestGhost> source = new CallbackPresenceSource<string, TestGhost>(new Kind("car"));
            Assert.That(source.Name, Is.EqualTo("CallbackPresenceSource"));
            source.Name = "Vehicle SDK";
            Assert.That(source.Name, Is.EqualTo("Vehicle SDK"));
            source.Name = " ";
            Assert.That(source.Name, Is.EqualTo("CallbackPresenceSource"));
            source.Name = null;
            Assert.That(source.Name, Is.EqualTo("CallbackPresenceSource"));
        }

        private sealed class TestGhost : Ghost
        {
        }
    }
}
