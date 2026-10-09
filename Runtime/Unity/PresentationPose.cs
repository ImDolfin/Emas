using UnityEngine;

namespace Emas
{
    // A source-space pose, kept separate from both immutable observations and projected Unity transforms.
    internal struct PresentationPose
    {
        internal Double3 Position;
        internal Quaternion Rotation;

        internal PresentationPose(Double3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }
}
