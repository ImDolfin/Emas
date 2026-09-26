# Callback quick start

1. Open `Callbacks.unity` and press Play.
2. The cube moves for four seconds, disappears for two, then returns.
3. Toggle **Tracking** off and on to exercise unsubscribe and restart.

`Bootstrap` connects `SimulatedFeed.Changed` and `Removed` through `Listen`, publishes the initial reading, and returns an unsubscribe action. `RealmSetup` owns an isolated realm and its default manifestation blueprint; `AnchorSetup` owns this anchor and its automatic views. `Bootstrap` implements `ISourceProvider` and creates a fresh callback source on each start. The feed remains application-owned.

`Marker.asset` is the manifestation blueprint for `callbacks.marker`. It references `Default Marker Variant.asset`, where an empty variant ID means `Variant.None` and the Full detail level uses `Marker.prefab`. Add another variant asset for each appearance and configure its detail levels there.

Each event carries an immutable `Reading`. Events are applied on a later realm update; explicit removals affect only their entity ID. Copy mutable SDK data before publishing it. A real SDK adapter must order initial data with live events and undo partial subscriptions if startup throws.
