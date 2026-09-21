using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Provides source-independent three-dimensional position data.</summary>

    public interface I3DPosition
    {
        /// <summary>Gets the current world-relative position.</summary>
        /// <value>The source-independent position.</value>
        Vector3 Position { get; }

        /// <summary>Sets the current world-relative position.</summary>
        /// <param name="position">The new position.</param>
        void SetPosition(Vector3 position);
    }
}
