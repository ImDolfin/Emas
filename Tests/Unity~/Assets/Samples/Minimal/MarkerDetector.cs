namespace Emas.Minimal
{
    /// <summary>Detects the sample's one permanent entity when attached.</summary>
    internal sealed class MarkerDetector : PresenceDetector
    {
        private readonly MarkerSource _source;

        internal MarkerDetector(MarkerSource source)
        {
            _source = source;
        }

        /// <inheritdoc />
        protected override void OnStart()
        {
            Detect("one", Marker.Kind, source: _source);
        }
    }
}
