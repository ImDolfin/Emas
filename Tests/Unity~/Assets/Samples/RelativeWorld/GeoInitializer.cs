namespace Emas.RelativeWorld
{
    /// <summary>Maps the SDK source to the traits authored on this Anchor's Ghosts.</summary>
    public sealed class GeoInitializer : GhostInitializer
    {
        /// <summary>Binds the required pose traits and any authored motion or attachment traits to the latest delivered SDK packet.</summary>
        /// <param name="presence">The entity identity and simulated SDK source supplying complete observation snapshots.</param>
        /// <param name="root">The authored Ghost whose readers are configured for this identity.</param>
        protected override void Initialize(Presence presence, Ghost root)
        {
            string id = presence.Key.EntityId;
            // Capture identity and Presence, then resolve the latest SDK snapshot without retaining the SDK itself.
            root.GetRequired<GeoPositionTrait>().Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            root.GetRequired<GeoOrientationTrait>().Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            GeoVelocityTrait velocity;
            if (root.TryGet<GeoVelocityTrait>(out velocity))
            {
                velocity.Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            }
            GeoAccelerationTrait acceleration;
            if (root.TryGet<GeoAccelerationTrait>(out acceleration))
            {
                acceleration.Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            }
            GeoAttachmentTrait attachment;
            if (root.TryGet<GeoAttachmentTrait>(out attachment))
            {
                attachment.Bind(() => ((SimulatedGeoSdk)presence.Source).Current[id]);
            }
        }
    }
}
