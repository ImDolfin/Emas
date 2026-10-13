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
        private Query _carQuery;
        private Query _smallCarQuery;

        /// <summary>Gets the number of available cars reported by the scoped membership observer.</summary>
        /// <value>The observed car count; zero after this component stops observing or is disabled.</value>
        public int AvailableCarCount => _cars.Count;

        /// <summary>Gets the current number of small-car variants satisfying the root contracts.</summary>
        /// <value>A freshly evaluated count in this setup's Realm and car Anchor; zero before subscription setup.</value>
        public int SmallCarCount => _smallCarQuery == null ? 0 : _smallCarQuery.Count;

        /// <summary>Gets one currently available small car for optional contract-based data access.</summary>
        /// <value>A matching Ghost or null; query results have no ordering guarantee.</value>
        public IGhost FirstSmallCar => _smallCarQuery == null ? null : _smallCarQuery.FirstOrDefault();

        private void Update()
        {
            Realm realm = _setup == null || _carAnchor == null ? null : _setup.Realm;

            if (ReferenceEquals(realm, _observedRealm))
            {
                return;
            }

            // A restarted setup owns a different Realm. Dispose the old paired subscription before replaying the new one.
            StopObserving();
            _observedRealm = realm;
            if (realm != null)
            {
                _carQuery = realm.Query().InAnchor(_carAnchor.Id).OfKind(SampleKinds.Car)
                    .With<I3DPosition>().With<IArticulate>();
                // Filters return new descriptions. Deriving this variant query leaves the observed car query unchanged.
                _smallCarQuery = _carQuery.WithVariant(CarVariants.SmallCar);
                _carSubscription = _carQuery.Observe(ghost => _cars.Add(ghost.Key), key => _cars.Remove(key));
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
            _carQuery = null;
            _smallCarQuery = null;
            _cars.Clear();
        }

        [ContextMenu("Log queried car positions")]
        private void LogCarPositions()
        {
            if (_carQuery == null)
            {
                return;
            }

            // foreach captures matching membership now; each contract still exposes the Ghost's current data.
            foreach (IGhost ghost in _carQuery)
            {
                I3DPosition position;
                if (ghost.TryGet<I3DPosition>(out position))
                {
                    Debug.Log(ghost.Key + ": " + position.Position, this);
                }
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16f, 16f, 900f, 28f),
                "Emas: authored blueprints, optional views and SDK replacement");
            GUI.Label(new Rect(16f, 44f, 900f, 24f),
                "Cars: " + _cars.Count + "    Source: " +
                (_carSource.IsUsingSecondSdk ? "SDK Two (replaced)" : "SDK One"));
            GUI.Label(new Rect(16f, 68f, 900f, 24f),
                "Small cars: " + SmallCarCount + "    " + SmallCarPosition());
            GUI.Label(new Rect(16f, 92f, 900f, 24f),
                "Disable and re-enable Tracking to restart. Blueprints and prefabs stay configured in the Inspector.");
        }

        private string SmallCarPosition()
        {
            IGhost smallCar = FirstSmallCar;
            I3DPosition position;
            return smallCar != null && smallCar.TryGet<I3DPosition>(out position)
                ? smallCar.Name + " at " + position.Position : "Waiting for a small car";
        }
    }
}
