namespace Emas.AircraftFormation
{
    /// <summary>Connects each authored aircraft Ghost to its independently delivered SDK observation.</summary>
    public sealed class FlightInitializer : GhostInitializer
    {
        /// <summary>Binds by stable aircraft identity and resolves the current source through its Presence.</summary>
        /// <param name="presence">The aircraft identity and current FlightSource.</param>
        /// <param name="root">The authored root with its FlightPoseTrait.</param>
        protected override void Initialize(Presence presence, Ghost root)
        {
            root.GetRequired<FlightPoseTrait>().Bind(() => ((FlightSource)presence.Source).Read(presence.Key.EntityId));
        }
    }
}
