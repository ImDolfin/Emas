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
        private bool _notifying;

        internal Subscriptions(Realm realm)
        {
            _realm = realm;
        }

        internal IDisposable Subscribe(Query query, Action<IGhost> callback, bool notifyImmediately)
        {
            Subscription subscription = new Subscription(this, query, callback);
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
                _items[index].Seen.Remove(key);
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
                List<Subscription> items = new List<Subscription>(_items);
                for (int index = 0; index < items.Count; index++)
                {
                    if (_realm.IsDisposed)
                    {
                        break;
                    }

                    Notify(items[index]);
                }
            }
            finally
            {
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

            List<IGhost> matches = _realm.Evaluate(subscription.Query);
            HashSet<Key> keys = subscription.MatchKeys;
            keys.Clear();
            for (int index = 0; index < matches.Count; index++)
            {
                keys.Add(matches[index].Key);
            }

            // Retain seen identities only while they remain current matches.
            subscription.Seen.IntersectWith(keys);
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
                        Debug.LogException(exception);
                    }
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            internal Subscription(Subscriptions owner, Query query, Action<IGhost> callback)
            {
                Owner = owner;
                Query = query;
                Callback = callback;
            }

            internal readonly Subscriptions Owner;
            internal readonly Query Query;
            internal readonly HashSet<Key> Seen = new HashSet<Key>();
            internal readonly HashSet<Key> MatchKeys = new HashSet<Key>();
            internal Action<IGhost> Callback;
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
                Callback = null;
            }
        }
    }
}
