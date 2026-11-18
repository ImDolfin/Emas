using System;
using System.Collections;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Describes filters used to select tracked ghosts.</summary>
    /// <remarks>
    /// Filters are immutable and results contain available roots, regardless of views or spatial range. Each
    /// enumeration snapshots membership; component data remains live. Results have no ordering guarantee.
    /// Subscriptions report membership changes and must be disposed. All access requires Unity's main thread.
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

        /// <summary>Gets the number of current matches.</summary>
        /// <value>The freshly evaluated number of matching available ghosts; zero when a scoped query's Realm is disposed.</value>
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

        internal Realm Realm
        {
            get
            {
                return _realm;
            }
        }

        internal Query(Realm realm, string partialName)
            : this(realm, partialName, null, null, null, null, new List<Func<IGhost, bool>>())
        {
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

        /// <summary>Creates a query across every live realm, including realms created later.</summary>
        /// <param name="partialName">The optional case-insensitive display-name substring; null leaves names unrestricted. This does not filter entity IDs.</param>
        /// <returns>A query that evaluates all live realms.</returns>
        /// <remarks>
        /// This does not create Realm.Default. Dispose subscriptions when their consumer stops.
        /// </remarks>
        public static Query All(string partialName = null)
        {
            return new Query(null, partialName);
        }

        /// <summary>Restricts the query to a kind.</summary>
        /// <param name="kind">The required kind.</param>
        /// <returns>A new query with this kind selector and all other filters retained.</returns>
        /// <exception cref="ArgumentException">The kind is invalid.</exception>
        public Query OfKind(Kind kind)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("The query kind must be valid.", nameof(kind));
            }

            return Copy(_partialName, _exactName, _anchorId, kind, _variant, _parts);
        }

        /// <summary>Restricts the query to an anchor.</summary>
        /// <param name="anchorId">The case-sensitive anchor identifier, or null to clear this filter.</param>
        /// <returns>A new query with this anchor selector and all other filters retained.</returns>
        public Query InAnchor(string anchorId)
        {
            return Copy(_partialName, _exactName, anchorId, _kind, _variant, _parts);
        }

        /// <summary>Adds a required root contract to the query.</summary>
        /// <typeparam name="T">The interface or root MonoBehaviour class resolved through IGhost.TryGet.</typeparam>
        /// <returns>A query containing the added filter.</returns>
        /// <remarks>
        /// Requires exactly one matching live component on the Ghost root. Disabled components remain eligible;
        /// child views do not supply contracts. Repeated calls add requirements without changing the original query.
        /// </remarks>
        public Query With<T>() where T : class
        {
            // Copy the filters so adding this requirement leaves earlier queries unchanged.
            List<Func<IGhost, bool>> parts = new List<Func<IGhost, bool>>(_parts);
            parts.Add(HasPart<T>);
            return Copy(_partialName, _exactName, _anchorId, _kind, _variant, parts);
        }

        /// <summary>Restricts the query to one display name.</summary>
        /// <param name="name">
        /// The case-insensitive exact display name, or null to clear this filter. Other name filters still apply.
        /// Matches Ghost.Name metadata, independently of Key.EntityId and the root GameObject name.
        /// </param>
        /// <returns>A new query with this exact name selector and all other filters retained.</returns>
        public Query WithExactName(string name)
        {
            return Copy(_partialName, name, _anchorId, _kind, _variant, _parts);
        }

        /// <summary>Restricts the query to one visual variant.</summary>
        /// <param name="variant">The case-sensitive appearance identifier; Variant.None matches unspecified appearances and does not clear this selector.</param>
        /// <returns>A new query with this variant selector and all other filters retained.</returns>
        public Query WithVariant(Variant variant)
        {
            return Copy(_partialName, _exactName, _anchorId, _kind, variant, _parts);
        }

        /// <summary>Returns exactly one current match.</summary>
        /// <returns>The single matching ghost.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the result is not exactly one ghost.</exception>
        /// <remarks>Evaluates current matches on every call. A disposed scoped Realm has zero matches and also causes this exception.</remarks>
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
                // Count every Realm. The same Key in two Realms counts as two matches.
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

        /// <summary>Returns the first current match, or null.</summary>
        /// <returns>The first match, or null when no match exists.</returns>
        /// <remarks>No result ordering is guaranteed. A scoped query whose realm is disposed returns null.</remarks>
        public IGhost FirstOrDefault()
        {
            if (_realm != null)
            {
                return _realm.FirstMatch(this);
            }

            // Copy the current Realm list without creating the default Realm.
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

        /// <summary>Subscribes to ghosts that become matches.</summary>
        /// <param name="callback">The callback invoked once per matching membership period, including current matches; this is not a per-frame data callback.</param>
        /// <returns>A subscription that stops future callbacks when disposed.</returns>
        /// <remarks>
        /// Current matches may notify synchronously. Each ghost notifies once while it remains a match; recovery can notify again.
        /// Dispose the returned subscription when the consumer stops. Callback exceptions are logged and isolated.
        /// Within each realm, initial notifications are deferred while that realm is processing a source callback,
        /// finalizing changes or notifying another subscription.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="ObjectDisposedException">A scoped query's realm was disposed.</exception>
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

        /// <summary>Observes ghosts entering and leaving this query's results.</summary>
        /// <param name="onEnter">Receives each available ghost when it begins matching, including current matches.</param>
        /// <param name="onLeave">Receives the identity when a previously reported ghost is removed, becomes unavailable or stops matching.</param>
        /// <returns>A subscription that cancels both callbacks when disposed.</returns>
        /// <remarks>
        /// Initial entries follow OnAvailable scheduling. Departures precede entries at notification, including loss and
        /// recovery between updates. Keys can outlive destroyed roots; use ObserveWithRealm to distinguish identical
        /// global keys. Disposal cancels pending callbacks without departures, so clear retained state yourself.
        /// Exceptions are logged and isolated; subscriptions created during notification wait for another update.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either callback is null.</exception>
        /// <exception cref="ObjectDisposedException">A scoped query's realm was disposed.</exception>
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

        /// <summary>Observes entries and departures with the owning realm supplied to both callbacks.</summary>
        /// <param name="onEnter">Receives the realm and each available matching ghost.</param>
        /// <param name="onLeave">Receives the realm and identity when a reported ghost leaves.</param>
        /// <returns>A subscription that cancels both callbacks when disposed.</returns>
        /// <remarks>
        /// Use this when different realms may contain the same Key. A global observer also reports
        /// departures when an owning realm is disposed. Disposing this subscription does not report departures.
        /// Notification scheduling and callback failure handling follow <see cref="Observe"/>.
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

        /// <summary>Enumerates a snapshot of the available ghosts matching at the time enumeration is requested.</summary>
        /// <returns>An enumerator over copied membership; the referenced ghosts retain live data and their own tracking lifetimes.</returns>
        /// <remarks>Request enumeration on Unity's main thread. Re-enumerating this Query reevaluates current membership; keeping an enumerator or LINQ materialization does not freeze Ghost data.</remarks>
        public IEnumerator<IGhost> GetEnumerator()
        {
            return Evaluate().GetEnumerator();
        }

        internal bool Matches(IGhost ghost)
        {
            // Check for a destroyed Unity component, which can still look non-null through an interface.
            Ghost root = ghost as Ghost;
            if (root == null || !root.IsAvailable)
            {
                return false;
            }

            // Check the simple metadata first, before searching the root's components.
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

        internal Query Rebind(Realm realm)
        {
            return new Query(realm, _partialName, _exactName, _anchorId, _kind, _variant, _parts);
        }

        /// <summary>Enumerates the same copied membership snapshot through the nongeneric collection interface.</summary>
        /// <returns>An enumerator over the matching Ghost handles captured for this enumeration.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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

            // Reuse one temporary list for each Realm and copy its matches into the global result.
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

        private Query Copy(string partialName, string exactName, string anchorId, Kind? kind, Variant? variant, List<Func<IGhost, bool>> parts)
        {
            return new Query(_realm, partialName, exactName, anchorId, kind, variant, new List<Func<IGhost, bool>>(parts));
        }
    }
}
