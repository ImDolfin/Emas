using UnityEngine;

namespace Emas
{
    /// <summary>Bookkeeping component attached to an instantiated view.</summary>
    [DisallowMultipleComponent]

    public sealed class View : MonoBehaviour
    {
        private IGhost _ghost;
        private DetailLevel _degree;

        /// <summary>Gets the ghost represented by this view.</summary>
        /// <value>The bound ghost.</value>
        public IGhost Ghost
        {
            get { return _ghost; }
        }

        /// <summary>Gets the current requested degree.</summary>
        /// <value>The bound detail level.</value>
        public DetailLevel Degree
        {
            get { return _degree; }
        }

        /// <summary>Binds this view to a ghost.</summary>
        /// <param name="ghost">The represented ghost.</param>
        /// <param name="degree">The requested degree.</param>
        internal void Bind(IGhost ghost, DetailLevel degree)
        {
            _ghost = ghost;
            _degree = degree;
        }
    }
}
