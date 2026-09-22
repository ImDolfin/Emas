using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>
    /// A minimal source record, standing in for an SDK entity.
    /// </summary>
    public sealed class Reading
    {
        /// <summary>
        /// Creates one source reading.
        /// </summary>
        /// <param name="id">
        /// The stable source identifier.
        /// </param>
        /// <param name="position">
        /// The current local position.
        /// </param>
        public Reading(string id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        /// <summary>
        /// Gets the source identifier.
        /// </summary>
        public string Id
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the reported local position.
        /// </summary>
        public Vector3 Position
        {
            get;
            private set;
        }
    }
}
