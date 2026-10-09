using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Displays paired-query membership while the authored setup owns tracking and automatic views.
    /// </summary>
    public sealed class SampleStatus : MonoBehaviour
    {
        [SerializeField]
        private RealmSetup _setup;
        [SerializeField]
        private AnchorSetup _carAnchor;
        [SerializeField]
        private CarSource _carSource;

        private readonly HashSet<Key> _cars = new HashSet<Key>();
        private Realm _observedRealm;
        private IDisposable _carSubscription;

        private void Update()
        {
            Realm realm = _setup.Realm;
            if (ReferenceEquals(realm, _observedRealm))
            {
                return;
            }

            // A restarted setup owns a different Realm. Dispose the old paired subscription before replaying the new one.
            StopObserving();
            _observedRealm = realm;
            if (realm != null)
            {
                _carSubscription = realm.Query().InAnchor(_carAnchor.Id).OfKind(SampleKinds.Car)
                    .With<I3DPosition>().With<IArticulate>()
                    .Observe(ghost => _cars.Add(ghost.Key), key => _cars.Remove(key));
            }
        }

        private void OnDisable()
        {
            StopObserving();
        }

        private void StopObserving()
        {
            _carSubscription?.Dispose();
            _carSubscription = null;
            _observedRealm = null;
            _cars.Clear();
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16f, 16f, 900f, 28f),
                "Emas: authored blueprints, optional views and SDK replacement");
            GUI.Label(new Rect(16f, 44f, 900f, 24f),
                "Cars: " + _cars.Count + "    Source: " +
                (_carSource.IsUsingSecondSdk ? "SDK Two (replaced)" : "SDK One"));
            GUI.Label(new Rect(16f, 68f, 900f, 24f),
                "Disable and re-enable Tracking to restart. Blueprints and prefabs stay configured in the Inspector.");
        }
    }
}
