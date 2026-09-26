using System;
using System.Collections.Generic;

namespace Emas
{
    // A live query subscription with one independent registration per realm.
    internal sealed class GlobalQuerySubscription : IDisposable
    {
        private readonly Query _query;
        private readonly Action<Realm, IGhost> _onEnter;
        private readonly Action<Realm, Key> _onLeave;
        private readonly Dictionary<Realm, Child> _children = new Dictionary<Realm, Child>();
        private bool _disposed;

        internal GlobalQuerySubscription(Query query, Action<Realm, IGhost> onEnter,
            Action<Realm, Key> onLeave)
        {
            _query = query;
            _onEnter = onEnter;
            _onLeave = onLeave;
        }

        internal void Attach(Realm realm)
        {
            if (_disposed || realm.IsDisposed || _children.ContainsKey(realm))
            {
                return;
            }

            Child child = new Child();
            _children.Add(realm, child);
            try
            {
                IDisposable subscription = realm.Subscribe(_query.Rebind(realm),
                    ghost => Enter(realm, child, ghost),
                    _onLeave == null ? (Action<Key>)null : key => Leave(realm, child, key));
                if (_disposed || realm.IsDisposed || !_children.ContainsKey(realm))
                {
                    subscription.Dispose();
                }
                else
                {
                    child.Subscription = subscription;
                }
            }
            catch
            {
                _children.Remove(realm);
                throw;
            }
        }

        internal void Detach(Realm realm)
        {
            Child child;
            if (!_children.TryGetValue(realm, out child))
            {
                return;
            }

            _children.Remove(realm);
            child.Subscription?.Dispose();
            if (_onLeave == null)
            {
                return;
            }

            Key[] departures = new Key[child.Seen.Count];
            child.Seen.CopyTo(departures);
            child.Seen.Clear();
            foreach (Key key in departures)
            {
                if (_disposed)
                {
                    break;
                }

                try
                {
                    _onLeave(realm, key);
                }
                catch (Exception exception)
                {
                    PresenceSource.LogError(exception, "query departure for " + key);
                }
            }
        }

        /// <summary>
        /// Stops observing all realms without reporting departures.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RealmRegistry.Remove(this);
            Child[] children = new Child[_children.Count];
            _children.Values.CopyTo(children, 0);
            _children.Clear();
            foreach (Child child in children)
            {
                child.Subscription?.Dispose();
                child.Seen.Clear();
            }
        }

        private void Enter(Realm realm, Child child, IGhost ghost)
        {
            Child current;
            if (_disposed || !_children.TryGetValue(realm, out current) || current != child)
            {
                return;
            }

            if (_onLeave != null)
            {
                child.Seen.Add(ghost.Key);
            }

            _onEnter(realm, ghost);
        }

        private void Leave(Realm realm, Child child, Key key)
        {
            Child current;
            if (_disposed || !_children.TryGetValue(realm, out current) || current != child
                || !child.Seen.Remove(key))
            {
                return;
            }

            _onLeave(realm, key);
        }

        private sealed class Child
        {
            internal readonly HashSet<Key> Seen = new HashSet<Key>();
            internal IDisposable Subscription;
        }
    }
}
