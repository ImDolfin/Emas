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

        /// <summary>Copies source position into this ghost.</summary>
        /// <param name="position">Position converted to the owning anchor's local frame.</param>
        public void SetPosition(Vector3 position)
        {
            _position = position;
        }
    }
}
