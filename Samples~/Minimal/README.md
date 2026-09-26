# Quick start

Open `QuickStart.unity` and press Play. One cube follows a polled position.

- `Reading`: replace this sample record with your SDK's item type.
- `Bootstrap`: implements `IDetectorProvider` and `IRealmConfigurator`. It registers one marker root initializer and creates a `MarkerDetector` for its anchor.
- `MarkerDetector`: subclasses `PresenceDetector` and calls `Report` with the marker's stable ID and current reading in `OnStart` and `OnUpdate`.
- `MarkerPositionModule`: converts each SDK reading to a position on the initialized `Marker` Ghost root.
- Tracking object's Inspector: `RealmSetup` owns an isolated realm and its default manifestation blueprint; `AnchorSetup` sets the anchor ID and automatic views. Anchors can also override realm manifestation blueprints.

Disable/re-enable the Tracking object to dispose/recreate its realm and detector. This sample's marker is always present. For an SDK entity that can leave, call `Disappear` with its kind and ID when it departs.