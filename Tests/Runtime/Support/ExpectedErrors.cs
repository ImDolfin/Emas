using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    // Intercepts only the exception messages declared for one synchronous test operation.
    internal sealed class ExpectedErrors : ILogHandler
    {
        private readonly ILogHandler _previous;
        private readonly Regex[] _patterns;
        private readonly List<string> _captured = new List<string>();

        private ExpectedErrors(string[] patterns)
        {
            _previous = Debug.unityLogger.logHandler;
            _patterns = new Regex[patterns.Length];
            for (int index = 0; index < patterns.Length; index++)
            {
                _patterns[index] = new Regex(patterns[index]);
            }
        }

        internal static void Verify(Action operation, params string[] patterns)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (patterns == null)
            {
                throw new ArgumentNullException(nameof(patterns));
            }

            ExpectedErrors capture = new ExpectedErrors(patterns);
            Debug.unityLogger.logHandler = capture;
            try
            {
                operation();
            }
            finally
            {
                // Restore logging before assertions or test output, even when the operation itself throws.
                Debug.unityLogger.logHandler = capture._previous;
            }

            capture.VerifyComplete();
        }

        /// <summary>
        /// Captures the next expected exception message or forwards the original log unchanged.
        /// </summary>
        /// <param name="logType">
        /// Original Unity severity.
        /// </param>
        /// <param name="context">
        /// Original Unity object context.
        /// </param>
        /// <param name="format">
        /// Original message format.
        /// </param>
        /// <param name="args">
        /// Message format arguments.
        /// </param>
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (logType == LogType.Exception && TryCapture(string.Format(CultureInfo.InvariantCulture, format, args)))
            {
                return;
            }

            _previous.LogFormat(logType, context, format, args);
        }

        /// <summary>
        /// Captures an expected direct exception log or forwards the original exception unchanged.
        /// </summary>
        /// <param name="exception">
        /// Logged exception.
        /// </param>
        /// <param name="context">
        /// Original Unity object context.
        /// </param>
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (!TryCapture(exception.GetType().Name + ": " + exception.Message))
            {
                _previous.LogException(exception, context);
            }
        }

        private bool TryCapture(string message)
        {
            // Match the message header, so text in a stack trace cannot satisfy an expectation accidentally.
            int newline = message.IndexOfAny(new[] { '\r', '\n' });
            string header = newline < 0 ? message : message.Substring(0, newline);
            if (_captured.Count >= _patterns.Length || !_patterns[_captured.Count].IsMatch(header))
            {
                return false;
            }

            _captured.Add(header);
            return true;
        }

        private void VerifyComplete()
        {
            if (_captured.Count != _patterns.Length)
            {
                Assert.Fail("Expected exception log {0} of {1} was not observed: {2}",
                    _captured.Count + 1, _patterns.Length, _patterns[_captured.Count]);
            }

            foreach (string message in _captured)
            {
                TestContext.Out.WriteLine("Verified expected failure: " + message);
            }
        }
    }
}
