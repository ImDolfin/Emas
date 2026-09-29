using System;
using UnityEngine;

namespace Emas.Callbacks
{
    /// <summary>
    /// A small event-driven SDK substitute that reports a marker for four seconds, then removes it for two.
    /// </summary>
    public sealed class SimulatedFeed
    {
        private float _elapsed;

        /// <summary>
        /// Announces an entity when it appears; later data changes are read through Current.
        /// </summary>
        public event Action<Reading> Arrived;

        /// <summary>
        /// Reports the stable ID of an entity that departed.
        /// </summary>
        public event Action<string> Removed;

        /// <summary>
        /// Gets the current entity for initial publication, or null while absent.
        /// </summary>
        public Reading Current
        {
            get;
            private set;
        } = new Reading("one", Vector3.zero);

        /// <summary>
        /// Advances the sample feed and emits individual changes.
        /// </summary>
        /// <param name="deltaTime">
        /// Seconds since the previous update.
        /// </param>
        public void Advance(float deltaTime)
        {
            _elapsed += deltaTime;
            if (_elapsed % 6f < 4f)
            {
                bool arrived = Current == null;
                Current = new Reading("one", new Vector3(Mathf.Sin(_elapsed) * 2f, 0f, 0f));
                if (arrived && Arrived != null)
                {
                    Arrived(Current);
                }
            }
            else if (Current != null)
            {
                Current = null;
                if (Removed != null)
                {
                    Removed("one");
                }
            }
        }
    }
}
