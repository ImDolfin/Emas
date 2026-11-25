using System;
using UnityEngine;

namespace Emas
{
    // Filters source observation error before optional motion extrapolation.
    internal struct PoseSmoother
    {
        private const double LogTwo = 0.6931471805599453;
        private Double3 _position;
        private Quaternion _rotation;
        private bool _hasPosition;
        private bool _hasRotation;
        private double _timestamp;
        private double _elapsed;

        internal void Prepare(double timestamp)
        {
            _elapsed = Math.Max(0d, timestamp - _timestamp);
            _timestamp = timestamp;
        }

        internal Double3 Position(Double3 target, float halfLife, Double3 motion)
        {
            if (!_hasPosition || !SpatialMath.IsFinite(halfLife) || halfLife <= 0)
            {
                _position = target;
            }
            else
            {
                // Advance by the predicted motion first, then decay the remaining error toward the observed target.
                // Keep the correction signed: legitimate source corrections may oppose the previous direction of travel.
                double weight = 1d - Math.Exp(-LogTwo * _elapsed / halfLife);
                double x = Coordinate(target.X, _position.X + motion.X, weight);
                double y = Coordinate(target.Y, _position.Y + motion.Y, weight);
                double z = Coordinate(target.Z, _position.Z + motion.Z, weight);
                _position = SpatialMath.IsFinite(x) && SpatialMath.IsFinite(y) && SpatialMath.IsFinite(z)
                    ? new Double3(x, y, z) : target;
            }

            _hasPosition = true;
            return _position;
        }

        internal Quaternion Rotation(Quaternion target, float halfLife, Quaternion motion)
        {
            double weight = SpatialMath.IsFinite(halfLife) && halfLife > 0 ? 1d - Math.Exp(-LogTwo * _elapsed / halfLife) : 1d;
            // Motion was converted to the stored quaternion's world axes by the observation filter.
            Quaternion predicted = motion * _rotation;
            _rotation = _hasRotation && SpatialMath.IsFinite(halfLife) && halfLife > 0
                ? Quaternion.Slerp(predicted, target, (float)weight) : target;
            _hasRotation = true;
            return _rotation;
        }

        internal void ResetPosition()
        {
            _hasPosition = false;
        }

        internal void ResetRotation()
        {
            _hasRotation = false;
        }

        internal void Reset()
        {
            ResetPosition();
            ResetRotation();
        }

        private static double Coordinate(double target, double predicted, double weight)
        {
            double correction = target - predicted;
            return SpatialMath.IsFinite(correction) ? predicted + correction * weight : double.NaN;
        }
    }
}
