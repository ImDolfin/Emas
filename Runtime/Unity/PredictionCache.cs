namespace Emas
{
    // Prepared trajectories belong to an Extrapolation component, not a shared strategy.
    // Two keys per value type cover position/rotation times and live model transitions.
    internal sealed class PredictionCache
    {
        private readonly struct Key
        {
            private readonly long _context;
            private readonly long _history;
            private readonly object _model;
            private readonly int _options;
            private readonly float _parameter;

            internal Key(long context, long history, object model, int options, float parameter)
            {
                _context = context;
                _history = history;
                _model = model;
                _options = options;
                _parameter = parameter;
            }

            internal bool Matches(in Key other)
            {
                return _context == other._context && _history == other._history && ReferenceEquals(_model, other._model)
                    && _options == other._options && _parameter == other._parameter;
            }
        }

        private abstract class Entry
        {
            internal Entry Next;
        }

        private sealed class Entry<T> : Entry where T : struct
        {
            internal bool HasFirst;
            internal bool HasSecond;
            internal Key FirstKey;
            internal Key SecondKey;
            internal T First;
            internal T Second;
        }

        private Entry _entries;

        internal bool TryGet<T>(long context, long history, object model, int options, float parameter, out T value) where T : struct
        {
            Key key = new Key(context, history, model, options, parameter);
            for (Entry current = _entries; current != null; current = current.Next)
            {
                if (!(current is Entry<T> entry))
                {
                    continue;
                }

                if (entry.HasFirst && entry.FirstKey.Matches(key))
                {
                    value = entry.First;
                    return true;
                }

                if (entry.HasSecond && entry.SecondKey.Matches(key))
                {
                    value = entry.Second;
                    return true;
                }

                break;
            }

            value = default(T);
            return false;
        }

        internal void Store<T>(long context, long history, object model, int options, float parameter, T value) where T : struct
        {
            Entry<T> entry = null;
            for (Entry current = _entries; current != null; current = current.Next)
            {
                entry = current as Entry<T>;
                if (entry != null)
                {
                    break;
                }
            }

            // Allocate only on first use of a model's value type. New observations and
            // repeated settings changes reuse these two slots without boxing structs.
            if (entry == null)
            {
                entry = new Entry<T> { Next = _entries };
                _entries = entry;
            }

            entry.SecondKey = entry.FirstKey;
            entry.Second = entry.First;
            entry.HasSecond = entry.HasFirst;
            entry.FirstKey = new Key(context, history, model, options, parameter);
            entry.First = value;
            entry.HasFirst = true;
        }
    }
}
