namespace Emas
{
    // Read settings once per projection. Disabled filters act the same as filters set to zero.
    internal readonly struct PresentationSettings
    {
        internal readonly float MaximumExtrapolation;
        internal readonly float PositionHalfLife;
        internal readonly float RotationHalfLife;
        internal readonly float InterpolationDelay;
        internal readonly IExtrapolationStrategy Strategy;
        internal readonly float PacketSmoothingDuration;

        internal PresentationSettings(Extrapolation extrapolation, Smoothing smoothing, float interpolationDelay)
        {
            MaximumExtrapolation = extrapolation != null && extrapolation.enabled ? ValidTime(extrapolation.MaximumExtrapolation) : 0;
            PositionHalfLife = smoothing != null && smoothing.enabled ? ValidTime(smoothing.PositionHalfLife) : 0;
            RotationHalfLife = smoothing != null && smoothing.enabled ? ValidTime(smoothing.RotationHalfLife) : 0;
            InterpolationDelay = ValidTime(interpolationDelay);
            Strategy = extrapolation != null ? extrapolation.Strategy : null;
            // Apply packet correction only when prediction is enabled. Having an Extrapolation trait alone is not enough.
            IPacketSmoothingStrategy packetSmoothing = Strategy as IPacketSmoothingStrategy;
            PacketSmoothingDuration = extrapolation != null && extrapolation.enabled && MaximumExtrapolation > 0 && packetSmoothing != null
                ? ValidTime(packetSmoothing.PacketSmoothingDuration) : 0;
        }

        internal bool SamePosition(PresentationSettings other)
        {
            return MaximumExtrapolation.Equals(other.MaximumExtrapolation) && PositionHalfLife.Equals(other.PositionHalfLife)
                && InterpolationDelay.Equals(other.InterpolationDelay) && ReferenceEquals(Strategy, other.Strategy)
                && PacketSmoothingDuration.Equals(other.PacketSmoothingDuration);
        }

        internal bool SameRotation(PresentationSettings other)
        {
            return RotationHalfLife.Equals(other.RotationHalfLife) && MaximumExtrapolation.Equals(other.MaximumExtrapolation)
                && InterpolationDelay.Equals(other.InterpolationDelay) && ReferenceEquals(Strategy, other.Strategy)
                && PacketSmoothingDuration.Equals(other.PacketSmoothingDuration);
        }

        private static float ValidTime(float value)
        {
            return SpatialMath.IsFinite(value) && value > 0 ? value : 0;
        }
    }
}
