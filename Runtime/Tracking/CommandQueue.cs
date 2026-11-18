using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Orders commands and prevents nested drains on Unity's main thread.</summary>
    /// <typeparam name="T">The command data interpreted by the owning subsystem.</typeparam>
    internal sealed class CommandQueue<T>
    {
        private readonly struct Entry
        {
            internal readonly T Command;
            internal readonly long Sequence;

            internal Entry(T command, long sequence)
            {
                Command = command;
                Sequence = sequence;
            }
        }

        private readonly Queue<Entry> _pending = new Queue<Entry>();
        private readonly Action<T> _execute;
        private long _lastSequence;
        private bool _executing;

        internal long LastSequence
        {
            get
            {
                return _lastSequence;
            }
        }

        internal CommandQueue(Action<T> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        internal void Enqueue(T command)
        {
            _pending.Enqueue(new Entry(command, ++_lastSequence));
        }

        internal void ExecutePending(int maximum)
        {
            // Work added while this batch runs waits for the next batch.
            Execute(maximum, _lastSequence);
        }

        internal void ExecuteAll()
        {
            // Scene changes also run work added by the current Unity callbacks.
            Execute(int.MaxValue, long.MaxValue);
        }

        internal bool HasPendingThrough(long sequence)
        {
            return _pending.Count > 0 && _pending.Peek().Sequence <= sequence;
        }

        internal void Clear()
        {
            _pending.Clear();
        }

        private void Execute(int maximum, long sequence)
        {
            // A callback may try to run the queue again. Let the current loop finish it in order.
            if (_executing)
            {
                return;
            }

            _executing = true;
            try
            {
                for (int index = 0; index < maximum && HasPendingThrough(sequence); index++)
                {
                    // Remove the item first because its callback may add or clear queued work.
                    _execute(_pending.Dequeue().Command);
                }
            }
            finally
            {
                _executing = false;
            }
        }
    }
}
