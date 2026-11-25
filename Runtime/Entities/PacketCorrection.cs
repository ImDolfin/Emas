using UnityEngine;

namespace Emas
{
    /// <summary>Filters packet discontinuities in source observations before extrapolation.</summary>
    /// <remarks>Place before Smoothing to smooth packet correction error as well. Runs independently of the
    /// selected extrapolation strategy. Coherent measured motion is retained while correction error fades.</remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Packet Correction")]
    public sealed class PacketCorrection : Filter
    {
        [Tooltip("Seconds to remove packet correction error before prediction. Zero applies observations immediately.")]
        [Min(0)]
        [SerializeField] private float _duration = 0.1f;
        private PacketSmoother _smoother;

        /// <summary>Gets or sets the packet correction interval.</summary>
        /// <value>Finite, nonnegative unscaled seconds; zero bypasses correction. Default: 0.1 seconds.</value>
        /// <exception cref="System.ArgumentOutOfRangeException">The value is negative or nonfinite.</exception>
        public float Duration
        {
            get => _duration;
            set
            {
                Smoothing.ValidateTime(value, nameof(value));
                _duration = value;
            }
        }

        /// <summary>Gets the current correction configuration for live transitions.</summary>
        /// <value>The correction duration's hash.</value>
        protected override int ConfigurationVersion => _duration.GetHashCode();

        /// <summary>Corrects observation jumps before the trajectory is extrapolated.</summary>
        /// <param name="input">Raw source observations or the previous filter's output.</param>
        /// <param name="context">Processing time and source coordinate conventions.</param>
        /// <returns>The corrected observation pose with retained motion and sample times.</returns>
        protected override FilterState Process(FilterState input, FilterContext context)
        {
            return _smoother.Apply(input, context, _duration);
        }

        /// <summary>Discards both correction curves.</summary>
        protected override void OnReset()
        {
            _smoother = default(PacketSmoother);
        }
    }
}
