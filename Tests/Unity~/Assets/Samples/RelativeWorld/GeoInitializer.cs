namespace Emas.RelativeWorld
{
    /// <summary>Maps the SDK source to the traits authored on this Anchor's Ghosts.</summary>
    public sealed class GeoInitializer : GhostInitializer
    {
        /// <inheritdoc />
        protected override void Initialize(Presence presence, Ghost root)
        {
            string id = presence.Key.EntityId;
            // Capture identity and Presence, then resolve the latest SDK snapshot without retaining the SDK itself.
            root.GetRequired<GeoPositionTrait>().Bind(() =>
            {
                GeoPoseReading reading = ((SimulatedGeoSdk)presence.Source).Current[id];
                return new GeoPosition(reading.LatitudeDegrees, reading.LongitudeDegrees, reading.AltitudeMeters);
            });
            root.GetRequired<GeoOrientationTrait>().Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            GeoVelocityTrait velocity;
            if (root.TryGet<GeoVelocityTrait>(out velocity))
            {
                velocity.Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id].EarthCenteredVelocity);
            }
            GeoAttachmentTrait attachment;
            if (root.TryGet<GeoAttachmentTrait>(out attachment))
            {
                attachment.Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            }
        }
    }
}
