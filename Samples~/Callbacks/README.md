# Callback quick start

1. Open `Callbacks.unity` and press Play.
2. The cube moves for four seconds, disappears for two, then returns.
3. Toggle **Tracking** off and on to exercise unsubscribe and restart.

`Bootstrap` connects `SimulatedFeed.Changed` and `Removed` through `Listen`, publishes the initial reading, and returns an unsubscribe action. `SceneSetup` owns the anchor and automatic views. The feed remains application-owned.

Each event carries an immutable `Reading`. Events are applied on a later realm update; explicit removals affect only their entity ID. Copy mutable SDK data before publishing it. A real SDK adapter must order initial data with live events and undo partial subscriptions if startup throws.
