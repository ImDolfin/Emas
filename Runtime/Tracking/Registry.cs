using System.Collections.Generic;

namespace Emas
{
    internal sealed class Registry
    {
        private readonly Dictionary<Key, Record> _records = new Dictionary<Key, Record>();

        internal IEnumerable<Record> Values
        {
            get
            {
                return _records.Values;
            }
        }

        internal void Add(Key key, Record record)
        {
            _records.Add(key, record);
        }

        internal bool TryGetValue(Key key, out Record record)
        {
            return _records.TryGetValue(key, out record);
        }

        internal void Remove(Key key)
        {
            _records.Remove(key);
        }

        internal List<Record> Snapshot()
        {
            return new List<Record>(_records.Values);
        }

        internal bool Contains(Record record)
        {
            Record current;
            return _records.TryGetValue(record.Key, out current) && ReferenceEquals(current, record);
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
