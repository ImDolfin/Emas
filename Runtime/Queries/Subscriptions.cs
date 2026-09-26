using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Owns match intervals; callbacks may remove ghosts, dispose subscriptions or dispose the realm.
    internal sealed class Subscriptions
    {
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
                _realm.Evaluate(subscription.Query, matches);
                keys.Clear();
                for (int index = 0; index < matches.Count; index++)
                {
                    keys.Add(matches[index].Key);
                }

                // Collect departures before invoking consumers, which may mutate the realm again.
                foreach (Key key in subscription.Seen)
                {
                    if (!keys.Contains(key) && subscription.OnLeave != null)
                    {
                        subscription.Departures.Add(key);
                    }
                }

                subscription.Seen.IntersectWith(keys);
                departures.Clear();
                departures.AddRange(subscription.Departures);
                subscription.Departures.Clear();
                for (int index = 0; index < departures.Count; index++)
                {
                    if (subscription.Disposed || _realm.IsDisposed)
                    {
                        return;
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

                for (int index = 0; index < matches.Count; index++)
                {
                    if (subscription.Disposed || _realm.IsDisposed)
                    {
                        break;
                    }

                    IGhost ghost = matches[index];
                    // A preceding callback can remove or invalidate another match in this snapshot.
                    if (!_realm.IsCurrentGhost(ghost) || !subscription.Query.Matches(ghost))
                    {
                        continue;
                    }

                    if (subscription.Seen.Add(ghost.Key))
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
            finally
            {
                matches.Clear();
                departures.Clear();
                keys.Clear();
            }
        }

        private sealed class Subscription : IDisposable
        {
            internal Subscription(Subscriptions owner, Query query, Action<IGhost> callback, Action<Key> onLeave)
            {
                Owner = owner;
                Query = query;
                Callback = callback;
                OnLeave = onLeave;
            }

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

            /// <summary>
            /// Stops notifications and releases the callback.
            /// </summary>
            public void Dispose()
            {
                if (Disposed)
                {
                    return;
                }

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
    }
}
