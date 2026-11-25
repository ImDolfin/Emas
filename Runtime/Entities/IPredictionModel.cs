using UnityEngine;

namespace Emas
{
    // Mathematical kernels shared by the built-in full-pose strategies.
    // They never call back through the public strategy contract.
    internal interface IPredictionModel
    {
        Double3 Displacement(ExtrapolationContext context, double seconds);
        Quaternion Rotation(ExtrapolationContext context, double seconds);
        Double3 Velocity(ExtrapolationContext context, double seconds);
        Double3 AngularVelocity(ExtrapolationContext context, double seconds);
    }
}
