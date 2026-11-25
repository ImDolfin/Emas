using System;
using UnityEngine;

namespace Emas
{
    internal static class PredictionEvaluator
    {
        internal static Prediction Evaluate(PredictionRequest request, IPredictionModel model)
        {
            Double3 displacement = default(Double3);
            Double3 position = request.Position;
            Double3 velocity = default(Double3);
            Quaternion rotation = request.Rotation;
            Double3 angularVelocity = default(Double3);
            // Each channel can fail independently without discarding a valid other channel.
            try
            {
                if (request.PositionContext.Position.HasValue)
                {
                    displacement = model.Displacement(request.PositionContext, request.PositionSeconds);
                    position += displacement;
                    velocity = model.Velocity(request.PositionContext, request.PositionSeconds);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                velocity = default(Double3);
            }

            try
            {
                if (request.RotationContext.Rotation.HasValue)
                {
                    rotation = request.ComposeRotation(model.Rotation(request.RotationContext, request.RotationSeconds));
                    angularVelocity = request.ConvertAngularVelocity(model.AngularVelocity(request.RotationContext, request.RotationSeconds), rotation);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                angularVelocity = default(Double3);
            }

            return new Prediction(position, rotation, velocity, angularVelocity);
        }
    }
}
