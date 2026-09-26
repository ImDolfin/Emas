using System;

namespace Emas
{
    /// <summary>
    /// Converts detected SDK data into state on an initialized presence.
    /// </summary>
    /// <remarks>
    /// Add modules in a realm's per-kind presence initializer. Emas invokes them on Unity's main thread
    /// after the presence has a root and before it becomes available to queries.
    /// </remarks>
    public abstract class EntityModule
    {
        private Presence _presence;

        /// <summary>
        /// Gets the presence that owns this module.
        /// </summary>
        public Presence Presence
        {
            get
            {
                return _presence;
            }
        }

        internal void Bind(Presence presence)
        {
            if (_presence != null)
            {
                throw new InvalidOperationException("An entity module is already attached to a presence.");
            }

            _presence = presence;
        }

        internal abstract bool TryApply(object data);
    }
}