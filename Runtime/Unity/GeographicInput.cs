namespace Emas
{
    // One root's geographic setters share tangent axes. Keep vector caches separate so
    // independent traits can repeat the same packet without converting each channel again.
    internal sealed class GeographicInput
    {
        private struct VectorCache
        {
            internal bool HasValue;
            internal double Latitude;
            internal double Longitude;
            internal Double3 Input;
            internal Double3 Output;
        }

        private bool _hasPosition;
        private GeoPosition _position;
        private Double3 _earthCentered;
        private bool _hasBasis;
        private double _latitude;
        private double _longitude;
        private GeographicBasis _basis;
        private VectorCache _velocity;
        private VectorCache _acceleration;
        private VectorCache _angularVelocity;

        internal Double3 Position(GeoPosition position)
        {
            if (_hasPosition && _position.LatitudeDegrees == position.LatitudeDegrees
                && _position.LongitudeDegrees == position.LongitudeDegrees && _position.HeightMeters == position.HeightMeters)
            {
                return _earthCentered;
            }

            GeographicBasis basis = Basis(position);
            Double3 result = basis.ToEarthCenteredPosition(position.HeightMeters);
            _position = position;
            _earthCentered = result;
            _hasPosition = true;
            return result;
        }

        internal Double3 Velocity(GeoPosition origin, Double3 value)
        {
            return Convert(origin, value, ref _velocity);
        }

        internal Double3 Acceleration(GeoPosition origin, Double3 value)
        {
            return Convert(origin, value, ref _acceleration);
        }

        internal Double3 AngularVelocity(GeoPosition origin, Double3 value)
        {
            return Convert(origin, value, ref _angularVelocity);
        }

        internal bool TryGetBasis(Double3 position, out GeographicBasis basis)
        {
            // Below the ellipsoid, extreme negative heights can have another canonical
            // inverse. Leave those and Earth's centre to the normal ECEF validation.
            if (_hasPosition && _position.HeightMeters >= 0 && position == _earthCentered && position != default(Double3))
            {
                basis = Basis(_position);
                return true;
            }

            basis = default(GeographicBasis);
            return false;
        }

        private GeographicBasis Basis(GeoPosition origin)
        {
            origin.Validate();
            if (!_hasBasis || _latitude != origin.LatitudeDegrees || _longitude != origin.LongitudeDegrees)
            {
                _basis = new GeographicBasis(origin);
                _latitude = origin.LatitudeDegrees;
                _longitude = origin.LongitudeDegrees;
                _hasBasis = true;
            }

            return _basis;
        }

        private Double3 Convert(GeoPosition origin, Double3 value, ref VectorCache cache)
        {
            // Height does not change vector axes, but invalid height must still fail
            // even on a cache hit or on a packet rejected for its timestamp.
            ReferenceFrame.ValidatePosition(value, "eastNorthUp");
            origin.Validate();
            if (cache.HasValue && cache.Latitude == origin.LatitudeDegrees
                && cache.Longitude == origin.LongitudeDegrees && cache.Input == value)
            {
                return cache.Output;
            }

            Double3 result = Basis(origin).ToEarthCentered(new Double3(value.X, value.Z, value.Y));
            cache = new VectorCache
            {
                HasValue = true, Latitude = origin.LatitudeDegrees, Longitude = origin.LongitudeDegrees, Input = value, Output = result
            };
            return result;
        }
    }
}
