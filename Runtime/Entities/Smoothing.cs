using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that smooths source pose presentation before reference projection.</summary>
    /// <remarks>Author beside Spatial on the same root. Position and rotation have independent half-lives;
    /// no SDK binding is needed. Filters raw observations independently of Prediction. Supplied SDK velocity can
    /// compensate motion between timed observations even with extrapolation disabled. With prediction enabled,
    /// also smooths its extrapolated result and advances between packets within its configured horizon.</remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Spatial))]
    [AddComponentMenu("Emas/Smoothing")]
    public sealed class Smoothing : Trait
    {
        [Tooltip("Seconds to halve the remaining position error. 0: immediate. 0.02-0.05: responsive, less jitter removal. 0.05-0.15: balanced (start at 0.08). 0.15-0.5: smoother but more visible lag.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _positionHalfLife = 0.08f;
        [Tooltip("Seconds to halve the remaining angular error. 0: immediate (default). 0.02-0.05: light filtering; 0.05-0.15: balanced; 0.15-0.5: stronger filtering with lag. On a followed Ghost this also delays scene repositioning when Follow Rotation is on.")]
        [Range(0, 0.5f)]
        [SerializeField] private float _rotationHalfLife;
        private PoseSmoother _smoother;

        /// <summary>Gets or sets seconds to halve position error; zero applies the incoming presentation position directly.</summary>
        /// <value>A finite, nonnegative half-life in seconds; the default is 0.08 seconds.</value>
        /// <remarks>Defaults to 0.08. After one half-life, 50% of a stationary correction remains; after three, 12.5% remains.
        /// The Inspector offers 0-0.5 seconds; larger finite values are supported in code and create stronger lag.
        /// Supplied SDK velocity in Prediction reduces motion lag even when Prediction is disabled; without it,
        /// ordinary position filtering produces speed-dependent lag. Raw input and timestamps are retained.
        /// Live edits retain history and blend the setting-induced displacement over 0.25 seconds of real time,
        /// including changes to zero and the enabled toggle. Inspector edits and property assignments behave identically.</remarks>
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

        /// <summary>Discards both filter histories so the next projection uses the current input directly.</summary>
        /// <remarks>Also clears any live-tuning transition. Retains settings, raw Spatial input and separate Prediction history.</remarks>
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
            Double3 motion, PresentationSettings settings, bool preview)
        {
            // A value copy previews the old settings at the same time and from the same history.
            // Only the new settings commit a step; the presentation transition never feeds back into this filter.
            PoseSmoother smoother = _smoother;
            smoother.Prepare(timestamp);
            if (spatial.HasRotation)
            {
                pose.Rotation = smoother.Rotation(pose.Rotation, settings.RotationHalfLife);
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
            // Evaluate after every input trait has run, in the realm's spatial phase.
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
