# Callback quick start

1. Open `Callbacks.unity` and press Play.
2. The cube moves for four seconds, disappears for two, then returns.
3. Toggle **Tracking** off and on to exercise unsubscribe and restart.

`Bootstrap` configures the realm before its detector starts. It registers a `Marker` root initializer that adds one `MarkerPositionModule` to each `Presence` and creates a `FeedDetector`. That small `PresenceDetector` subclass forwards readings with `Report`; the module applies each reading's position to the root.

`FeedDetector.OnStart` captures an attachment-bound dispatcher with `CaptureDispatcher`, connects `SimulatedFeed.Changed` and `Removed`, and publishes the initial reading. `OnStop` unsubscribes both event handlers. Capturing the dispatcher in the handlers keeps callbacks retained from an old attachment from publishing after a restart. `RealmSetup` owns an isolated realm and its default manifestation blueprint; `AnchorSetup` owns the anchor and its automatic views. The feed remains application-owned.

`Marker.asset` is the manifestation blueprint for `callbacks.marker`. It references `Default Marker Variant.asset`, where an empty variant ID means `Variant.None` and the Full detail level uses `Marker.prefab`. Add another variant asset for each appearance and configure its detail levels there.

Each event carries an immutable `Reading`. Events are applied on a later realm update; `Disappear` removes only the specified entity ID. All callbacks run on Unity's main thread; the dispatcher defers work and does not transfer it between threads. Copy mutable SDK data before publishing it. A real SDK integration must order initial data with live events and make partial subscriptions available to `OnStop` for cleanup when startup throws.
