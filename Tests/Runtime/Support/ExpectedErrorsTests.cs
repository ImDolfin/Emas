using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Ensures quiet expected-failure assertions never swallow unexpected logs or change later tests' logging.
    /// </summary>
    public sealed class ExpectedErrorsTests
    {
        private ILogHandler _previous;
        private RecordingHandler _forwarded;

        /// <summary>
        /// Records forwarded messages so negative capture tests do not emit deliberate errors to Unity.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _previous = Debug.unityLogger.logHandler;
            _forwarded = new RecordingHandler();
            Debug.unityLogger.logHandler = _forwarded;
        }

        /// <summary>
        /// Restores Unity's handler after every assertion, including a failed one.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Debug.unityLogger.logHandler = _previous;
        }

        /// <summary>
        /// Both Unity exception entry points are captured without forwarding their stack traces.
        /// </summary>
        [Test]
        public void ExpectedMessages_AreCapturedInOrder()
        {
            ExpectedErrors.Verify(() =>
            {
                Debug.LogFormat(LogType.Exception, LogOption.NoStacktrace, null, "{0}\nstack details", "formatted failure");
                Debug.LogException(new InvalidOperationException("direct failure"));
            }, "formatted failure", "direct failure");
            Assert.That(_forwarded.Messages, Is.Empty);
            Assert.That(Debug.unityLogger.logHandler, Is.SameAs(_forwarded));
        }

        /// <summary>
        /// Missing and out-of-order messages fail the assertion and restore the original handler.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MissingExpectedMessage_Fails(bool wrongOrder)
        {
            AssertionException failure = Assert.Throws<AssertionException>(() => ExpectedErrors.Verify(() =>
            {
                if (wrongOrder)
                {
                    Debug.LogException(new Exception("second failure"));
                }
            }, "first failure", "second failure"));
            Assert.That(failure.Message, Does.Contain("1 of 2").And.Contain("first failure"));
            Assert.That(_forwarded.Messages.Count, Is.EqualTo(wrongOrder ? 1 : 0));
            Assert.That(Debug.unityLogger.logHandler, Is.SameAs(_forwarded));
        }

        /// <summary>
        /// Unexpected severity, unrelated errors and duplicate exceptions still reach Unity's previous handler.
        /// </summary>
        [TestCase(LogType.Error)]
        [TestCase(LogType.Warning)]
        [TestCase(LogType.Log)]
        [TestCase(LogType.Exception)]
        public void UnexpectedMessages_AreForwarded(LogType type)
        {
            ExpectedErrors.Verify(() =>
            {
                Debug.LogException(new Exception("expected failure"));
                Debug.LogFormat(type, LogOption.NoStacktrace, null, "{0}", "unexpected message");
                Debug.LogException(new Exception("expected failure"));
            }, "expected failure");
            Assert.That(_forwarded.Messages, Is.EqualTo(new[] { "unexpected message", "expected failure" }));
            Assert.That(_forwarded.Types, Is.EqualTo(new[] { type, LogType.Exception }));
        }

        /// <summary>
        /// Message text appearing only inside stack details cannot satisfy an expected header.
        /// </summary>
        [Test]
        public void StackTraceText_DoesNotMatchAnExpectedMessage()
        {
            Assert.Throws<AssertionException>(() => ExpectedErrors.Verify(() =>
            {
                Debug.LogFormat(LogType.Exception, LogOption.NoStacktrace, null, "{0}", "unrelated failure\nexpected failure");
            }, "expected failure"));
            Assert.That(_forwarded.Messages.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A thrown test assertion or exception remains the primary failure even if an expected log is missing.
        /// </summary>
        [Test]
        public void OperationFailure_IsPreservedAndRestoresLogging()
        {
            InvalidOperationException primary = new InvalidOperationException("operation failed");
            Exception observed = Assert.Throws<InvalidOperationException>(() => ExpectedErrors.Verify(() =>
            {
                throw primary;
            }, "missing message"));
            Assert.That(observed, Is.SameAs(primary));
            Assert.That(Debug.unityLogger.logHandler, Is.SameAs(_forwarded));
        }

        /// <summary>
        /// Nested capture scopes restore their enclosing handler and verify their own expected messages.
        /// </summary>
        [Test]
        public void NestedCaptures_RestoreTheEnclosingHandler()
        {
            ExpectedErrors.Verify(() =>
            {
                ILogHandler outer = Debug.unityLogger.logHandler;
                ExpectedErrors.Verify(() => Debug.LogException(new Exception("inner failure")), "inner failure");
                Assert.That(Debug.unityLogger.logHandler, Is.SameAs(outer));
                Debug.LogException(new Exception("outer failure"));
            }, "outer failure");
            Assert.That(_forwarded.Messages, Is.Empty);
            Assert.That(Debug.unityLogger.logHandler, Is.SameAs(_forwarded));
        }

        private sealed class RecordingHandler : ILogHandler
        {
            internal readonly List<string> Messages = new List<string>();
            internal readonly List<LogType> Types = new List<LogType>();

            /// <inheritdoc />
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                Types.Add(logType);
                Messages.Add(string.Format(format, args));
            }

            /// <inheritdoc />
            public void LogException(Exception exception, UnityEngine.Object context)
            {
                Types.Add(LogType.Exception);
                Messages.Add(exception.Message);
            }
        }
    }
}
