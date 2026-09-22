using System;
using System.Collections;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Describes filters used to select tracked ghosts.</summary>
    /// <remarks>Immutable filters evaluated on the Unity thread. Results contain only available ghosts, with no ordering guarantee.</remarks>
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

        /// <summary>Restricts the query to a kind.</summary>
        /// <param name="kind">The required kind.</param>
        /// <returns>A query containing the added filter.</returns>
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
        /// <param name="anchorId">The anchor identifier.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query InAnchor(string anchorId)
        {
            return Copy(_partialName, _exactName, anchorId, _kind, _variant, _parts);
        }

        /// <summary>Restricts the query to ghosts exposing an interface.</summary>
        /// <typeparam name="T">The required interface.</typeparam>
        /// <returns>A query containing the added filter.</returns>
        public Query With<T>() where T : class
        {
            var parts = new List<Func<IGhost, bool>>(_parts);
            parts.Add(HasPart<T>);
            return Copy(_partialName, _exactName, _anchorId, _kind, _variant, parts);
        }

        /// <summary>Restricts the query to one display name.</summary>
        /// <param name="name">The exact display name.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query WithExactName(string name)
        {
            return Copy(_partialName, name, _anchorId, _kind, _variant, _parts);
        }

        /// <summary>Restricts the query to one visual variant.</summary>
        /// <param name="variant">The variant identifier.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query WithVariant(Variant variant)
        {
            return Copy(_partialName, _exactName, _anchorId, _kind, variant, _parts);
        }

        /// <summary>Gets the number of current matches.</summary>
        /// <value>The number of currently matching available ghosts.</value>
        public int Count
        {
            get { return Evaluate().Count; }
        }

        /// <summary>Returns exactly one current match.</summary>
        /// <returns>The single matching ghost.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the result is not exactly one ghost.</exception>
        public IGhost Single()
        {
            var matches = Evaluate();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Expected exactly one ghost, but found " + matches.Count + ".");
            }

            return matches[0];
        }

        /// <summary>Returns the first current match, or null.</summary>
        /// <returns>The first match, or null when no match exists.</returns>
        public IGhost FirstOrDefault()
        {
            var matches = Evaluate();
            return matches.Count == 0 ? null : matches[0];
        }

        /// <summary>Subscribes to ghosts that become matches.</summary>
        /// <param name="callback">The callback invoked for current and future matches.</param>
        /// <returns>A subscription that stops future callbacks when disposed.</returns>
        /// <remarks>Current matches may notify synchronously. Each ghost notifies once while it remains a match; recovery can notify again.
        /// Dispose the returned subscription when the consumer stops. Callback exceptions are logged and isolated.</remarks>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public IDisposable OnAvailable(Action<IGhost> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return _realm.Subscribe(this, callback);
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
            get { return _realm; }
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

            for (var index = 0; index < _parts.Count; index++)
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
            return _realm.Evaluate(this);
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
