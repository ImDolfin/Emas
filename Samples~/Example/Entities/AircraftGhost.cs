using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Represents a discovered sample aircraft.</summary>

    public sealed class AircraftGhost : Ghost, I3DPosition
    {
        private Vector3 _position;

        /// <inheritdoc />
        public Vector3 Position
        {
            get { return _position; }
        }

        /// <inheritdoc />
        public void SetPosition(Vector3 position)
        {
            _position = position;
        }
    }
}
