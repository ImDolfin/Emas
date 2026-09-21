using System;
using System.Collections;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Describes filters used to select tracked ghosts.</summary>
    public sealed class Query : IEnumerable<IGhost>
    {
        private readonly Context _context;
        private readonly string _partialName;
        private readonly string _exactName;
        private readonly string _originId;
        private readonly Kind? _kind;
        private readonly Variant? _variant;
        private readonly List<Func<IGhost, bool>> _parts;

        internal Query(Context context, string partialName)
            : this(context, partialName, null, null, null, null, new List<Func<IGhost, bool>>())
        {
        }

        private Query(Context context, string partialName, string exactName, string originId, Kind? kind, Variant? variant, List<Func<IGhost, bool>> parts)
        {
            _context = context;
            _partialName = partialName;
            _exactName = exactName;
            _originId = originId;
            _kind = kind;
            _variant = variant;
            _parts = parts;
        }

        /// <summary>Restricts the query to a kind.</summary>
        /// <param name="kind">The required kind.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query OfKind(Kind kind)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("The query kind must be valid.", nameof(kind));
            }

            return Copy(_partialName, _exactName, _originId, kind, _variant, _parts);
        }

        /// <summary>Restricts the query to an origin.</summary>
        /// <param name="originId">The origin identifier.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query InOrigin(string originId)
        {
            return Copy(_partialName, _exactName, originId, _kind, _variant, _parts);
        }

        /// <summary>Restricts the query to ghosts exposing an interface.</summary>
        /// <typeparam name="T">The required interface.</typeparam>
        /// <returns>A query containing the added filter.</returns>
        public Query With<T>() where T : class
        {
            var parts = new List<Func<IGhost, bool>>(_parts);
            parts.Add(HasPart<T>);
            return Copy(_partialName, _exactName, _originId, _kind, _variant, parts);
        }

        /// <summary>Restricts the query to one display name.</summary>
        /// <param name="name">The exact display name.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query WithExactName(string name)
        {
            return Copy(_partialName, name, _originId, _kind, _variant, _parts);
        }

        /// <summary>Restricts the query to one visual variant.</summary>
        /// <param name="variant">The variant identifier.</param>
        /// <returns>A query containing the added filter.</returns>
        public Query WithVariant(Variant variant)
        {
            return Copy(_partialName, _exactName, _originId, _kind, variant, _parts);
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
        public IDisposable OnAvailable(Action<IGhost> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return _context.Subscribe(this, callback);
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

        internal Context Context
        {
            get { return _context; }
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

            if (_originId != null && !string.Equals(ghost.Key.OriginId, _originId, StringComparison.Ordinal))
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
            return _context.Evaluate(this);
        }

        internal Query Rebind(Context context)
        {
            return new Query(context, _partialName, _exactName, _originId, _kind, _variant, _parts);
        }
        private Query Copy(string partialName, string exactName, string originId, Kind? kind, Variant? variant, List<Func<IGhost, bool>> parts)
        {
            return new Query(_context, partialName, exactName, originId, kind, variant, new List<Func<IGhost, bool>>(parts));
        }
    }
}
