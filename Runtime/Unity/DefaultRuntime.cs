using UnityEngine;

namespace Emas
{
    internal static class DefaultRuntime
    {
        private static Context _context;
        private static Runner _runner;

        internal static Context Context
        {
            get
            {
                EnsureContext();
                return _context;
            }
        }

        private static void EnsureContext()
        {
            if (_context != null && !_context.IsDisposed)
            {
                return;
            }

            _context = new Context();
            if (_runner == null)
            {
                var runnerObject = new GameObject("[Emas Runner]");
                Object.DontDestroyOnLoad(runnerObject);
                _runner = runnerObject.AddComponent<Runner>();
            }
        }

        [DefaultExecutionOrder(-32000)]
        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                if (_context != null)
                {
                    _context.Update();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (_runner != null)
            {
                Object.Destroy(_runner.gameObject);
                _runner = null;
            }
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureContext();
        }
    }
}
