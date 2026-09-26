using System;

namespace Emas.Minimal
{
    /// <summary>
    /// Converts SDK readings into position on a marker Ghost root.
    /// </summary>
    public sealed class MarkerPositionModule : EntityModule<Reading>
    {
        private readonly Marker _marker;

        /// <summary>
        /// Creates one position module for a marker.
        /// </summary>
        /// <param name="marker">The initialized marker root.</param>
        public MarkerPositionModule(Marker marker)
        {
            _marker = marker ?? throw new ArgumentNullException(nameof(marker));
        }

        /// <summary>
        /// Applies one polled position to the marker root.
        /// </summary>
        /// <param name="reading">The SDK reading.</param>
        public override void Apply(Reading reading)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            _marker.SetPosition(reading.Position);
        }
    }
}