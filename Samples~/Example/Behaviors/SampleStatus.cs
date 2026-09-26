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
        private AnchorSetup _aircraftAnchor;
        [SerializeField]
        private CarSource _carSource;

        private readonly HashSet<Key> _cars = new HashSet<Key>();
        private readonly HashSet<Key> _aircraft = new HashSet<Key>();
        private Realm _observedRealm;
        private IDisposable _carSubscription;
        private IDisposable _aircraftSubscription;

        private void Update()
        {
            Realm realm = _setup.Realm;
            if (ReferenceEquals(realm, _observedRealm))
            {
                return;
            }

            StopObserving();
            _observedRealm = realm;
            if (realm != null)
            {
                _carSubscription = realm.Query().InAnchor(_carAnchor.Id).OfKind(SampleKinds.Car)
                    .With<I3DPosition>().With<IArticulate>()
                    .Observe(ghost => _cars.Add(ghost.Key), key => _cars.Remove(key));
                _aircraftSubscription = realm.Query().InAnchor(_aircraftAnchor.Id).OfKind(SampleKinds.Aircraft)
                    .With<I3DPosition>()
                    .Observe(ghost => _aircraft.Add(ghost.Key), key => _aircraft.Remove(key));
            }
        }

        private void OnDisable()
        {
            StopObserving();
        }

        private void StopObserving()
        {
            _carSubscription?.Dispose();
            _aircraftSubscription?.Dispose();
            _carSubscription = null;
            _aircraftSubscription = null;
            _observedRealm = null;
            _cars.Clear();
            _aircraft.Clear();
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16f, 16f, 900f, 28f),
                "Emas: authored blueprints, optional views and SDK replacement");
            GUI.Label(new Rect(16f, 44f, 900f, 24f),
                "Cars: " + _cars.Count + "    Aircraft: " + _aircraft.Count + "    Car source: " +
                (_carSource.IsUsingSecondSdk ? "SDK Two (replaced)" : "SDK One"));
            GUI.Label(new Rect(16f, 68f, 900f, 24f),
                "Disable and re-enable Tracking to restart. Blueprints and prefabs stay configured in the Inspector.");
            GUI.Label(new Rect(16f, 92f, 900f, 24f),
                "The cockpit marker follows screen-local data independently of the tracked ghosts.");
        }
    }
}
