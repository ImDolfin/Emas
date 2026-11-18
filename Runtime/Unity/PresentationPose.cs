using UnityEngine;

namespace Emas
{
    // A pose in source axes, kept separate from raw observations and Unity transforms.
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
