using UnityEngine;

namespace Emas
{
    // Owns the shared Realm and its Unity update component. Each RealmSetup runs its own updates.
    internal static class DefaultRuntime
    {
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
            // Debug code uses this lookup so reading state does not create the shared update component.
            realm = _realm != null && !_realm.IsDisposed ? _realm : null;
            return realm != null;
        }

        private static void EnsureRealm()
        {
            if (_realm != null && !_realm.IsDisposed)
            {
                return;
            }

            // Keep the update component across scene changes so loading a scene does not reset tracking.
            _realm = new Realm();
            if (_runner == null)
            {
                GameObject runnerObject = new GameObject("[Emas Runner]");
                Object.DontDestroyOnLoad(runnerObject);
                _runner = runnerObject.AddComponent<Runner>();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            // Clear state from the previous play session even when Unity keeps static fields between sessions.
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
