using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Reports complete SDK snapshots without creating Ghosts or mapping their data.</summary>
    /// <typeparam name="TSource">The SDK item type.</typeparam>
    /// <remarks>All operations run on Unity's main thread. Each read must contain the full current population.</remarks>
    public sealed class PollingPresenceDetector<TSource> : PresenceDetector
    {
        private readonly Kind _kind;
        private readonly Func<double> _elapsedSeconds;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private Func<IEnumerable<TSource>> _read;
        private Func<TSource, string> _identify;
        private Func<TSource, string> _name;
        private Func<TSource, Variant> _variant;
        private Func<TSource, IEnumerable<Type>> _capabilities;
        private double _intervalSeconds;
        private double _nextPollTime;
        private bool _polling;

        /// <summary>Creates a detector for one entity kind.</summary>
        /// <param name="kind">The kind assigned to every detected item.</param>
        public PollingPresenceDetector(Kind kind)
            : this(kind, () => Time.realtimeSinceStartupAsDouble)
        {
        }

        internal PollingPresenceDetector(Kind kind, Func<double> elapsedSeconds)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("A polling detector requires a valid kind.", nameof(kind));
            }

            _kind = kind;
            _elapsedSeconds = elapsedSeconds ?? throw new ArgumentNullException(nameof(elapsedSeconds));
        }

        /// <summary>Sets the complete-population reader.</summary>
        /// <param name="read">Returns current SDK items; an empty collection reports all previous IDs missing.</param>
        /// <returns>This detector.</returns>
        public PollingPresenceDetector<TSource> ReadFrom(Func<IEnumerable<TSource>> read)
        {
            ThrowIfConfiguring();
            _read = read ?? throw new ArgumentNullException(nameof(read));
            return this;
        }

        /// <summary>Sets the stable identity selector.</summary>
        /// <param name="identify">Returns a nonempty ID unique within each snapshot.</param>
        /// <returns>This detector.</returns>
        public PollingPresenceDetector<TSource> IdentifyBy(Func<TSource, string> identify)
        {
            ThrowIfConfiguring();
            _identify = identify ?? throw new ArgumentNullException(nameof(identify));
            return this;
        }

        /// <summary>Optionally selects the entity display name.</summary>
        /// <param name="name">Returns a label; null retains the prior label.</param>
        /// <returns>This detector.</returns>
        public PollingPresenceDetector<TSource> WithName(Func<TSource, string> name)
        {
            ThrowIfConfiguring();
            _name = name ?? throw new ArgumentNullException(nameof(name));
            return this;
        }

        /// <summary>Optionally selects the entity's visual variant.</summary>
        /// <param name="variant">Returns a variant; Variant.None clears the prior variant.</param>
        /// <returns>This detector.</returns>
        public PollingPresenceDetector<TSource> WithVariant(Func<TSource, Variant> variant)
        {
            ThrowIfConfiguring();
            _variant = variant ?? throw new ArgumentNullException(nameof(variant));
            return this;
        }

        /// <summary>Optionally selects capability interfaces reported by the SDK.</summary>
        /// <param name="capabilities">Returns interface types; null retains prior capabilities.</param>
        /// <returns>This detector.</returns>
        public PollingPresenceDetector<TSource> WithCapabilities(Func<TSource, IEnumerable<Type>> capabilities)
        {
            ThrowIfConfiguring();
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            return this;
        }

        /// <summary>Sets the minimum time between polls after an immediate startup read.</summary>
        /// <param name="interval">A nonnegative interval; zero polls on every realm update.</param>
        /// <returns>This detector.</returns>
        /// <remarks>A delayed update performs one poll, without catch-up reads.</remarks>
        public PollingPresenceDetector<TSource> PollEvery(TimeSpan interval)
        {
            ThrowIfConfiguring();
            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), "The polling interval must be non-negative.");
            }

            _intervalSeconds = interval.TotalSeconds;
            return this;
        }

        private void ThrowIfConfiguring()
        {
            if (IsAttached || _polling)
            {
                throw new InvalidOperationException("Configure polling detector callbacks before attaching to an anchor.");
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

            if (missing.Count > 0)
            {
                throw new InvalidOperationException("Polling detector is missing required steps: "
                    + string.Join(", ", missing) + ". Configure them before tracking.");
            }

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
            string context = CaptureErrorContext();
            string operation = nameof(ReadFrom);
            string entityId = null;
            _seen.Clear();
            _entries.Clear();
            try
            {
                IEnumerable<TSource> snapshot = _read();
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                if (snapshot == null)
                {
                    throw new InvalidOperationException("A polling detector must return a complete snapshot, not null.");
                }

                foreach (TSource item in snapshot)
                {
                    if ((object)item == null)
                    {
                        throw new InvalidOperationException("A polling detector cannot report a null item.");
                    }

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

                    operation = nameof(WithName);
                    string name = _name == null ? null : _name(item);
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    operation = nameof(WithVariant);
                    Variant? variant = _variant == null ? (Variant?)null : _variant(item);
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    operation = nameof(WithCapabilities);
                    List<Type> capabilities = SnapshotCapabilities(_capabilities == null ? null : _capabilities(item));
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    _entries.Add(new Entry(item, entityId, name, variant, capabilities));
                    operation = nameof(ReadFrom);
                    entityId = null;
                }

                foreach (Entry entry in _entries)
                {
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    operation = "Report";
                    entityId = entry.Id;
                    Report(entry.Id, _kind, entry.Item, entry.Name, entry.Variant, entry.Capabilities);
                }

                IReadOnlyList<Presence> owned = OwnedPresences;
                foreach (Presence presence in owned)
                {
                    if (!IsRegistration(realm, generation))
                    {
                        return;
                    }

                    if (presence.Key.Kind != _kind || !_seen.Contains(presence.Key.EntityId))
                    {
                        operation = "Disappear";
                        entityId = presence.Key.EntityId;
                        Disappear(presence.Key.Kind, presence.Key.EntityId);
                    }
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(context, operation, _kind, entityId));
                throw;
            }
            finally
            {
                _entries.Clear();
                _seen.Clear();
                _polling = false;
            }
        }

        private static List<Type> SnapshotCapabilities(IEnumerable<Type> selected)
        {
            if (selected == null)
            {
                return null;
            }

            List<Type> snapshot = new List<Type>();
            foreach (Type capability in selected)
            {
                if (capability == null || !capability.IsInterface)
                {
                    throw new ArgumentException("Presence capabilities must be non-null interface types.", nameof(selected));
                }

                if (!snapshot.Contains(capability))
                {
                    snapshot.Add(capability);
                }
            }

            return snapshot;
        }

        private struct Entry
        {
            internal Entry(TSource item, string id, string name, Variant? variant, List<Type> capabilities)
            {
                Item = item;
                Id = id;
                Name = name;
                Variant = variant;
                Capabilities = capabilities;
            }

            internal readonly TSource Item;
            internal readonly string Id;
            internal readonly string Name;
            internal readonly Variant? Variant;
            internal readonly List<Type> Capabilities;
        }
    }
}
