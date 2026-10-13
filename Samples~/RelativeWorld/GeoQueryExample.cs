using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Demonstrates scoped queries, immutable filters and paired parked-car membership in the geographic sample.</summary>
    /// <remarks>Uses available Ghost roots and their Spatial data independently of views. Disposes and rebinds when the authored setup restarts.</remarks>
    public sealed class GeoQueryExample : MonoBehaviour
    {
        [SerializeField] private RealmSetup _setup;
        [SerializeField] private AnchorSetup _anchor;
        private readonly HashSet<Key> _parkedCars = new HashSet<Key>();
        private Realm _observedRealm;
        private Query _carQuery;
        private Query _parkedCarQuery;
        private Query _originQuery;
        private Query _birdQuery;
        private IDisposable _parkedCarSubscription;

        /// <summary>Gets the current number of available car Ghosts, including the driving reference.</summary>
        /// <value>A freshly evaluated count in this setup's Realm and Anchor, independently of view or range state.</value>
        public int AvailableCarCount => _carQuery == null ? 0 : _carQuery.Count;

        /// <summary>Gets the number of parked-car identities maintained by paired query callbacks.</summary>
        /// <value>The observed target-variant count; zero when disabled or after observation stops.</value>
        public int ParkedCarCount => _parkedCars.Count;

        /// <summary>Gets an available bird with the root Spatial contract, when one has arrived.</summary>
        /// <value>A matching Ghost or null; no result ordering is guaranteed.</value>
        public IGhost Bird => _birdQuery == null ? null : _birdQuery.FirstOrDefault();

        /// <summary>Gets the SDK-observed ECEF chord distance between the bird and the driving reference in metres.</summary>
        /// <value>The raw-pose separation in metres, or null until both available roots have position observations.</value>
        /// <remarks>The sample captures both poses together. This is measured separation, independent of prediction, smoothing and projected visibility.</remarks>
        public double? BirdDistanceMeters
        {
            get
            {
                IGhost origin = _originQuery == null ? null : _originQuery.FirstOrDefault();
                IGhost bird = Bird;
                Spatial originSpatial;
                Spatial birdSpatial;
                if (origin == null || bird == null || !origin.TryGet<Spatial>(out originSpatial)
                    || !bird.TryGet<Spatial>(out birdSpatial) || !originSpatial.HasPosition || !birdSpatial.HasPosition)
                {
                    return null;
                }

                return Double3.Distance(originSpatial.Position, birdSpatial.Position);
            }
        }

        private void Update()
        {
            Realm realm = _setup == null || _anchor == null ? null : _setup.Realm;

            if (ReferenceEquals(realm, _observedRealm))
            {
                return;
            }

            StopObserving();
            _observedRealm = realm;
            if (realm == null)
            {
                return;
            }

            _carQuery = realm.Query().InAnchor(_anchor.Id).OfKind(GeoSource.Kind).With<Spatial>();
            _parkedCarQuery = _carQuery.WithVariant(new Variant("target"));
            _originQuery = _carQuery.WithVariant(new Variant("origin"));
            _birdQuery = realm.Query().InAnchor(_anchor.Id).OfKind(GeoSource.BirdKind).With<Spatial>();

            // Current matches may enter synchronously. The collection is ready before subscribing.
            // Keep only identity on departure: the Ghost's Unity object may already be destroyed.
            _parkedCarSubscription = _parkedCarQuery.Observe(
                ghost => _parkedCars.Add(ghost.Key), key => _parkedCars.Remove(key));
        }

        private void OnDisable()
        {
            StopObserving();
        }

        private void StopObserving()
        {
            _parkedCarSubscription?.Dispose();
            _parkedCarSubscription = null;
            _observedRealm = null;
            _carQuery = null;
            _parkedCarQuery = null;
            _originQuery = null;
            _birdQuery = null;
            // Subscription disposal does not synthesize departures; the consumer clears its own retained state.
            _parkedCars.Clear();
        }

        [ContextMenu("Log queried parked cars")]
        private void LogParkedCars()
        {
            if (_parkedCarQuery == null)
            {
                return;
            }

            foreach (IGhost ghost in _parkedCarQuery)
            {
                Spatial spatial;
                if (ghost.TryGet<Spatial>(out spatial) && spatial.HasPosition)
                {
                    Debug.Log(ghost.Key + ": " + GeoPosition.FromEarthCentered(spatial.Position), this);
                }
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16f, 16f, 900f, 24f),
                "Available cars: " + AvailableCarCount + "    Parked cars: " + ParkedCarCount);
            double? distance = BirdDistanceMeters;
            GUI.Label(new Rect(16f, 40f, 900f, 24f),
                distance.HasValue ? "Bird separation: " + distance.Value.ToString("F1") + " m" : "Waiting for bird position");
        }
    }
}
