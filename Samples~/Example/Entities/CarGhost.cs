using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Represents a discovered sample car.</summary>

    public sealed class CarGhost : Ghost, I3DPosition, IArticulate
    {
        private Vector3 _position;
        private float _steering;

        /// <inheritdoc />
        public Vector3 Position
        {
            get { return _position; }
        }

        /// <inheritdoc />
        public float Steering
        {
            get { return _steering; }
        }

        /// <inheritdoc />
        public void SetPosition(Vector3 position)
        {
            _position = position;
        }

        /// <inheritdoc />
        public void SetArticulation(float steering)
        {
            _steering = steering;
        }
    }
}
