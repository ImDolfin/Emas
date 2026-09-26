using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Orders commands and prevents nested drains on Unity's main thread.
    /// </summary>
    /// <typeparam name="T">The command data interpreted by the owning subsystem.</typeparam>
    internal sealed class CommandQueue<T>
    {
        private readonly Queue<Entry> _pending = new Queue<Entry>();
        private readonly Action<T> _execute;
        private long _lastSequence;
        private bool _executing;

        internal CommandQueue(Action<T> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        internal long LastSequence
        {
            get
            {
                return _lastSequence;
            }
        }

        internal void Enqueue(T command)
        {
            _pending.Enqueue(new Entry(command, ++_lastSequence));
        }

        internal void ExecutePending(int maximum)
        {
            // Commands enqueued during execution belong to a later batch.
            Execute(maximum, _lastSequence);
        }

        internal void ExecuteAll()
        {
            // Scene changes include work enqueued by the current Unity callbacks.
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
            if (_executing)
            {
                return;
            }

            _executing = true;
            try
            {
                for (int index = 0; index < maximum && HasPendingThrough(sequence); index++)
                {
                    // Dequeue before invoking application code, which may enqueue or clear work.
                    _execute(_pending.Dequeue().Command);
                }
            }
            finally
            {
                _executing = false;
            }
        }

        private readonly struct Entry
        {
            internal Entry(T command, long sequence)
            {
                Command = command;
                Sequence = sequence;
            }

            internal readonly T Command;
            internal readonly long Sequence;
        }
    }
}
