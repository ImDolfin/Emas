using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Publishes a complete source snapshot and removes entities absent from the next successful snapshot.</summary>
    /// <typeparam name="TSource">The source item type.</typeparam>
    /// <typeparam name="TGhost">The application ghost component.</typeparam>
    /// <remarks>Configure while detached on the Unity thread. Reads on startup and each update. Use full snapshots, not delta batches.</remarks>
    public sealed class PollingPresenceSource<TSource, TGhost> : PresenceSource where TGhost : Ghost
    {
        private bool _polling;
        private readonly Kind _kind;
        private Func<IEnumerable<TSource>> _read;
        private Func<TSource, string> _identify;
        private Action<TSource, TGhost> _apply;
        private Func<TSource, Variant> _variant;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Creates a polling source for one entity kind. Configure its callbacks before tracking.</summary>
        /// <param name="kind">The kind assigned to every ghost from this source.</param>
        /// <exception cref="ArgumentException">The kind is empty or invalid.</exception>
        public PollingPresenceSource(Kind kind)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("A polling source requires a valid kind.", nameof(kind));
            }
            _kind = kind;
        }

        /// <summary>Sets the callback that reads the complete current population on startup and each update.</summary>
        /// <param name="read">Returns all current items; an empty collection removes the population, null is an error.</param>
        /// <returns>This source for further configuration.</returns>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="InvalidOperationException">The source is attached or a read/subscription is still executing.</exception>
        public PollingPresenceSource<TSource, TGhost> ReadFrom(Func<IEnumerable<TSource>> read)
        {
            ThrowIfConfiguringWhileTracking();
            _read = read ?? throw new ArgumentNullException(nameof(read));
            return this;
        }

        /// <summary>Sets the stable identity selector used to match items to existing ghosts.</summary>
        /// <param name="identify">Returns a non-empty ID unique within each read.</param>
        /// <returns>This source for further configuration.</returns>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="InvalidOperationException">The source is attached or a read/subscription is still executing.</exception>
        public PollingPresenceSource<TSource, TGhost> IdentifyBy(Func<TSource, string> identify)
        {
            ThrowIfConfiguringWhileTracking();
            _identify = identify ?? throw new ArgumentNullException(nameof(identify));
            return this;
        }

        /// <summary>Sets the callback that copies each source item's data into its ghost.</summary>
        /// <param name="apply">Receives the source item first and its stable ghost second.</param>
        /// <returns>This source for further configuration.</returns>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="InvalidOperationException">The source is attached or a read/subscription is still executing.</exception>
        public PollingPresenceSource<TSource, TGhost> Apply(Action<TSource, TGhost> apply)
        {
            ThrowIfConfiguringWhileTracking();
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            return this;
        }

        /// <summary>Optionally selects each ghost's appearance. Omit this step to preserve existing appearances.</summary>
        /// <param name="variant">Returns the appearance for an item; Variant.None clears its appearance.</param>
        /// <returns>This source for further configuration.</returns>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="InvalidOperationException">The source is attached or a read/subscription is still executing.</exception>
        public PollingPresenceSource<TSource, TGhost> WithVariant(Func<TSource, Variant> variant)
        {
            ThrowIfConfiguringWhileTracking();
            _variant = variant ?? throw new ArgumentNullException(nameof(variant));
            return this;
        }

        private void ThrowIfConfiguringWhileTracking()
        {
            if (IsAttached || _polling)
            {
                throw new InvalidOperationException("Configure polling callbacks before attaching the source to an anchor.");
            }
        }

        /// <inheritdoc />
        protected override void OnStart()
        {
            var missing = new List<string>();
            if (_read == null)
            {
                missing.Add(nameof(ReadFrom));
            }
            if (_identify == null)
            {
                missing.Add(nameof(IdentifyBy));
            }
            if (_apply == null)
            {
                missing.Add(nameof(Apply));
            }
            if (missing.Count > 0)
            {
                throw new InvalidOperationException("Polling source is missing required steps: "
                    + string.Join(", ", missing) + ". Configure them before tracking.");
            }
            Poll();
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            Poll();
        }

        private void Poll()
        {
            if (_polling)
            {
                return;
            }
            _polling = true;
            var generation = RegistrationGeneration;
            _seen.Clear();
            _entries.Clear();
            try
            {
                var snapshot = _read();
                if (snapshot == null)
                {
                    throw new InvalidOperationException("A polling source must return a complete snapshot, not null.");
                }
                foreach (var item in snapshot)
                {
                    var id = _identify(item);
                    if (string.IsNullOrEmpty(id) || !_seen.Add(id))
                    {
                        throw new InvalidOperationException("A polling snapshot contains an empty or duplicate entity ID.");
                    }
                    _entries.Add(new Entry(item, id, _variant == null ? (Variant?)null : _variant(item)));
                }
                foreach (var entry in _entries)
                {
                    if (!IsActive || RegistrationGeneration != generation)
                    {
                        return;
                    }
                    var ghost = GetOrCreate<TGhost>(entry.Id, _kind, entry.Variant);
                    _apply(entry.Item, ghost);
                }
                // Deletions happen only after the full read and all mapping callbacks succeed.
                foreach (var ghost in OwnedGhosts)
                {
                    if (!IsActive || RegistrationGeneration != generation)
                    {
                        return;
                    }
                    if (ghost.Key.Kind != _kind || !_seen.Contains(ghost.Key.EntityId))
                    {
                        Remove(ghost.Key.Kind, ghost.Key.EntityId);
                    }
                }
            }
            finally
            {
                _entries.Clear();
                _seen.Clear();
                _polling = false;
            }
        }

        private struct Entry
        {
            internal Entry(TSource item, string id, Variant? variant)
            {
                Item = item;
                Id = id;
                Variant = variant;
            }

            internal readonly TSource Item;
            internal readonly string Id;
            internal readonly Variant? Variant;
        }
    }
}
