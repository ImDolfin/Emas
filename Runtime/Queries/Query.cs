using System;
using System.Collections;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Describes filters used to select tracked ghosts.
    /// </summary>
    /// <remarks>
    /// Immutable filters evaluated on the Unity thread. Results contain only available ghosts, with no ordering guarantee.
    /// </remarks>
    public sealed class Query : IEnumerable<IGhost>
    {
        private readonly Realm _realm;
        private readonly string _partialName;
        private readonly string _exactName;
        private readonly string _anchorId;
        private readonly Kind? _kind;
        private readonly Variant? _variant;
        private readonly List<Func<IGhost, bool>> _parts;

        internal Query(Realm realm, string partialName)
            : this(realm, partialName, null, null, null, null, new List<Func<IGhost, bool>>())
        {
        }

        /// <summary>
        /// Creates a query across every live realm, including realms created later.
        /// </summary>
        /// <param name="partialName">
        /// The optional case-insensitive partial display name.
        /// </param>
        /// <returns>A query that evaluates all live realms.</returns>
        /// <remarks>
        /// This does not create Realm.Default. Dispose subscriptions when their consumer stops.
        /// </remarks>
        public static Query All(string partialName = null)
        {
            return new Query(null, partialName);
        }

        private Query(Realm realm, string partialName, string exactName, string anchorId, Kind? kind, Variant? variant, List<Func<IGhost, bool>> parts)
        {
            _realm = realm;
            _partialName = partialName;
            _exactName = exactName;
            _anchorId = anchorId;
            _kind = kind;
            _variant = variant;
            _parts = parts;
        }

        /// <summary>
        /// Restricts the query to a kind.
        /// </summary>
        /// <param name="kind">
        /// The required kind.
        /// </param>
        /// <returns>
        /// A query containing the added filter.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The kind is invalid.
        /// </exception>
        public Query OfKind(Kind kind)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("The query kind must be valid.", nameof(kind));
            }

            return Copy(_partialName, _exactName, _anchorId, kind, _variant, _parts);
        }

        /// <summary>
        /// Restricts the query to an anchor.
        /// </summary>
        /// <param name="anchorId">
        /// The anchor identifier.
        /// </param>
        /// <returns>
        /// A query containing the added filter.
        /// </returns>
        public Query InAnchor(string anchorId)
        {
            return Copy(_partialName, _exactName, anchorId, _kind, _variant, _parts);
        }

        /// <summary>
        /// Restricts the query to ghosts exposing an interface.
        /// </summary>
        /// <typeparam name="T">
        /// The required interface.
        /// </typeparam>
        /// <returns>
        /// A query containing the added filter.
        /// </returns>
        public Query With<T>() where T : class
        {
            List<Func<IGhost, bool>> parts = new List<Func<IGhost, bool>>(_parts);
            parts.Add(HasPart<T>);
            return Copy(_partialName, _exactName, _anchorId, _kind, _variant, parts);
        }

        /// <summary>
        /// Restricts the query to one display name.
        /// </summary>
        /// <param name="name">
        /// The exact display name.
        /// </param>
        /// <returns>
        /// A query containing the added filter.
        /// </returns>
        public Query WithExactName(string name)
        {
            return Copy(_partialName, name, _anchorId, _kind, _variant, _parts);
        }

        /// <summary>
        /// Restricts the query to one visual variant.
        /// </summary>
        /// <param name="variant">
        /// The variant identifier.
        /// </param>
        /// <returns>
        /// A query containing the added filter.
        /// </returns>
        public Query WithVariant(Variant variant)
        {
            return Copy(_partialName, _exactName, _anchorId, _kind, variant, _parts);
        }

        /// <summary>
        /// Gets the number of current matches.
        /// </summary>
        /// <value>
        /// The number of currently matching available ghosts.
        /// </value>
        public int Count
        {
            get
            {
                IGhost ignored;
                if (_realm != null)
                {
                    return _realm.CountMatches(this, out ignored);
                }

                int total = 0;
                foreach (Realm realm in RealmRegistry.Snapshot())
                {
                    if (!realm.IsDisposed)
                    {
                        total += realm.CountMatches(this, out ignored);
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// Returns exactly one current match.
        /// </summary>
        /// <returns>
        /// The single matching ghost.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the result is not exactly one ghost.
        /// </exception>
        public IGhost Single()
        {
            IGhost first;
            int count = 0;
            if (_realm != null)
            {
                count = _realm.CountMatches(this, out first);
            }
            else
            {
                first = null;
                foreach (Realm realm in RealmRegistry.Snapshot())
                {
                    if (realm.IsDisposed)
                    {
                        continue;
                    }

                    IGhost candidate;
                    count += realm.CountMatches(this, out candidate);
                    if (first == null)
                    {
                        first = candidate;
                    }
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException("Expected exactly one ghost, but found " + count + ".");
            }

            return first;
        }

        /// <summary>
        /// Returns the first current match, or null.
        /// </summary>
        /// <returns>
        /// The first match, or null when no match exists.
        /// </returns>
        public IGhost FirstOrDefault()
        {
            if (_realm != null)
            {
                return _realm.FirstMatch(this);
            }

            foreach (Realm realm in RealmRegistry.Snapshot())
            {
                if (realm.IsDisposed)
                {
                    continue;
                }

                IGhost first = realm.FirstMatch(this);
                if (first != null)
                {
                    return first;
                }
            }

            return null;
        }

        /// <summary>
        /// Subscribes to ghosts that become matches.
        /// </summary>
        /// <param name="callback">
        /// The callback invoked for current and future matches.
        /// </param>
        /// <returns>
        /// A subscription that stops future callbacks when disposed.
        /// </returns>
        /// <remarks>
        /// Current matches may notify synchronously. Each ghost notifies once while it remains a match; recovery can notify again.
        /// Dispose the returned subscription when the consumer stops. Callback exceptions are logged and isolated.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// A scoped query's realm was disposed.
        /// </exception>
        public IDisposable OnAvailable(Action<IGhost> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (_realm != null)
            {
                return _realm.Subscribe(this, callback);
            }

            return RealmRegistry.Subscribe(this, (realm, ghost) => callback(ghost), null);
        }

        /// <summary>
        /// Observes ghosts entering and leaving this query's results.
        /// </summary>
        /// <param name="onEnter">
        /// Receives each available ghost when it begins matching, including current matches.
        /// </param>
        /// <param name="onLeave">
        /// Receives the identity when a previously reported ghost is removed, becomes unavailable or stops matching.
        /// </param>
        /// <returns>
        /// A subscription that cancels both callbacks when disposed.
        /// </returns>
        /// <remarks>
        /// Initial entries follow OnAvailable scheduling. Departures run in the realm update notification phase, before entries
        /// for this subscription. A departure supplies a Key because the Unity object may already be destroyed.
        /// Availability loss and recovery between updates still produce a departure followed by a fresh entry.
        /// Filter changes are observed at notification time. For a global query, realm disposal reports departures;
        /// duplicate Keys from different realms are indistinguishable here, so use ObserveWithRealm when needed.
        /// Disposing the subscription or a scoped query's realm cancels pending
        /// notifications without synthesizing departures; consumers must release their own retained state.
        /// Callback exceptions are logged and isolated. Subscriptions created inside notifications wait for another update.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Either callback is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// A scoped query's realm was disposed.
        /// </exception>
        public IDisposable Observe(Action<IGhost> onEnter, Action<Key> onLeave)
        {
            if (onEnter == null)
            {
                throw new ArgumentNullException(nameof(onEnter));
            }

            if (onLeave == null)
            {
                throw new ArgumentNullException(nameof(onLeave));
            }

            if (_realm != null)
            {
                return _realm.Subscribe(this, onEnter, onLeave);
            }

            return RealmRegistry.Subscribe(this, (realm, ghost) => onEnter(ghost),
                (realm, key) => onLeave(key));
        }

        /// <summary>
        /// Observes entries and departures with the owning realm supplied to both callbacks.
        /// </summary>
        /// <param name="onEnter">Receives the realm and each available matching ghost.</param>
        /// <param name="onLeave">Receives the realm and identity when a reported ghost leaves.</param>
        /// <returns>A subscription that cancels both callbacks when disposed.</returns>
        /// <remarks>
        /// Use this when different realms may contain the same Key. A global observer also reports
        /// departures when an owning realm is disposed. Disposing this subscription does not report departures.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either callback is null.</exception>
        /// <exception cref="ObjectDisposedException">A scoped query's realm was disposed.</exception>
        public IDisposable ObserveWithRealm(Action<Realm, IGhost> onEnter, Action<Realm, Key> onLeave)
        {
            if (onEnter == null)
            {
                throw new ArgumentNullException(nameof(onEnter));
            }

            if (onLeave == null)
            {
                throw new ArgumentNullException(nameof(onLeave));
            }

            if (_realm != null)
            {
                return _realm.Subscribe(this, ghost => onEnter(_realm, ghost),
                    key => onLeave(_realm, key));
            }

            return RealmRegistry.Subscribe(this, onEnter, onLeave);
        }

        /// <inheritdoc />
        public IEnumerator<IGhost> GetEnumerator()
        {
            return Evaluate().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal Realm Realm
        {
            get
            {
                return _realm;
            }
        }

        internal bool Matches(IGhost ghost)
        {
            if (ghost == null || !ghost.IsAvailable)
            {
                return false;
            }

            if (_partialName != null && (ghost.Name == null || ghost.Name.IndexOf(_partialName, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            if (_exactName != null && !string.Equals(ghost.Name, _exactName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_anchorId != null && !string.Equals(ghost.Key.AnchorId, _anchorId, StringComparison.Ordinal))
            {
                return false;
            }

            if (_kind.HasValue && ghost.Key.Kind != _kind.Value)
            {
                return false;
            }

            if (_variant.HasValue && ghost.Variant != _variant.Value)
            {
                return false;
            }

            for (int index = 0; index < _parts.Count; index++)
            {
                if (!_parts[index](ghost))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasPart<T>(IGhost ghost) where T : class
        {
            T part;
            return ghost.TryGet<T>(out part);
        }

        private List<IGhost> Evaluate()
        {
            if (_realm != null)
            {
                return _realm.Evaluate(this);
            }

            List<IGhost> result = new List<IGhost>();
            List<IGhost> current = new List<IGhost>();
            foreach (Realm realm in RealmRegistry.Snapshot())
            {
                if (!realm.IsDisposed)
                {
                    realm.Evaluate(this, current);
                    result.AddRange(current);
                }
            }

            return result;
        }

        internal Query Rebind(Realm realm)
        {
            return new Query(realm, _partialName, _exactName, _anchorId, _kind, _variant, _parts);
        }

        private Query Copy(string partialName, string exactName, string anchorId, Kind? kind, Variant? variant, List<Func<IGhost, bool>> parts)
        {
            return new Query(_realm, partialName, exactName, anchorId, kind, variant, new List<Func<IGhost, bool>>(parts));
        }
    }
}
