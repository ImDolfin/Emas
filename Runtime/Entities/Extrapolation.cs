using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that extrapolates pose using timestamped observations and optional SDK linear and angular motion.</summary>
    /// <remarks>
    /// Place beside Spatial; no reader binding is required. Supply all pose and motion observations to Spatial.
    /// They share one SDK clock and buffered playback time. Missing rates can be estimated from timed poses.
    /// Disabling this trait stops gap prediction; Spatial inputs remain available to Smoothing.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Spatial))]
    [AddComponentMenu("Emas/Extrapolation")]
    public sealed class Extrapolation : Trait
    {
        // Round one 144 Hz frame upward to a practical millisecond limit.
        private const float MinimumPredictionLimit = 0.007f;

        [Tooltip("Gap extrapolation model. Change at runtime to compare methods without losing observations or supplied motion.")]
        [SerializeReference] private IExtrapolationStrategy _strategy = new ConstantAccelerationExtrapolation();
        [Tooltip("Zero uses automatic clock timing. A positive value replaces automatic age compensation with this prediction interval plus time since receipt. On the followed Ghost it controls the whole Realm; other Ghosts inherit that target. Without a timed reference the override is individual. Prediction limit still caps each entity's prediction.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _predictionTime;
        [Tooltip("Maximum total prediction beyond a recorded pose, including automatic age-based prediction and manual lead. Minimum: 0.007 seconds (7 ms, rounded up from one 144 Hz frame). This cap does not force a prediction interval. Disable this trait to stop extrapolation.")]
        [Range(MinimumPredictionLimit, 0.5f)]
        [SerializeField] private float _maximumExtrapolation = 0.15f;
        /// <summary>Gets or sets the linear and angular gap extrapolation model on Unity's main thread.</summary>
        /// <value>A non-null strategy; defaults to <see cref="ConstantAccelerationExtrapolation"/>.</value>
        /// <remarks>Replacing the instance blends the presentation difference over 0.25 seconds without
        /// clearing observations, supplied motion or smoothing. Strategies may be shared when immutable;
        /// packet correction duration edits also blend. Other in-place configuration changes
        /// require assigning a new instance to start that transition.
        /// Serializable implementations persist with the component; ordinary implementations can be assigned at runtime.</remarks>
        /// <exception cref="ArgumentNullException">The assigned strategy is null.</exception>
        public IExtrapolationStrategy Strategy
        {
            get
            {
                // Use the default if Unity cannot load the saved strategy or the saved value is null.
                if (_strategy == null)
                {
                    _strategy = new ConstantAccelerationExtrapolation();
                }

                return _strategy;
            }
            set => _strategy = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets or sets the manual prediction interval, with zero selecting automatic clock timing.</summary>
        /// <value>Finite, nonnegative seconds; zero by default uses automatic age-based prediction.</value>
        /// <remarks>A positive value replaces automatic age compensation with this interval plus time since receipt.
        /// MaximumExtrapolation caps the total duration. A sample at SDK time 0.2 with PredictionTime 0.2
        /// targets SDK time 0.4 at receipt, even when known delivery age exceeds 0.2 seconds.
        /// Buffered playback consumes forward time first. Applies to every strategy;
        /// NoExtrapolation still holds recorded poses. Disabling this trait disables forward prediction.
        /// With a timed followed Ghost, its enabled Extrapolation trait selects manual timing for the whole Realm.
        /// Peers inherit that target and retain their own strategy and cap; their individual intervals are ignored.
        /// Returning the followed interval to zero, disabling it or removing it restores automatic reference timing,
        /// including ReferenceFrame.PredictionTime. Without a timed followed Ghost, overrides are individual.
        /// Individual live edits blend over 0.25 unscaled seconds. Shared overrides retarget the clock on the next
        /// projection pass. Raw input and SDK timestamps remain unchanged.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative, NaN or infinite.</exception>
        public float PredictionTime
        {
            get => _predictionTime;
            set
            {
                Smoothing.ValidateTime(value, nameof(value));
                _predictionTime = value;
            }
        }

        /// <summary>Gets or sets the maximum prediction duration beyond a recorded pose, including PredictionTime; defaults to 0.15 seconds.</summary>
        /// <value>A finite horizon of at least 0.007 seconds (7 ms, rounded up from one 144 Hz frame).
        /// Nonnegative assignments below this minimum, including zero, clamp to 0.007 seconds.</value>
        /// <remarks>Disable this trait to stop extrapolation while retaining SDK motion assistance for smoothing.
        /// Saved values below the minimum also use 0.007 seconds. The minimum is a cap floor, not a forced forward interval.
        /// Live edits blend the setting-induced displacement over 0.25 seconds of real time, including enabled
        /// changes, with or without Smoothing. Filter history, raw input, timestamps and motion remain intact.
        /// During that transition presentation can retain some of the previous limit's lead. Reaching the limit
        /// stops motion from held observations, while new timed observations retain supplied SDK motion assistance.
        /// This is a cap, not a requested forward duration; use PredictionTime to predict farther ahead.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The assigned horizon is negative, NaN or infinite.</exception>
        public float MaximumExtrapolation
        {
            get => ClampPredictionLimit(_maximumExtrapolation);
            set
            {
                Smoothing.ValidateTime(value, nameof(value));
                _maximumExtrapolation = ClampPredictionLimit(value);
            }
        }

        internal override bool NeedsRefresh => false;

        /// <summary>Restarts motion-estimation windows and packet correction after a teleport while retaining supplied SDK motion and settings.</summary>
        /// <remarks>Use <see cref="Spatial.ResetPresentation"/> when smoothing history must be reset at the same time.</remarks>
        public void Reset()
        {
            Spatial spatial = GetComponent<Spatial>();
            if (spatial != null)
            {
                spatial.ResetPrediction();
            }
        }

        internal override void Refresh()
        {
        }

        internal override void ClearBinding()
        {
            Reset();
        }

        private void OnValidate()
        {
            // Serialized edits bypass the API setter. Normalize saved values here, and
            // keep the getter safe for existing prefabs loaded directly in a player.
            _maximumExtrapolation = ClampPredictionLimit(_maximumExtrapolation);
        }

        private static float ClampPredictionLimit(float value)
        {
            return SpatialMath.IsFinite(value) && value >= MinimumPredictionLimit ? value : MinimumPredictionLimit;
        }
    }
}
