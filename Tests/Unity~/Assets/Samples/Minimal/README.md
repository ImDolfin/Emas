# Quick start

Open `QuickStart.unity` and press Play. One cube follows a polled position.

- `Reading`: replace this sample record with your SDK's item type.
- `Bootstrap`: implements `IDetectorProvider` and `IRealmConfigurator`. It registers one marker root initializer and creates a `PollingPresenceDetector<Reading>` for its anchor.
- `MarkerPositionModule`: converts each SDK reading to a position on the initialized `Marker` Ghost root.
- Tracking object's Inspector: `RealmSetup` owns an isolated realm and its default manifestation blueprint; `AnchorSetup` sets the anchor ID and automatic views. Anchors can also override realm manifestation blueprints.

Disable/re-enable the Tracking object to dispose/recreate its realm and detector. Polling expects a complete collection; empty means all entities departed.