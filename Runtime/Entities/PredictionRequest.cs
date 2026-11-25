using UnityEngine;

namespace Emas
{
    /// <summary>Supplies one coherent prediction request for a Ghost's complete pose.</summary>
    /// <remarks>Position and rotation may have different creation times. Use each channel's own context
    /// and duration. Base values already include historical interpolation. History readers are valid only
    /// during Predict on the main thread. Retaining this value does not retain a history snapshot.</remarks>
    public readonly struct PredictionRequest
    {
        /// <summary>Gets observations aligned to the position's creation time.</summary>
        /// <value>Position, motion, bounded history and coordinate conventions for linear prediction.</value>
        public ExtrapolationContext PositionContext { get; }

        /// <summary>Gets observations aligned to the orientation's creation time.</summary>
        /// <value>Pose, motion, bounded history and coordinate conventions for angular prediction.</value>
        public ExtrapolationContext RotationContext { get; }

        /// <summary>Gets the sampled absolute position to predict from.</summary>
        /// <value>Cartesian source units or ECEF metres; retain it when PositionContext.Position is null.</value>
        public Double3 Position { get; }

        /// <summary>Gets the sampled absolute orientation to predict from.</summary>
        /// <value>A normalized quaternion in RotationContext.RotationSpace.</value>
        public Quaternion Rotation { get; }

        /// <summary>Gets the forward interval for position.</summary>
        /// <value>Finite, nonnegative seconds, already capped by the configured prediction limit.</value>
        public double PositionSeconds { get; }

        /// <summary>Gets the forward interval for orientation.</summary>
        /// <value>Finite, nonnegative seconds, already capped independently of position.</value>
        public double RotationSeconds { get; }

        internal double PreviousPositionSeconds { get; }
        internal double RotationMotionSeconds { get; }

        /// <summary>Creates a request from independently timed channels and an interpolated base pose.</summary>
        /// <param name="positionContext">Observations aligned to position creation.</param>
        /// <param name="rotationContext">Observations aligned to orientation creation.</param>
        /// <param name="position">Finite base position in source coordinates.</param>
        /// <param name="rotation">Finite, nonzero base orientation in RotationSpace.</param>
        /// <param name="positionSeconds">Finite, nonnegative position prediction duration in seconds.</param>
        /// <param name="rotationSeconds">Finite, nonnegative angular prediction duration in seconds.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Pose or duration is invalid.</exception>
        public PredictionRequest(ExtrapolationContext positionContext, ExtrapolationContext rotationContext,
            Double3 position, Quaternion rotation, double positionSeconds, double rotationSeconds)
            : this(positionContext, rotationContext, position, rotation, positionSeconds, rotationSeconds, positionSeconds, 0)
        {
        }

        internal PredictionRequest(ExtrapolationContext positionContext, ExtrapolationContext rotationContext,
            Double3 position, Quaternion rotation, double positionSeconds, double rotationSeconds,
            double previousPositionSeconds, double rotationMotionSeconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(positionSeconds);
            ConstantVelocityExtrapolation.ValidateSeconds(rotationSeconds);
            if (!SpatialMath.IsFinite(position))
            {
                throw new System.ArgumentOutOfRangeException(nameof(position));
            }

            PositionContext = positionContext;
            RotationContext = rotationContext;
            Position = position;
            Rotation = SpatialMath.NormalizeRotation(rotation, nameof(rotation));
            PositionSeconds = positionSeconds;
            RotationSeconds = rotationSeconds;
            PreviousPositionSeconds = previousPositionSeconds;
            RotationMotionSeconds = rotationMotionSeconds;
        }

        /// <summary>Composes model offsets into a full prediction using the request's coordinate conventions.</summary>
        /// <param name="displacement">Position offset in source units or ECEF metres.</param>
        /// <param name="rotationOffset">Angular offset in model right/up/forward axes, or world axes when AngularVelocityIsWorld is true.</param>
        /// <param name="velocity">Predicted source-space position derivative per second.</param>
        /// <param name="angularVelocity">Predicted angular derivative in the same basis as rotationOffset, in radians per second.</param>
        /// <returns>An absolute predicted pose, with angular velocity converted to its quaternion's world axes.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">An offset, motion or resulting pose is invalid.</exception>
        public Prediction CreatePrediction(Double3 displacement, Quaternion rotationOffset,
            Double3 velocity = default(Double3), Double3 angularVelocity = default(Double3))
        {
            Quaternion rotation = ComposeRotation(rotationOffset);
            return new Prediction(Position + displacement, rotation, velocity, ConvertAngularVelocity(angularVelocity, rotation));
        }

        internal Double3 ConvertAngularVelocity(Double3 angularVelocity, Quaternion rotation)
        {
            // Mirrored axis mappings reverse the sign of angular vectors as well as rotation offsets.
            Double3 rate = angularVelocity;
            ExtrapolationContext context = RotationContext;
            if (context.RotationSpace == RotationSpace.Source)
            {
                rate = context.SourceCoordinates.ToSourceAngularVelocity(rate);
            }
            else if (!context.AngularVelocityIsWorld && context.RotationSpace == RotationSpace.EarthCentered)
            {
                rate = context.EarthCenteredBodyAxes.Value.ToSourceAngularVelocity(rate);
            }

            if (!context.AngularVelocityIsWorld)
            {
                rate = SpatialMath.Rotate(rotation, rate);
            }

            return rate;
        }

        internal Quaternion ConvertRotation(Quaternion offset)
        {
            offset = SpatialMath.NormalizeRotation(offset, nameof(offset));
            if (RotationContext.RotationSpace == RotationSpace.Source)
            {
                return RotationContext.SourceCoordinates.ToSourceRotation(offset);
            }

            return !RotationContext.AngularVelocityIsWorld && RotationContext.RotationSpace == RotationSpace.EarthCentered
                ? RotationContext.EarthCenteredBodyAxes.Value.ToSourceRotation(offset) : offset;
        }

        internal Quaternion ComposeRotation(Quaternion offset)
        {
            offset = ConvertRotation(offset);
            return SpatialMath.NormalizeRotation(RotationContext.AngularVelocityIsWorld ? offset * Rotation : Rotation * offset, nameof(offset));
        }
    }
}
