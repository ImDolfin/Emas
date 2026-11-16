namespace Emas
{
    // Capture effective values once per projection. Disabled channels use the same direct path as zero settings.
    internal readonly struct PresentationSettings
    {
        internal readonly float MaximumExtrapolation;
        internal readonly float PositionHalfLife;
        internal readonly float RotationHalfLife;
        internal readonly float InterpolationDelay;
        internal readonly IExtrapolationStrategy Strategy;

        internal PresentationSettings(Extrapolation extrapolation, Smoothing smoothing, float interpolationDelay)
        {
            MaximumExtrapolation = extrapolation != null && extrapolation.enabled ? ValidTime(extrapolation.MaximumExtrapolation) : 0;
            PositionHalfLife = smoothing != null && smoothing.enabled ? ValidTime(smoothing.PositionHalfLife) : 0;
            RotationHalfLife = smoothing != null && smoothing.enabled ? ValidTime(smoothing.RotationHalfLife) : 0;
            InterpolationDelay = ValidTime(interpolationDelay);
            Strategy = extrapolation != null ? extrapolation.Strategy : null;
        }

        internal bool SamePosition(PresentationSettings other)
        {
            return MaximumExtrapolation.Equals(other.MaximumExtrapolation) && PositionHalfLife.Equals(other.PositionHalfLife)
                && InterpolationDelay.Equals(other.InterpolationDelay) && ReferenceEquals(Strategy, other.Strategy);
        }

        internal bool SameRotation(PresentationSettings other)
        {
            return RotationHalfLife.Equals(other.RotationHalfLife) && MaximumExtrapolation.Equals(other.MaximumExtrapolation)
                && InterpolationDelay.Equals(other.InterpolationDelay) && ReferenceEquals(Strategy, other.Strategy);
        }

        private static float ValidTime(float value)
        {
            return SpatialMath.IsFinite(value) && value > 0 ? value : 0;
        }
    }
}
