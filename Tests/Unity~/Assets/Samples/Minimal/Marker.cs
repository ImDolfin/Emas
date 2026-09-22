using UnityEngine;

namespace Emas.Minimal
{
    /// <summary>
    /// A source-independent marker whose root follows the reported position.
    /// </summary>
    public sealed class Marker : Ghost
    {
        /// <summary>
        /// The population identifier used by this sample's blueprint.
        /// </summary>
        public static readonly Kind Kind = new Kind("minimal.marker");

        /// <summary>
        /// Applies a position in the anchor's coordinate frame.
        /// </summary>
        /// <param name="position">
        /// The source-independent local position.
        /// </param>
        public void SetPosition(Vector3 position)
        {
            transform.localPosition = position;
        }
    }
}
