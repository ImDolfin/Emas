using System;

namespace Emas
{
    /// <summary>
    /// Reads a source-independent value and applies it to a Ghost on each realm update.
    /// </summary>
    /// <typeparam name="TData">The input value needed by this module.</typeparam>
    public abstract class EntityModule<TData> : EntityModule
    {
        private Func<TData> _read;

        /// <summary>
        /// Connects this module to a value reader without depending on the SDK's proxy type.
        /// </summary>
        /// <param name="read">Reads the latest value on Unity's main thread.</param>
        /// <remarks>
        /// Bind in the registered Ghost initializer. Rebinding replaces the previous reader.
        /// The realm reads enabled modules before projection and availability notifications.
        /// Disappearance, source handover and removal release readers; rediscovery reruns initialization.
        /// </remarks>
        public void Bind(Func<TData> read)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
        }

        /// <summary>
        /// Applies a value without knowledge of where it came from.
        /// </summary>
        /// <param name="data">The source-independent module input.</param>
        public abstract void Apply(TData data);

        internal override void Refresh()
        {
            Func<TData> read = _read;
            if (read == null)
            {
                return;
            }

            TData data = read();
            // A reader may synchronously remove, disable or rebind its own module.
            if (this != null && enabled && ReferenceEquals(_read, read))
            {
                Apply(data);
            }
        }

        internal override void ClearBinding()
        {
            _read = null;
        }
    }
}
