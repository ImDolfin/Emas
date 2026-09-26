using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Publishes a complete source snapshot and removes entities absent from the next successful snapshot.
    /// </summary>
    /// <typeparam name="TSource">
    /// The source item type.
    /// </typeparam>
    /// <typeparam name="TGhost">
    /// The application ghost component.
    /// </typeparam>
    /// <remarks>
    /// Configure while detached on the Unity thread. Reads on startup and every update unless PollEvery sets an interval. Use full snapshots, not delta batches.
    /// </remarks>
    public sealed class PollingPresenceDetector<TSource, TGhost> : PresenceDetector where TGhost : Ghost
    {
        private bool _polling;
        private readonly Kind _kind;
        private readonly Func<double> _elapsedSeconds;
        private double _intervalSeconds;
        private double _nextPollTime;
        private Func<IEnumerable<TSource>> _read;
        private Func<TSource, string> _identify;
        private Action<TSource, TGhost> _apply;
        private Func<TSource, Variant> _variant;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<IGhost> _ownedGhosts = new List<IGhost>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Creates a polling source for one entity kind. Configure its callbacks before tracking.
        /// </summary>
        /// <param name="kind">
        /// The kind assigned to every ghost from this source.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The kind is empty or invalid.
        /// </exception>
        public PollingPresenceDetector(Kind kind)
            : this(kind, () => Time.realtimeSinceStartupAsDouble)
        {
        }

        internal PollingPresenceDetector(Kind kind, Func<double> elapsedSeconds)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("A polling source requires a valid kind.", nameof(kind));
            }

            _kind = kind;
            _elapsedSeconds = elapsedSeconds ?? throw new ArgumentNullException(nameof(elapsedSeconds));
        }

        /// <summary>
        /// Sets the callback that reads the complete current population on startup and each scheduled poll.
        /// </summary>
        /// <param name="read">
        /// Returns all current items; an empty collection removes the population, null is an error.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public PollingPresenceDetector<TSource, TGhost> ReadFrom(Func<IEnumerable<TSource>> read)
        {
            ThrowIfConfiguringWhileTracking();
            _read = read ?? throw new ArgumentNullException(nameof(read));
            return this;
        }

        /// <summary>
        /// Sets the stable identity selector used to match items to existing ghosts.
        /// </summary>
        /// <param name="identify">
        /// Returns a non-empty ID unique within each read.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public PollingPresenceDetector<TSource, TGhost> IdentifyBy(Func<TSource, string> identify)
        {
            ThrowIfConfiguringWhileTracking();
            _identify = identify ?? throw new ArgumentNullException(nameof(identify));
            return this;
        }

        /// <summary>
        /// Sets the callback that copies each source item's data into its ghost.
        /// </summary>
        /// <param name="apply">
        /// Receives the source item first and its stable ghost second.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public PollingPresenceDetector<TSource, TGhost> Apply(Action<TSource, TGhost> apply)
        {
            ThrowIfConfiguringWhileTracking();
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            return this;
        }

        /// <summary>
        /// Optionally selects each ghost's appearance. Omit this step to preserve existing appearances.
        /// </summary>
        /// <param name="variant">
        /// Returns the appearance for an item; Variant.None clears its appearance.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public PollingPresenceDetector<TSource, TGhost> WithVariant(Func<TSource, Variant> variant)
        {
            ThrowIfConfiguringWhileTracking();
            _variant = variant ?? throw new ArgumentNullException(nameof(variant));
            return this;
        }

        /// <summary>
        /// Sets the minimum elapsed time between polls after the immediate startup read.
        /// </summary>
        /// <param name="interval">
        /// A non-negative interval. Zero, the default, polls on every realm update.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <remarks>
        /// Uses unscaled real time on Unity's main thread. Positive intervals run on the first realm update
        /// at or after the deadline, with at most one poll per update and no catch-up reads. The next deadline
        /// is measured from the start of the actual poll. Every attachment, including restart, reads immediately
        /// and resets the deadline. Ghost data and membership remain unchanged between polls.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The interval is negative.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read is still executing.
        /// </exception>
        public PollingPresenceDetector<TSource, TGhost> PollEvery(TimeSpan interval)
        {
            ThrowIfConfiguringWhileTracking();
            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), "The polling interval must be non-negative.");
            }

            _intervalSeconds = interval.TotalSeconds;
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
            List<string> missing = new List<string>();
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

            // A fresh attachment always reads now, regardless of its previous deadline.
            _nextPollTime = _intervalSeconds > 0 ? _elapsedSeconds() + _intervalSeconds : 0;
            Poll();
        }

        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (_intervalSeconds > 0)
            {
                double now = _elapsedSeconds();
                if (now < _nextPollTime)
                {
                    return;
                }

                // Schedule from this read, so a delayed update cannot cause a catch-up burst.
                _nextPollTime = now + _intervalSeconds;
            }

            Poll();
        }

        private void Poll()
        {
            if (_polling)
            {
                return;
            }

            _polling = true;
            long generation = RegistrationGeneration;
            Realm realm = Anchor.Realm;
            string sourceContext = CaptureErrorContext();
            string operation = nameof(ReadFrom);
            string entityId = null;
            _seen.Clear();
            _entries.Clear();
            try
            {
                // Validate the complete population before applying any changes to ghosts.
                IEnumerable<TSource> snapshot = _read();
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                if (snapshot == null)
                {
                    throw new InvalidOperationException("A polling source must return a complete snapshot, not null.");
                }

                foreach (TSource item in snapshot)
                {
                    operation = nameof(IdentifyBy);
                    entityId = _identify(item);
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(entityId) || !_seen.Add(entityId))
                    {
                        throw new InvalidOperationException("A polling snapshot contains an empty or duplicate entity ID.");
                    }

                    operation = nameof(WithVariant);
                    Variant? variant = _variant == null ? (Variant?)null : _variant(item);
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    _entries.Add(new Entry(item, entityId, variant));
                    // Enumerator failures belong to the read, not to the preceding selector.
                    operation = nameof(ReadFrom);
                    entityId = null;
                }

                // Map every item before deciding which existing ghosts have departed.
                foreach (Entry entry in _entries)
                {
                    if (!IsActive || RegistrationGeneration != generation)
                    {
                        return;
                    }

                    operation = "GetOrCreate";
                    entityId = entry.Id;
                    TGhost ghost = GetOrCreate<TGhost>(entry.Id, _kind, entry.Variant);
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    operation = nameof(Apply);
                    _apply(entry.Item, ghost);
                }

                // Deletions happen only after the full read and all mapping callbacks succeed.
                realm.GetOwnedGhosts(this, _ownedGhosts);
                foreach (IGhost ghost in _ownedGhosts)
                {
                    if (!IsActive || RegistrationGeneration != generation)
                    {
                        return;
                    }

                    if (ghost.Key.Kind != _kind || !_seen.Contains(ghost.Key.EntityId))
                    {
                        operation = "Remove";
                        entityId = ghost.Key.EntityId;
                        Disappear(ghost.Key.Kind, ghost.Key.EntityId);
                    }
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(sourceContext, operation, _kind, entityId));
                throw;
            }
            finally
            {
                _entries.Clear();
                _ownedGhosts.Clear();
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
