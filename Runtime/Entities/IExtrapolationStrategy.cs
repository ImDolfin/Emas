using UnityEngine;

namespace Emas
{
    /// <summary>Calculates future pose offsets using pose, motion and timing observations.</summary>
    /// <remarks>Called on Unity's main thread, potentially more than once per projection for live tuning.
    /// Implementations must be deterministic and must not advance history or mutate inputs. The caller owns
    /// timing, horizon limits, timestamped history, coordinate conversion, interpolation and smoothing.
    /// Strategies choose their own history-based estimation; shared motion values remain available as fallbacks.
    /// Use a serializable class to persist an implementation on a Extrapolation component.</remarks>
    public interface IExtrapolationStrategy
    {
        /// <summary>Calculates linear displacement beyond an observed position.</summary>
        /// <param name="context">Pose and time-aligned linear/angular motion, including availability, provenance and coordinate conventions.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the observation, already limited by the extrapolation horizon.</param>
        /// <returns>A finite displacement in the position's source axes and units; zero at zero seconds.</returns>
        Double3 ExtrapolateDisplacement(ExtrapolationContext context, double seconds);

        /// <summary>Calculates an angular offset using the available pose and motion information.</summary>
        /// <param name="context">Pose and time-aligned linear/angular motion, including availability, provenance and coordinate conventions.</param>
        /// <param name="seconds">Finite, nonnegative seconds beyond the observation, already limited by the extrapolation horizon.</param>
        /// <returns>A finite, nonzero quaternion offset; identity at zero seconds. The caller converts axes and composes body or world rotation.</returns>
        Quaternion ExtrapolateRotation(ExtrapolationContext context, double seconds);
    }
}
