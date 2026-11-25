namespace Emas
{
    internal readonly struct PresentationSettings
    {
        internal readonly float MaximumExtrapolation;
        internal readonly float InterpolationDelay;
        internal readonly IExtrapolationStrategy Strategy;
        internal readonly float PredictionTime;
        internal readonly int Filters;

        internal PresentationSettings(Extrapolation extrapolation, int filters, float interpolationDelay)
        {
            MaximumExtrapolation = extrapolation != null && extrapolation.enabled ? extrapolation.MaximumExtrapolation : 0;
            InterpolationDelay = interpolationDelay;
            Strategy = extrapolation != null ? extrapolation.Strategy : null;
            PredictionTime = MaximumExtrapolation > 0 ? extrapolation.PredictionTime : 0;
            Filters = filters;
        }

        internal bool SamePosition(PresentationSettings other)
        {
            return MaximumExtrapolation.Equals(other.MaximumExtrapolation) && InterpolationDelay.Equals(other.InterpolationDelay)
                && ReferenceEquals(Strategy, other.Strategy) && PredictionTime.Equals(other.PredictionTime) && Filters == other.Filters;
        }

        internal bool SameRotation(PresentationSettings other)
        {
            return SamePosition(other);
        }
    }
}
