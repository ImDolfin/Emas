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
        internal abstract void Refresh();
        internal abstract void ClearBinding();
    }
}
