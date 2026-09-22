using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Application-owned, read-only position contract shared by sources and consumers.</summary>
    public interface I3DPosition
    {
        /// <summary>Gets the position in the owning anchor's local coordinate frame.</summary>
        Vector3 Position { get; }
    }
}
