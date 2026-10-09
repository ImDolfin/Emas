using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Application-owned, read-only position contract shared by sources and consumers.
    /// </summary>
    public interface I3DPosition
    {
        /// <summary>
        /// Gets the position in the owning anchor's local coordinate frame.
        /// </summary>
        /// <value>A position in local Unity units, using X right, Y up and Z forward.</value>
        Vector3 Position
        {
            get;
        }
    }
}
