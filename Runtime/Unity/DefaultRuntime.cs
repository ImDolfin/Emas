using UnityEngine;

namespace Emas
{
    // Owns the shared realm and its persistent Unity runner; isolated RealmSetup lifetimes use their own updates.
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

        internal static bool TryGetRealm(out Realm realm)
        {
            realm = _realm != null && !_realm.IsDisposed ? _realm : null;
            return realm != null;
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
                GameObject runnerObject = new GameObject("[Emas Runner]");
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
            // Release the previous play session's static state even when Unity domain reload is disabled.
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
