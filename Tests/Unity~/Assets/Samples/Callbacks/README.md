# Callback quick start

Open `Callbacks.unity` and press Play. The orange cube moves for four seconds, disappears for two, then returns. Disable and re-enable **Tracking** to exercise unsubscribe and restart.

## Inspect the setup

The scene contains an instance of `Tracking.prefab` plus an authored camera, light and environment. Open the prefab to inspect its components:

- **Realm Setup** assigns `MarkerBlueprint.asset` as the default blueprint.
- **Anchor Setup** uses the `callback-quick-start` anchor ID with automatic views enabled.
- **Feed Source** creates the sample feed and detector, and binds the configured position module before tracking starts.

| Asset | Purpose |
| --- | --- |
| `MarkerBlueprint.asset` | Maps `callbacks.marker` to its Ghost root and default variant. |
| `MarkerRoot.prefab` | Contains a plain `Ghost` and its reusable `MarkerPositionModule`. |
| `Default Marker Variant.asset` | Maps the empty variant ID (`Variant.None`) at Full detail to the view. |
| `MarkerView.prefab` | Contains the cube mesh and its `Marker.mat` material. |
| `Tracking.prefab` | Reusable realm, anchor and callback-source configuration. |

Change `MarkerView.prefab` to customize the visible object. Add another manifestation variant asset for another appearance and assign it to the blueprint. All presentation and scene configuration is saved in assets; the source scripts handle data only.

## Connect an event feed

`FeedSource` implements `IDetectorProvider` and `IRealmConfigurator`. It creates a `FeedDetector`, advances the application-owned `SimulatedFeed`, and binds the `MarkerPositionModule` configured on the Ghost prefab. The reader accesses the feed's current position, so the module only consumes `Vector3`.

`FeedDetector.OnStart` captures an attachment-bound dispatcher with `CaptureDispatcher`, subscribes to `Arrived` and `Removed`, and detects the initially present entity. Position changes emit no detector event; the bound module reads the current value each realm update. `OnStop` unsubscribes both handlers. Capturing the dispatcher keeps callbacks retained from an old attachment from publishing after a restart.

Events are applied on a later realm update; `Disappear` removes only the specified entity ID. All sample callbacks run on Unity's main thread. The dispatcher defers work and does not transfer it between threads. Keep proxy reads on the main thread, or maintain an application-owned snapshot there. A real SDK integration must order initial data with live events and make partial subscriptions available to `OnStop` for cleanup when startup throws.

The detector supplies the `SimulatedFeed` itself as `Presence.Source`. The initializer reads its current position through that weak reference on each update; it does not capture the SDK object in the module delegate.
