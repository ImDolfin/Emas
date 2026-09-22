using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Represents a discovered sample car.
    /// </summary>

    public sealed class CarGhost : Ghost, I3DPosition, IArticulate
    {
        private Vector3 _position;
        private float _steering;

        /// <inheritdoc />
        public Vector3 Position
        {
            get
            {
                return _position;
            }
        }

        /// <inheritdoc />
        public float Steering
        {
            get
            {
                return _steering;
            }
        }

        /// <summary>
        /// Copies source position into this ghost.
        /// </summary>
        /// <param name="position">
        /// Position converted to the owning anchor's local frame.
        /// </param>
        public void SetPosition(Vector3 position)
        {
            _position = position;
        }

        /// <summary>
        /// Copies source steering into this ghost.
        /// </summary>
        /// <param name="steering">
        /// Normalized steering, from -1 to 1.
        /// </param>
        public void SetArticulation(float steering)
        {
            _steering = steering;
        }
    }
}
