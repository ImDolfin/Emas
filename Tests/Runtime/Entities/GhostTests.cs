using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies ghost contract resolution and diagnostics.
    /// </summary>
    public sealed class GhostTests
    {
        /// <summary>
        /// Ambiguous root providers identify the ghost and components without repeating until resolved.
        /// </summary>
        [Test]
        public void TryGet_ReportsAmbiguityOnceUntilResolved()
        {
            GameObject root = new GameObject("contract root");
            root.SetActive(false);
            try
            {
                TestGhost ghost = root.AddComponent<TestGhost>();
                Key key = new Key("simulation", new Kind("vehicles.car"), "42");
                ghost.Initialize(key, "Car 42", Variant.None);
                FirstPart first = root.AddComponent<FirstPart>();
                SecondPart second = root.AddComponent<SecondPart>();
                using (ErrorCapture errors = new ErrorCapture())
                {
                    IPart part;
                    Assert.That(ghost.TryGet<IPart>(out part), Is.False);
                    Assert.That(part, Is.Null);
                    Assert.That(ghost.TryGet<IPart>(out part), Is.False);
                    Assert.That(errors.Messages.Count, Is.EqualTo(1));
                    Assert.That(errors.Messages[0], Does.Contain(key.ToString())
                        .And.Contain(typeof(IPart).FullName)
                        .And.Contain(typeof(FirstPart).FullName)
                        .And.Contain(typeof(SecondPart).FullName));
                    Assert.That(errors.Contexts[0], Is.SameAs(ghost));

                    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                        () => ((IGhost)ghost).GetRequired<IPart>());
                    Assert.That(exception.Message, Does.Contain(key.ToString()).And.Contain(typeof(IPart).FullName));
                    Assert.That(errors.Messages.Count, Is.EqualTo(1));

                    UnityEngine.Object.DestroyImmediate(second);
                    Assert.That(ghost.TryGet<IPart>(out part), Is.True);
                    Assert.That(part, Is.SameAs(first));
                    root.AddComponent<SecondPart>();
                    Assert.That(ghost.TryGet<IPart>(out part), Is.False);
                    Assert.That(errors.Messages.Count, Is.EqualTo(2));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Required lookup returns a single provider and explains missing and null ghosts.
        /// </summary>
        [Test]
        public void GetRequired_ReturnsProviderOrExplainsMissing()
        {
            GameObject root = new GameObject("required contract root");
            root.SetActive(false);
            try
            {
                TestGhost concrete = root.AddComponent<TestGhost>();
                Key key = new Key("simulation", new Kind("vehicles.car"), "43");
                concrete.Initialize(key, "Car 43", Variant.None);
                IGhost ghost = concrete;
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => ghost.GetRequired<IPart>());
                Assert.That(exception.Message, Does.Contain(key.ToString()).And.Contain(typeof(IPart).FullName));

                FirstPart first = root.AddComponent<FirstPart>();
                Assert.That(ghost.GetRequired<IPart>(), Is.SameAs(first));
                Assert.Throws<ArgumentNullException>(() => GhostExtensions.GetRequired<IPart>(null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private interface IPart
        {
        }

        private sealed class TestGhost : Ghost
        {
        }

        private sealed class FirstPart : MonoBehaviour, IPart
        {
        }

        private sealed class SecondPart : MonoBehaviour, IPart
        {
        }

        private sealed class ErrorCapture : ILogHandler, IDisposable
        {
            private readonly ILogHandler _previous;
            internal readonly List<string> Messages = new List<string>();
            internal readonly List<UnityEngine.Object> Contexts = new List<UnityEngine.Object>();

            internal ErrorCapture()
            {
                _previous = Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = this;
            }

            /// <summary>
            /// Captures error messages and contexts; forwards other logs unchanged.
            /// </summary>
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                if (logType == LogType.Error)
                {
                    Messages.Add(string.Format(CultureInfo.InvariantCulture, format, args));
                    Contexts.Add(context);
                    return;
                }

                _previous.LogFormat(logType, context, format, args);
            }

            /// <summary>
            /// Forwards exception logs unchanged.
            /// </summary>
            public void LogException(Exception exception, UnityEngine.Object context)
            {
                _previous.LogException(exception, context);
            }

            /// <summary>
            /// Restores Unity's previous log handler.
            /// </summary>
            public void Dispose()
            {
                Debug.unityLogger.logHandler = _previous;
            }
        }
    }
}
