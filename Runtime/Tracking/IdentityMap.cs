using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Keeps one current record per identity within a realm.</summary>
    internal sealed class IdentityMap
    {
        private readonly Dictionary<Key, Record> _records = new Dictionary<Key, Record>();
        private IReadOnlyList<Record> _snapshot;

        internal Dictionary<Key, Record>.ValueCollection Values
        {
            get
            {
                return _records.Values;
            }
        }

        internal void Add(Record record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            // Add must fail if another root already has this Key.
            _records.Add(record.Key, record);
            record.IdentityMap = this;
            _snapshot = null;
        }

        internal bool TryGetValue(Key key, out Record record)
        {
            return _records.TryGetValue(key, out record);
        }

        internal Record Find(IGhost ghost)
        {
            if (ghost == null)
            {
                return null;
            }

            Record record;
            if (!_records.TryGetValue(ghost.Key, out record))
            {
                return null;
            }

            // An old root must not find its replacement just because they share a Key.
            return ReferenceEquals(record.Ghost, ghost) ? record : null;
        }

        internal bool Remove(Record record)
        {
            if (!Contains(record))
            {
                return false;
            }

            // Cleanup for an old root must not remove its replacement.
            bool removed = _records.Remove(record.Key);
            if (removed)
            {
                record.IdentityMap = null;
            }
            _snapshot = null;
            return removed;
        }

        internal bool Contains(Record record)
        {
            // Records are inserted and removed only through this map. The membership
            // token avoids hashing the identity before every application callback and
            // rejects old records even if a replacement has exactly the same Key.
            return record != null && ReferenceEquals(record.IdentityMap, this);
        }

        internal IReadOnlyList<Record> Snapshot()
        {
            // Membership changes replace the snapshot. Callbacks can keep iterating the old one safely.
            if (_snapshot == null)
            {
                _snapshot = new List<Record>(_records.Values);
            }

            return _snapshot;
        }

        internal List<Record> OwnedBy(PresenceDetector owner)
        {
            // Copy the list so callers can change owners or remove roots while going through it.
            List<Record> result = new List<Record>();
            foreach (Record record in _records.Values)
            {
                if (record.Owner == owner)
                {
                    result.Add(record);
                }
            }

            return result;
        }
    }
}
