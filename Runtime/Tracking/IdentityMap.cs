using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Keeps one current record per identity within a realm.
    /// </summary>
    internal sealed class IdentityMap
    {
        private readonly Dictionary<Key, Record> _records = new Dictionary<Key, Record>();

        internal IEnumerable<Record> Values
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

            // The record supplies its own identity; an existing key cannot be overwritten.
            _records.Add(record.Key, record);
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

            // A retained handle cannot address a replacement with the same key.
            return ReferenceEquals(record.Ghost, ghost) ? record : null;
        }

        internal bool Remove(Record record)
        {
            if (!Contains(record))
            {
                return false;
            }

            // Cleanup from an old lifetime must never remove a newer record.
            return _records.Remove(record.Key);
        }

        internal bool Contains(Record record)
        {
            if (record == null)
            {
                return false;
            }

            Record current;
            return _records.TryGetValue(record.Key, out current) && ReferenceEquals(current, record);
        }

        internal List<Record> Snapshot()
        {
            return new List<Record>(_records.Values);
        }

        internal List<Record> OwnedBy(PresenceDetector owner)
        {
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
