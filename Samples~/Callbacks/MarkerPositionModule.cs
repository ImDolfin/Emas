using System;

namespace Emas.Callbacks
{
    /// <summary>
    /// Applies callback readings to an initialized Marker Ghost root.
    /// </summary>
    public sealed class MarkerPositionModule : EntityModule<Reading>
    {
        private readonly Marker _marker;

        /// <summary>
        /// Creates a position module for one marker root.
        /// </summary>
        /// <param name="marker">The initialized root to update.</param>
        public MarkerPositionModule(Marker marker)
        {
            _marker = marker ?? throw new ArgumentNullException(nameof(marker));
        }

        /// <summary>
        /// Applies a reported local position to the marker root.
        /// </summary>
        /// <param name="reading">The detector's latest reading.</param>
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
