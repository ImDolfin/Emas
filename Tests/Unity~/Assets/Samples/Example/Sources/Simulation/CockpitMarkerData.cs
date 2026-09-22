using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Represents screen-coordinate data from the simulated cockpit source.</summary>

    public sealed class CockpitMarkerData
    {
        /// <summary>Creates screen-coordinate data.</summary>
        /// <param name="screenId">The source screen identifier.</param>
        /// <param name="normalizedTopLeft">The normalized top-left screen coordinate.</param>
        public CockpitMarkerData(string screenId, Vector2 normalizedTopLeft)
        {
            ScreenId = screenId;
            NormalizedTopLeft = normalizedTopLeft;
        }

        /// <summary>Gets the source screen identifier.</summary>
        /// <value>The source screen identifier.</value>
        public string ScreenId { get; private set; }

        /// <summary>Gets the normalized top-left coordinate.</summary>
        /// <value>The source coordinate with both components in the range zero to one.</value>
        public Vector2 NormalizedTopLeft { get; private set; }
    }
}
