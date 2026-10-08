using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Base component for a Ghost trait updated by its realm before spatial projection and queries.
    /// </summary>
    /// <remarks>
    /// Configure traits on the Ghost root prefab or declare them with RequireComponent on its Ghost class.
    /// Initializers connect input traits to application data. Optional Smoothing and Prediction behavior traits
    /// require no binding and evaluate after all inputs in the spatial phase. Disabled traits do not update.
    /// Enabled traits can read before the root's first activation, so binding must not depend on OnEnable.
    /// Every trait read finishes before Ghost.OnUpdate hooks. Ordering among individual traits is unspecified.
    /// </remarks>
    public abstract class Trait : MonoBehaviour
    {
        private Ghost _ghost;

        /// <summary>Gets the Ghost on this trait's GameObject, or null when none is present.</summary>
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

        internal abstract void Refresh();
        internal abstract void ClearBinding();
    }
}
