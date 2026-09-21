using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Owns match intervals; callbacks may remove ghosts, dispose subscriptions or dispose the context.
    internal sealed class Subscriptions
    {
        private readonly Context _context;
        private readonly List<Subscription> _items = new List<Subscription>();
        private bool _notifying;

        internal Subscriptions(Context context)
        {
            _context = context;
        }

        internal IDisposable Subscribe(Query query, Action<IGhost> callback, bool notifyImmediately)
        {
            var subscription = new Subscription(this, query, callback);
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
            for (var index = 0; index < _items.Count; index++)
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
                var items = new List<Subscription>(_items);
                for (var index = 0; index < items.Count; index++)
                {
                    if (_context.IsDisposed)
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
            var items = new List<Subscription>(_items);
            for (var index = 0; index < items.Count; index++)
            {
                items[index].Dispose();
            }
        }

        private void Notify(Subscription subscription)
        {
            if (subscription.Disposed || _context.IsDisposed)
            {
                return;
            }
            var matches = _context.Evaluate(subscription.Query);
            var keys = subscription.MatchKeys;
            keys.Clear();
            for (var index = 0; index < matches.Count; index++)
            {
                keys.Add(matches[index].Key);
            }
            // Linear set intersection replaces a linear search for every previously seen key.
            subscription.Seen.IntersectWith(keys);
            for (var index = 0; index < matches.Count; index++)
            {
                if (subscription.Disposed || _context.IsDisposed)
                {
                    break;
                }
                var ghost = matches[index];
                // A preceding callback can remove or invalidate another match in this snapshot.
                if (!_context.IsCurrentGhost(ghost) || !subscription.Query.Matches(ghost))
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

            /// <summary>Stops notifications and releases the callback.</summary>
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
