namespace Emas.RelativeWorld
{
    /// <summary>Maps the SDK source to the modules authored on this Anchor's Ghosts.</summary>
    public sealed class GeoInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            string id = presence.Key.EntityId;
            // Capture identity and Presence, then resolve the latest SDK snapshot without retaining the SDK itself.
            root.GetRequired<GeoPositionModule>().Bind(() =>
            {
                GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                return new GeoPosition(reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters);
            });
            root.GetRequired<GeoOrientationModule>().Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            GeoAttachmentModule attachment;
            if (root.TryGet<GeoAttachmentModule>(out attachment))
            {
                attachment.Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            }
        }
    }
}
