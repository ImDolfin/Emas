using UnityEngine;

namespace Emas
{
    /// <summary>Bookkeeping component attached to an instantiated view.</summary>
    /// <remarks>Emas binds the ghost before activating a view. Read application contracts through Ghost.TryGet in OnEnable or later.
    /// Prefab reuse may rebind an existing view without another OnEnable; avoid retaining source-specific data or assuming one binding forever.</remarks>
    [DisallowMultipleComponent]

    public sealed class View : MonoBehaviour
    {
        private IGhost _ghost;
        private DetailLevel _requestedDetailLevel;

        /// <summary>Gets the ghost represented by this view.</summary>
        /// <value>The bound ghost.</value>
        public IGhost Ghost
        {
            get { return _ghost; }
        }

        /// <summary>Gets the current requested detail level.</summary>
        /// <value>The requested level; the selected prefab may support a lower level.</value>
        public DetailLevel RequestedDetailLevel
        {
            get { return _requestedDetailLevel; }
        }

        /// <summary>Binds this view to a ghost.</summary>
        /// <param name="ghost">The represented ghost.</param>
        /// <param name="detailLevel">The requested detail level.</param>
        internal void Bind(IGhost ghost, DetailLevel detailLevel)
        {
            _ghost = ghost;
            _requestedDetailLevel = detailLevel;
        }
    }
}
