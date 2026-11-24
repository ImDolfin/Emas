namespace Emas
{
    // A tangent basis is a pure function of an ECEF point. Cache it independently of
    // observation timing, so packet resets never keep stale clock or filter state.
    internal struct GeographicPoint
    {
        private bool _hasValue;
        private Double3 _position;
        private GeographicBasis _basis;

        internal GeographicBasis Get(Double3 position, GeographicInput input = null)
        {
            if (!_hasValue || _position != position)
            {
                GeographicBasis basis;
                if (input == null || !input.TryGetBasis(position, out basis))
                {
                    basis = new GeographicBasis(GeoPosition.FromEarthCentered(position));
                }

                Set(position, basis);
            }

            return _basis;
        }

        internal void Set(Double3 position, GeographicBasis basis)
        {
            _position = position;
            _basis = basis;
            _hasValue = true;
        }
    }
}
