using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Optional Ghost trait that smooths source pose presentation before reference projection.</summary>
    /// <remarks>Author beside Spatial on the same root. Position and rotation have independent half-lives;
    /// no SDK binding is needed. With Prediction present, smooth its result and use its motion estimate to reduce lag.</remarks>
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
        private readonly PoseSmoother _smoother = new PoseSmoother();

        /// <summary>Gets or sets seconds to halve position error; zero applies the incoming presentation position directly.</summary>
        /// <remarks>Defaults to 0.08. After one half-life, 50% of a stationary correction remains; after three, 12.5% remains.
        /// The Inspector offers 0-0.5 seconds; larger finite values are supported in code and create stronger lag.</remarks>
        public float PositionHalfLife
        {
            get => _positionHalfLife;
            set
            {
                ValidateTime(value, nameof(value));
                if (_positionHalfLife != value)
                {
                    _positionHalfLife = value;
                    _smoother.ResetPosition();
                }
            }
        }

        /// <summary>Gets or sets seconds to halve angular error; zero applies orientation immediately (the default).</summary>
        /// <remarks>Controls reference orientation and scene repositioning when this Ghost is followed with FollowRotation enabled.
        /// Changing this setting preserves position history.</remarks>
        public float RotationHalfLife
        {
            get => _rotationHalfLife;
            set
            {
                ValidateTime(value, nameof(value));
                if (_rotationHalfLife != value)
                {
                    _rotationHalfLife = value;
                    _smoother.ResetRotation();
                }
            }
        }

        /// <summary>Discards both filter histories so the next projection uses the current input directly.</summary>
        public void Reset()
        {
            _smoother.Reset();
        }

        internal Quaternion PresentationRotation { get; private set; } = Quaternion.identity;

        internal Double3 Prepare(Spatial spatial, Double3 position, double timestamp, Double3 motion)
        {
            _smoother.Prepare(timestamp);
            if (spatial.HasRotation)
            {
                PresentationRotation = _smoother.Rotation(spatial.Rotation, _rotationHalfLife);
            }

            return spatial.HasPosition ? _smoother.Position(position, _positionHalfLife, motion) : position;
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

        private void OnDisable()
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
