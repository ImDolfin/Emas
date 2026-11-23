using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Tracks when matches arrive and leave. Callbacks may remove Ghosts or dispose subscriptions or the Realm.
    internal sealed class Subscriptions
    {
        private sealed class Subscription : IDisposable
        {
            internal readonly Subscriptions Owner;
            internal readonly Query Query;
            internal readonly HashSet<Key> Seen = new HashSet<Key>();
            internal readonly HashSet<Key> MatchKeys = new HashSet<Key>();
            internal readonly List<IGhost> Matches = new List<IGhost>();
            internal readonly List<Key> Departures = new List<Key>();
            internal readonly List<Key> PendingDepartures = new List<Key>();
            internal Action<IGhost> Callback;
            internal Action<Key> OnLeave;
            internal bool Disposed;

            internal Subscription(Subscriptions owner, Query query, Action<IGhost> callback, Action<Key> onLeave)
            {
                Owner = owner;
                Query = query;
                Callback = callback;
                OnLeave = onLeave;
            }

            /// <summary>Stops notifications and releases the callback.</summary>
            public void Dispose()
            {
                if (Disposed)
                {
                    return;
                }

                // Remove the subscription first, then clear matches and callbacks. A callback may call this method.
                Disposed = true;
                Owner._items.Remove(this);
                Seen.Clear();
                MatchKeys.Clear();
                Matches.Clear();
                Departures.Clear();
                PendingDepartures.Clear();
                Callback = null;
                OnLeave = null;
            }
        }

        private readonly Realm _realm;
        private readonly List<Subscription> _items = new List<Subscription>();
        private readonly List<Subscription> _notificationItems = new List<Subscription>();
        private bool _notifying;

        internal Subscriptions(Realm realm)
        {
            _realm = realm;
        }

        internal IDisposable Subscribe(Query query, Action<IGhost> callback, Action<Key> onLeave, bool notifyImmediately)
        {
            Subscription subscription = new Subscription(this, query, callback, onLeave);
            _items.Add(subscription);
            // Report initial matches through the normal notification pass so callbacks cannot start a nested pass.
            if (notifyImmediately && !_notifying)
            {
                _notifying = true;
                try
                {
                    Notify(subscription);
                }
                finally
                {
                    _notifying = false;
                }
            }

            return subscription;
        }

        internal void Forget(Key key)
        {
            // Remember a departure even if the same Key returns before the next query check.
            for (int index = 0; index < _items.Count; index++)
            {
                Subscription subscription = _items[index];
                if (subscription.Seen.Remove(key) && subscription.OnLeave != null)
                {
                    subscription.Departures.Add(key);
                }
            }
        }

        internal void NotifyAll()
        {
            if (_notifying)
            {
                return;
            }

            _notifying = true;
            try
            {
                _notificationItems.Clear();
                // Subscriptions added by callbacks wait for the next pass, so their initial callbacks cannot nest.
                _notificationItems.AddRange(_items);
                for (int index = 0; index < _notificationItems.Count; index++)
                {
                    if (_realm.IsDisposed)
                    {
                        break;
                    }

                    Notify(_notificationItems[index]);
                }
            }
            finally
            {
                _notificationItems.Clear();
                _notifying = false;
            }
        }

        internal void Clear()
        {
            List<Subscription> items = new List<Subscription>(_items);
            for (int index = 0; index < items.Count; index++)
            {
                items[index].Dispose();
            }
        }

        private void Notify(Subscription subscription)
        {
            if (subscription.Disposed || _realm.IsDisposed)
            {
                return;
            }

            List<IGhost> matches = subscription.Matches;
            List<Key> departures = subscription.PendingDepartures;
            HashSet<Key> keys = subscription.MatchKeys;
            try
            {
                // Report departures before arrivals so observers see when a Ghost leaves and returns.
                RefreshMatches(subscription, matches, keys);
                if (!NotifyDepartures(subscription, departures))
                {
                    return;
                }

                NotifyArrivals(subscription, matches);
            }
            finally
            {
                // Release Ghost references even if a callback disposes the subscription or Realm.
                matches.Clear();
                departures.Clear();
                keys.Clear();
            }
        }

        private void RefreshMatches(Subscription subscription, List<IGhost> matches, HashSet<Key> keys)
        {
            _realm.Evaluate(subscription.Query, matches);
            keys.Clear();
            for (int index = 0; index < matches.Count; index++)
            {
                keys.Add(matches[index].Key);
            }

            // Collect departures before calling observers, since their callbacks may change the Realm.
            foreach (Key key in subscription.Seen)
            {
                if (!keys.Contains(key) && subscription.OnLeave != null)
                {
                    subscription.Departures.Add(key);
                }
            }

            subscription.Seen.IntersectWith(keys);
        }

        private bool NotifyDepartures(Subscription subscription, List<Key> departures)
        {
            // Report departures before arrivals so observers see when the same Key leaves and returns.
            departures.Clear();
            departures.AddRange(subscription.Departures);
            // Departures added by these callbacks wait for the next notification pass.
            subscription.Departures.Clear();
            for (int index = 0; index < departures.Count; index++)
            {
                if (subscription.Disposed || _realm.IsDisposed)
                {
                    return false;
                }

                Key key = departures[index];
                try
                {
                    subscription.OnLeave(key);
                }
                catch (Exception exception)
                {
                    PresenceDetector.LogError(exception, "query departure for " + key);
                }
            }

            return true;
        }

        private void NotifyArrivals(Subscription subscription, List<IGhost> matches)
        {
            for (int index = 0; index < matches.Count; index++)
            {
                if (subscription.Disposed || _realm.IsDisposed)
                {
                    break;
                }

                IGhost ghost = matches[index];
                Key key = ghost.Key;
                // Existing matches were checked by RefreshMatches. Only arrivals need a second check
                // after callbacks, including roots removed and republished during this notification pass.
                if (subscription.Seen.Contains(key))
                {
                    continue;
                }

                // An earlier callback may have removed this Ghost or changed whether it matches.
                if (!_realm.IsCurrentGhost(ghost) || !subscription.Query.Matches(ghost))
                {
                    continue;
                }

                // Save the match before calling observers, since a callback may remove this Ghost right away.
                if (subscription.Seen.Add(key))
                {
                    try
                    {
                        subscription.Callback(ghost);
                    }
                    catch (Exception exception)
                    {
                        PresenceDetector.LogError(exception, "query arrival for " + ghost.Key);
                    }
                }
            }
        }
    }
}
