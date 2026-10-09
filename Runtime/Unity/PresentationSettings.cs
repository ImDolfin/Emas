namespace Emas
{
    // Capture effective values once per projection. Disabled channels use the same direct path as zero settings.
    internal readonly struct PresentationSettings
    {
        internal readonly float MaximumExtrapolation;
        internal readonly float PositionHalfLife;
        internal readonly float RotationHalfLife;
        internal readonly float InterpolationDelay;

        internal PresentationSettings(Prediction prediction, Smoothing smoothing, float interpolationDelay)
        {
            MaximumExtrapolation = prediction != null && prediction.enabled ? ValidTime(prediction.MaximumExtrapolation) : 0;
            PositionHalfLife = smoothing != null && smoothing.enabled ? ValidTime(smoothing.PositionHalfLife) : 0;
            RotationHalfLife = smoothing != null && smoothing.enabled ? ValidTime(smoothing.RotationHalfLife) : 0;
            InterpolationDelay = ValidTime(interpolationDelay);
        }

        internal bool SamePosition(PresentationSettings other)
        {
            return MaximumExtrapolation.Equals(other.MaximumExtrapolation) && PositionHalfLife.Equals(other.PositionHalfLife)
                && InterpolationDelay.Equals(other.InterpolationDelay);
        }

        internal bool SameRotation(PresentationSettings other)
        {
            return RotationHalfLife.Equals(other.RotationHalfLife) && MaximumExtrapolation.Equals(other.MaximumExtrapolation)
                && InterpolationDelay.Equals(other.InterpolationDelay);
        }

        private static float ValidTime(float value)
        {
            return SpatialMath.IsFinite(value) && value > 0 ? value : 0;
        }
    }
}
