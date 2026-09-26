using System;
using System.Collections.Generic;

namespace Emas
{
    // Tracks live realms and global query subscriptions on Unity's main thread.
    internal static class RealmRegistry
    {
        private static readonly List<Realm> Realms = new List<Realm>();
        private static Realm[] _snapshot = Array.Empty<Realm>();
        private static readonly List<GlobalQuerySubscription> Subscriptions =
            new List<GlobalQuerySubscription>();

        internal static Realm[] Snapshot()
        {
            return _snapshot;
        }

        internal static void Register(Realm realm)
        {
            Realms.Add(realm);
            _snapshot = Realms.ToArray();
            GlobalQuerySubscription[] subscriptions = Subscriptions.ToArray();
            foreach (GlobalQuerySubscription subscription in subscriptions)
            {
                subscription.Attach(realm);
            }
        }

        internal static void Unregister(Realm realm)
        {
            Realms.Remove(realm);
            _snapshot = Realms.ToArray();
            GlobalQuerySubscription[] subscriptions = Subscriptions.ToArray();
            foreach (GlobalQuerySubscription subscription in subscriptions)
            {
                subscription.Detach(realm);
            }
        }

        internal static IDisposable Subscribe(Query query, Action<Realm, IGhost> onEnter,
            Action<Realm, Key> onLeave)
        {
            GlobalQuerySubscription subscription =
                new GlobalQuerySubscription(query, onEnter, onLeave);
            Subscriptions.Add(subscription);
            foreach (Realm realm in Snapshot())
            {
                subscription.Attach(realm);
            }

            return subscription;
        }

        internal static void Remove(GlobalQuerySubscription subscription)
        {
            Subscriptions.Remove(subscription);
        }
    }
}
