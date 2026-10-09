using System;
using UnityEngine;

namespace Emas
{
    // Fade only the difference introduced by an edit. Never feed this correction back into prediction or smoothing.
    internal sealed class PresentationTuning
    {
        private const double Duration = 0.25d;
        private bool _initialized;
        private Double3 _positionOffset;
        private Quaternion _rotationOffset = Quaternion.identity;
        private double _positionStarted;
        private double _rotationStarted;
        private bool _positionActive;
        private bool _rotationActive;

        internal PresentationSettings Settings { get; private set; }

        internal bool Changed(PresentationSettings settings)
        {
            return _initialized && (!Settings.SamePosition(settings) || !Settings.SameRotation(settings));
        }

        internal PresentationPose Apply(PresentationPose previous, PresentationPose current,
            PresentationSettings settings, double timestamp)
        {
            try
            {
                current.Position = ApplyPosition(previous.Position, current.Position, settings, timestamp);
                current.Rotation = ApplyRotation(previous.Rotation, current.Rotation, settings, timestamp);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Extreme finite inputs can overflow a difference. Keep valid baseline presentation instead.
                Reset();
            }

            Settings = settings;
            _initialized = true;
            return current;
        }

        internal void Reset()
        {
            _initialized = false;
            _positionOffset = default(Double3);
            _positionActive = false;
            ResetRotation();
        }

        internal void ResetRotation()
        {
            _rotationOffset = Quaternion.identity;
            _rotationActive = false;
        }

        private Double3 ApplyPosition(Double3 previous, Double3 current, PresentationSettings settings, double timestamp)
        {
            bool changed = _initialized && !Settings.SamePosition(settings);
            if (!_positionActive && !changed)
            {
                return current;
            }

            double remaining = _positionActive ? Remaining(timestamp, _positionStarted) : 0;
            Double3 offset = _positionOffset * remaining;
            _positionActive = remaining > 0;
            if (changed)
            {
                // Preview the old behavior at NOW, not at the last rendered frame. Repeated slider edits
                // therefore retain ordinary forward motion instead of freezing a fast-moving entity.
                offset += previous - current;
                _positionOffset = offset;
                _positionStarted = timestamp;
                _positionActive = offset != default(Double3);
            }

            return current + offset;
        }

        private Quaternion ApplyRotation(Quaternion previous, Quaternion current, PresentationSettings settings, double timestamp)
        {
            bool changed = _initialized && !Settings.SameRotation(settings);
            if (!_rotationActive && !changed)
            {
                return current;
            }

            double remaining = _rotationActive ? Remaining(timestamp, _rotationStarted) : 0;
            Quaternion offset = Quaternion.Slerp(Quaternion.identity, _rotationOffset, (float)remaining);
            _rotationActive = remaining > 0;
            if (changed)
            {
                offset = offset * previous * Quaternion.Inverse(current);
                _rotationOffset = offset;
                _rotationStarted = timestamp;
                _rotationActive = !offset.Equals(Quaternion.identity);
            }

            return offset * current;
        }

        private static double Remaining(double timestamp, double started)
        {
            double progress = Math.Min(1d, Math.Max(0d, (timestamp - started) / Duration));
            // Smoothstep has zero slope at each end and reaches exactly zero, leaving no permanent offset.
            return 1d - progress * progress * (3d - 2d * progress);
        }
    }
}
