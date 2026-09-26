# Quick start

Open `QuickStart.unity` and press Play. One cube follows a polled position.

- `Reading`: replace this sample record with your SDK's item type.
- `Marker`: the application ghost; its setter moves the root.
- `Bootstrap`: implements `ISourceProvider` and creates one polling source for its anchor.
- Tracking object's Inspector: `RealmSetup` owns an isolated realm and its default manifestation blueprint; `AnchorSetup` sets the anchor ID and automatic views. Anchors can also override realm manifestation blueprints.

Disable/re-enable the Tracking object to dispose/recreate its realm and source. Polling expects a complete collection; empty means all entities departed.
