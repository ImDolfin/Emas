using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that smooths source pose presentation before reference projection.</summary>
    /// <remarks>
    /// Place beside Spatial; no reader binding is required. Position and rotation filter independently, using
    /// buffered poses when configured. Timed SDK velocity supplied through Extrapolation compensates linear motion
    /// between observations even when gap extrapolation is disabled.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Spatial))]
    [AddComponentMenu("Emas/Smoothing")]
    public sealed class Smoothing : Trait
    {
        [Tooltip("Seconds to halve position correction error. Zero applies immediately; start at 0.08 s. Larger values remove more jitter but increase lag without SDK motion.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _positionHalfLife = 0.08f;
        [Tooltip("Seconds to halve angular correction error. Zero applies immediately (default). Larger values smooth more but delay turns, including scene movement when following this Ghost's orientation.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _rotationHalfLife;
        private PoseSmoother _smoother;

        /// <summary>Gets or sets seconds to halve position error; zero applies the incoming presentation position directly.</summary>
        /// <value>A finite, nonnegative half-life in seconds; the default is 0.08 seconds.</value>
        /// <remarks>
        /// Timed SDK motion compensates movement while measurement error remains filtered; without it, moving inputs
        /// trail. Live edits, including zero and enabled changes, retain history and blend their presentation difference
        /// over 0.25 unscaled seconds. The Inspector offers 0-0.5 seconds; code also accepts larger finite values.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The assigned half-life is negative, NaN or infinite.</exception>
        public float PositionHalfLife
        {
            get => _positionHalfLife;
            set
            {
                ValidateTime(value, nameof(value));
                _positionHalfLife = value;
            }
        }

        /// <summary>Gets or sets seconds to halve angular error; zero applies orientation immediately (the default).</summary>
        /// <value>A finite, nonnegative rotation half-life in seconds.</value>
        /// <remarks>Controls reference orientation and scene repositioning when this Ghost is followed with FollowRotation enabled.
        /// Timed model motion advances the filter before it decays correction error, avoiding steady-turn lag.
        /// Pose-derived angular rates are filtered before assistance so measurement steps do not bypass smoothing.
        /// Live edits retain history and blend the setting-induced rotation over 0.25 seconds of real time,
        /// independently of position. Zero applies orientation directly after this transition.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The assigned half-life is negative, NaN or infinite.</exception>
        public float RotationHalfLife
        {
            get => _rotationHalfLife;
            set
            {
                ValidateTime(value, nameof(value));
                _rotationHalfLife = value;
            }
        }

        internal override bool NeedsRefresh => false;

        /// <summary>Discards both filter histories so the next projection uses the current input directly.</summary>
        /// <remarks>Also clears any live-tuning transition. Retains settings, raw Spatial input and separate Extrapolation history.</remarks>
        public void Reset()
        {
            _smoother.Reset();
            Spatial spatial = GetComponent<Spatial>();
            if (spatial != null)
            {
                spatial.ResetTuning();
            }
        }

        internal PresentationPose Prepare(Spatial spatial, PresentationPose pose, double timestamp,
            Double3 motion, Quaternion rotationMotion, bool worldRotationMotion, bool estimatedRotationMotion, PresentationSettings settings, bool preview)
        {
            // Use a copy to try the old settings at the same time and with the same stored state.
            // Save only the step using the new settings. Keep the settings-change blend out of this filter.
            PoseSmoother smoother = _smoother;
            smoother.Prepare(timestamp);
            if (spatial.HasRotation)
            {
                pose.Rotation = smoother.Rotation(pose.Rotation, settings.RotationHalfLife, rotationMotion, worldRotationMotion, estimatedRotationMotion);
            }

            if (spatial.HasPosition)
            {
                pose.Position = smoother.Position(pose.Position, settings.PositionHalfLife, motion);
            }

            if (!preview)
            {
                _smoother = smoother;
            }

            return pose;
        }

        internal void ResetRotation()
        {
            _smoother.ResetRotation();
        }

        internal override void Refresh()
        {
        // Run this after all input traits have supplied their values.
        }

        internal override void ClearBinding()
        {
            Reset();
        }

        internal static void ValidateTime(float value, string parameter)
        {
            if (!SpatialMath.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameter, "The time must be finite and nonnegative.");
            }
        }
    }
}
