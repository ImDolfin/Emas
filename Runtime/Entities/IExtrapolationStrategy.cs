namespace Emas
{
    /// <summary>Predicts a complete source pose and its motion for one Ghost.</summary>
    /// <remarks>Emas calls Predict once per eligible Ghost per Realm projection, on Unity's main thread.
    /// Position and rotation keep independent observation times within the same request. Implementations
    /// must be deterministic and must not mutate inputs. Emas owns interpolation, time limits and smoothing.
    /// Use a serializable class to persist a strategy on an Extrapolation component.</remarks>
    public interface IExtrapolationStrategy
    {
        /// <summary>Predicts both pose channels in one evaluation, after observation filtering.</summary>
        /// <param name="request">Sampled base pose, independent bounded prediction intervals and time-aligned observations.</param>
        /// <returns>The complete predicted source pose and its linear and angular velocity.</returns>
        Prediction Predict(PredictionRequest request);
    }
}
