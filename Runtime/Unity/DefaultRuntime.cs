using UnityEngine;

namespace Emas
{
    internal static class DefaultRuntime
    {
        private static Realm _realm;
        private static Runner _runner;

        internal static Realm Realm
        {
            get
            {
                EnsureRealm();
                return _realm;
            }
        }

        private static void EnsureRealm()
        {
            if (_realm != null && !_realm.IsDisposed)
            {
                return;
            }

            _realm = new Realm();
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
                if (_realm != null)
                {
                    _realm.Update();
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
            if (_realm != null)
            {
                _realm.Dispose();
                _realm = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureRealm();
        }
    }
}
