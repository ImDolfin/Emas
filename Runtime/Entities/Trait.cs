using UnityEngine;

namespace Emas
{
    /// <summary>Base component for a Ghost trait updated by its realm before spatial projection and queries.</summary>
    /// <remarks>
    /// Author traits on the Ghost root and bind inputs in its initializer. Enabled readers finish before
    /// Ghost.OnUpdate; their relative order is unspecified. Reads can precede initial activation, so binding must
    /// not depend on OnEnable. Filters and Extrapolation need no reader binding.
    /// </remarks>
    public abstract class Trait : MonoBehaviour
    {
        private Ghost _ghost;

        /// <summary>Gets the Ghost on this trait's GameObject, or null when none is present.</summary>
        /// <value>The live Ghost on the same root, including when the root or trait is inactive.</value>
        /// <remarks>
        /// Resolves lazily, including while the root is inactive or this trait is disabled, without depending on Awake or OnEnable.
        /// Does not search parents or children. A missing or destroyed Ghost is resolved again on the next access.
        /// Emas initializes the Ghost's identity before application initialization and bound trait reads.
        /// </remarks>
        public Ghost Ghost
        {
            get
            {
                if (_ghost == null)
                {
                    _ghost = GetComponent<Ghost>();
                }

                return _ghost;
            }
        }

        internal virtual bool NeedsRefresh => true;

        internal abstract void Refresh();
        internal abstract void ClearBinding();
    }
}
