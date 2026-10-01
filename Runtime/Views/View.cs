using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Bookkeeping component attached to an instantiated view.
    /// </summary>
    /// <remarks>
    /// Emas binds the ghost before activating a view. Read application contracts through Ghost.TryGet in OnEnable or later.
    /// Prefab reuse may rebind an existing view without another OnEnable; avoid retaining source-specific data or assuming one binding forever.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class View : MonoBehaviour
    {
        private IGhost _ghost;

        /// <summary>
        /// Gets the ghost represented by this view.
        /// </summary>
        public IGhost Ghost
        {
            get
            {
                return _ghost;
            }
        }

        internal void Bind(IGhost ghost)
        {
            _ghost = ghost;
        }
    }
}
