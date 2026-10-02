using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Base component for a Ghost module updated by its realm before spatial projection and queries.
    /// </summary>
    /// <remarks>
    /// Configure modules on the Ghost root prefab or declare them with RequireComponent on its Ghost class.
    /// Initializers connect module inputs to application data. Disabled modules do not update.
    /// Enabled modules can read before the root's first activation, so binding must not depend on OnEnable.
    /// Every module read finishes before Ghost.OnUpdate hooks. Ordering among individual modules is unspecified.
    /// </remarks>
    public abstract class EntityModule : MonoBehaviour
    {
        private Ghost _ghost;

        /// <summary>Gets the Ghost on this module's GameObject, or null when none is present.</summary>
        /// <remarks>
        /// Resolves lazily, including while the root is inactive or this module is disabled, without depending on Awake or OnEnable.
        /// Does not search parents or children. A missing or destroyed Ghost is resolved again on the next access.
        /// Emas initializes the Ghost's identity before application initialization and bound module reads.
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

        internal abstract void Refresh();
        internal abstract void ClearBinding();
    }
}
