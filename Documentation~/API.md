# API reference

Runtime APIs use the `Emas` namespace. All operations require Unity's main thread, including detector publish/disappear callbacks and protected `Dispatch`. Applications handle SDK threading before calling Emas; Emas provides no thread synchronization or marshalling. `Realm.Default` is updated automatically by Unity; an isolated `Realm` implements `IDisposable` and advances through explicit `Update()`.

## Fast integration

| API | Contract |
| --- | --- |
| `PollingPresenceDetector<TSource>(kind)` | Read a complete SDK population at startup and on scheduled polls; report items to the realm and signal omitted IDs as disappeared |
| `CallbackPresenceDetector<TSource>(kind)` | Subscribe to SDK changes and disappearances; process callbacks during a later realm update |
| `Realm.RegisterPresenceInitializer<TGhost>(kind, initialize)` | Choose the Ghost root type and install modules for a kind before attaching detectors |
| `EntityModule<TData>.Apply(data)` | Convert reported SDK data into the initialized presence's root or other application state |
| `IRealmConfigurator.ConfigureRealm(realm)` | Register presence initializers before prefab detectors attach, including later-enabled anchors |
| `IDetectorProvider.CreateDetector()` | Create one new or detached detector for a prefab anchor whenever it starts |
| `RealmSetup.Realm` | Access the isolated, automatically updated realm while the setup is running |
| `RealmSetup.StartRealm()` / `StopRealm()` | Start from prefab settings or dispose the realm and its anchors |
| `AnchorSetup.Anchor` | Access the live anchor for detector restart or replacement |

A detector identifies and labels SDK entities, reports their capabilities and forwards their data as `Presence` updates. The realm chooses a Ghost root, runs any registered per-kind initializer, applies data to matching modules, then makes the Ghost available. Register the initializer before any Ghost of that kind exists. In a prefab, put an enabled `IRealmConfigurator` component under the owning Realm Setup. In code, call `RegisterPresenceInitializer` before attaching a detector. An initializer runs once for a new presence and again when its reported capability set changes. Use `presence.TryGetModule<T>(out module)` before adding a module on a later run.

For polling, configure `.ReadFrom(read)` and `.IdentifyBy(idSelector)` on `PollingPresenceDetector<TSource>`. For callbacks, configure `.IdentifyBy(idSelector)` and `.Listen(subscribe)` on `CallbackPresenceDetector<TSource>`. Both optionally support `.WithName(selector)`, `.WithVariant(selector)` and `.WithCapabilities(selector)`. The capability selector returns interface `Type` values. Null preserves previous capabilities; an empty collection clears them. Capabilities describe what the SDK reports; the realm initializer decides which modules to add. The detector does not choose a Ghost type or mutate its root.

Reported non-null SDK data is delivered to every installed `EntityModule<TData>` that accepts its runtime type. Data with no matching module is ignored, so a detector can still track a silent Presence. Exceptions from a matching module's `Apply` stop the detector and remove its population. A direct custom report rethrows the original exception after cleanup. `EntityModule<TData>.Apply` runs on Unity's main thread before the presence becomes available to queries. A custom detector can call protected `Detect(id, kind, name, variant, capabilities)` for metadata only, or `Report<TData>(id, kind, data, name, variant, capabilities)` to forward data. Without an initializer, a detection or report uses the blueprint's Ghost prefab or Emas's plain `DefaultGhost` root.

`PollingPresenceDetector<TSource>.PollEvery(TimeSpan.FromMilliseconds(500))` reads immediately on attachment, then on the first realm update at least 500 ms after the previous read started. It uses unscaled time, runs at most once per update and skips missed intervals. A restart reads immediately. Configure the interval while detached; negative values are rejected. A read must return a non-null complete snapshot with unique, nonempty IDs. All IDs, selectors and capability types are validated before any item is reported. Missing IDs disappear only after the successful reports. Failure stops the detector and removes its population.

`CallbackPresenceDetector<TSource>.Listen(Func<Action<TSource>, Action<string>, Action> subscribe)` receives publish-item and disappear-by-ID callbacks. Return an unsubscribe action, or null when cleanup is unnecessary. Publish and disappear callbacks must run on Unity's main thread. They enter a bounded FIFO queue and execute during a later realm update; keep SDK payloads unchanged until then, or copy them before publishing. Subscription and cleanup also run on Unity's main thread. Initial items may be published inside `Listen` and are still deferred.

| Callback contract | Behavior |
| --- | --- |
| Configuration | Change only while detached and outside subscription startup; a failed attached detector remains unconfigurable |
| Identity | Repeated IDs update the same Presence; unknown disappearances are ignored |
| Lifetime | Subscribe once per attachment and clean up once on stop, including interrupted startup; old callbacks cannot affect a restarted registration |
| Failure | Null items, empty IDs, invalid capability types and selector or module exceptions stop the detector, remove its population and discard queued work |
| Replacement | Compatible Ghost roots and manifestation requests survive when a replacement detector reports the same identities during handover |
| Adapter responsibility | Order initial reports with live events, copy mutable SDK data and undo partial subscriptions before throwing |

The two-generic `PollingPresenceDetector<TSource, TGhost>` and `CallbackPresenceDetector<TSource, TGhost>` remain available for integrations that map SDK items directly into a typed Ghost with `.Apply((item, ghost) => ...)`. Their polling and callback scheduling, identity and lifecycle rules are the same; they do not use realm-installed `EntityModule<TData>` for that mapping.

Put one `RealmSetup` on a prefab or scene object and any number of `AnchorSetup` components on that object or its children. Each active anchor needs a unique ID within its realm and exactly one enabled `MonoBehaviour` implementing `IDetectorProvider` on the same GameObject. Its `CreateDetector()` returns one new or detached `PresenceDetector` whenever the anchor starts. Realm Setup calls enabled `IRealmConfigurator` components beneath it once per realm lifetime before attaching detectors; newly enabled configurators run before a later-enabled anchor starts. A nested Realm Setup owns its own configurators and anchors. Reparenting an attached anchor retains its original owner until it is disabled; enabling it again attaches it to the Realm Setup in its current hierarchy. Assign zero or more realm default manifestation blueprints and optional anchor overrides, with at most one per kind in either list. Automatic views apply to configured kinds with a view prefab; an empty anchor override can make a kind silent. Realm Setup starts on the first update after enable in Play Mode and updates its isolated realm each frame. `StopRealm()` and disabling dispose it; `StartRealm()` creates a fresh one. Direct `Realm.Default` and `new Realm()` integrations use the same detector and presence APIs.

## Tracking and lifecycle

| Operation | Contract |
| --- | --- |
| `Realm.Anchors` / `Anchor.Detectors` | Copied, read-only membership snapshots; earlier snapshots stay unchanged and disposed owners return empty snapshots |
| `Realm.RegisterPresenceInitializer<TGhost>(kind, initialize)` | Register a root type and module setup before a Ghost of this kind exists |
| `Realm.RegisterManifestationBlueprint(blueprint)` | Register a realm-wide default by kind for anchors without an override |
| `Anchor.RegisterManifestationBlueprint(blueprint)` / `UnregisterManifestationBlueprint(kind)` | Set or remove an anchor override |
| `GetOrCreateAnchor(id, params PresenceDetector[] sources)` | Create or reuse an anchor and attach supplied detectors; overload accepts a parent `Transform` |
| `Prepare<TGhost>(anchorId, kind, entityId, variant = null)` | Optionally create an unavailable Ghost identity before detection |
| `TryGetPresence(key, out presence)` | Find a stable Presence, including one unavailable during a grace period or handover |
| `TryGetGhost(key, out ghost)` | Find its Ghost root, including unavailable and prepared roots |
| `Query(partialName = null)` / `Query.All(partialName = null)` | Query available Ghosts in one realm or across all live realms |
| `Query(description)` | Rebind an immutable query description to this realm |
| `RemoveAnchor(id)` | Stop its detectors and remove its presences, Ghosts and prepared roots |
| `Update()` | Advance an explicitly managed realm; the default realm advances automatically |
| `realm.Dispose()` | Release the realm and all owned state |

An `Anchor` exposes `Id`, `Transform`, `Realm`, `Detectors`, blueprint registration, `AddDetector`, `RemoveDetector`, `RestartDetector`, `ReplaceDetector` and `Dispose()`. Restart reuses an attached detector; replacement attaches a different instance. During successful handover, compatible Presence handles, Ghost roots and manifestation requests survive when identities are reported again. Roots are unavailable until republished. Identities still unreported after the first subsequent update and queued startup reports are removed.

Detector failure or removal deletes its population immediately and discards its queued callbacks, regardless of the configured disappearance grace period. Failed restart or replacement startup leaves that detector attached for retry. A detector removed and reattached during an update waits until the next update to tick. See [lifecycle rules](Architecture.md#failure-and-cleanup).

## PresenceDetector, Presence and modules

| Member | Use |
| --- | --- |
| `PresenceDetector.IsAttached` / `IsActive` | Check whether an anchor owns the detector and whether its current registration accepts updates |
| `PresenceDetector.Name` | Label the detector for diagnostics; it does not name its individual entities |
| `PresenceDetector.LastError` / `LastErrorContext` | Read the first error and its anchor, detector and operation context from the latest attachment |
| `PresenceDetector.OnStart` / `OnUpdate` / `OnStop` | Implement a custom SDK subscription or polling lifecycle |
| `PresenceDetector.Detect(id, kind, name, variant, capabilities)` | Protected metadata-only detection; returns the stable Presence |
| `PresenceDetector.Report<TData>(id, kind, data, name, variant, capabilities)` | Protected report that forwards SDK data to entity modules and resets activity |
| `PresenceDetector.Disappear(kind, id)` | Protected explicit disappearance signal for an owned identity |
| `PresenceDetector.OwnedPresences` | Protected snapshot of monitored presences, including unavailable ones |
| `PresenceDetector.InactivityTimeout` | Optional positive period without reports before a presence disappears; null disables expiry |
| `PresenceDetector.DisappearanceGracePeriod` | Nonnegative period retaining a disappeared Presence and Ghost root while unavailable; zero removes immediately |
| `PresenceDetector.Dispatch(action)` / `CaptureDispatcher()` | Queue Unity-thread work for the current registration; captured dispatchers reject stale callbacks |
| `Presence.Key` / `Name` / `Variant` | Stable identity, per-entity label and visual variant |
| `Presence.IsAvailable` / `IsRemoved` / `Root` | Detection availability, final removal state and the initialized Ghost root |
| `Presence.Capabilities` / `HasCapability<T>()` | Snapshot and exact-interface check of SDK-reported capabilities |
| `Presence.Modules` / `TryGetModule<T>()` / `AddModule(module)` | Inspect or install realm-owned SDK data converters |
| `IGhost.TryGet<T>(out part)` / `GetRequired<T>()` | Resolve root MonoBehaviour interfaces for consumers; these are separate from reported capabilities |

A Presence is stable from its first detection or report until final removal. Its Key combines anchor ID, Kind and SDK entity ID; the same key in a different realm identifies a different Presence. `TryGetPresence` can return an unavailable Presence, so check `IsAvailable` before treating its data as current. `IsRemoved` becomes true after final removal, and a later report creates a new handle. `Root` holds the invisible Ghost even when no view has been requested. `Prepare<TGhost>` creates only an unavailable Ghost; `TryGetPresence` stays false until a detector first reports that identity. Consumers still use `IGhost` queries and root interfaces.

Declare SDK capabilities as interface types with `WithCapabilities`, `Detect` or `Report<TData>`. Emas validates and snapshots the types and removes duplicates. Reporting a changed set reruns the realm initializer. Capability metadata alone does not install components: the initializer inspects `presence.HasCapability<T>()` and adds the modules needed for that Kind. `Presence.AddModule` accepts a new, unbound `EntityModule` after the root has been created. Use `TryGetModule<T>` to avoid adding the same module when the initializer runs again.

Subclass `EntityModule<TData>` and implement `Apply(TData data)` to move one SDK data type into the Ghost root or another presence-owned component. One report calls every compatible module. When no module accepts a payload, Emas leaves that data unapplied and continues tracking the Presence. Exceptions raised by an accepting module stop the detector. Initialize the root and modules before its first data report completes; the realm makes it available and notifies queries only after the update batch is complete. For metadata-only integrations, omit data and the module. When no initializer or Ghost prefab is registered, Emas creates a plain `DefaultGhost` root.

`InactivityTimeout` defaults to null. Each detection or data report resets the individual identity's inactivity deadline. Choose a timeout longer than the feed's normal interval; feeds that publish only changed values should normally leave it disabled. `DisappearanceGracePeriod` defaults to zero. An explicit `Disappear` or inactivity expiry makes the presence unavailable immediately; with positive grace, its stable Presence and Ghost root remain inactive until the deadline. A detection or report before that deadline restores them and retains an existing manifestation request. Repeated disappearances do not extend the deadline. Query subscriptions see a departure on availability loss. Once grace expires, the realm removes the root and view; a later detection or report creates a new Presence. Detector failure, anchor removal and realm disposal bypass grace.

The direct-Ghost path remains available: custom detectors can use protected `GetOrCreate<TGhost>`, `MarkPublished(ghost)` and `OwnedGhosts`, and the two-generic adapters use `Apply` to write to a typed Ghost. `GetOrCreate` and `MarkPublished` reset inactivity deadlines. Publishing cached data with `MarkPublished` during disappearance grace cancels removal and restores availability during finalization, retaining the same root and view request. Use the one-generic detector and entity modules when the SDK integration should know only detection metadata and payloads.

Capture a dispatcher in `OnStart` when subscribing to SDK callbacks and release the subscription in `OnStop`. The returned callback accepts an `Action` to run during a later realm update; calls retained from an earlier attachment are ignored. Calling `Dispatch` directly from an old callback instead targets the detector's current attachment. Move SDK events to Unity's main thread before using either mechanism.

Use `IGhost.TryGet<T>` for optional application interfaces and `GetRequired<T>` when absence is a setup error. Both inspect only root MonoBehaviours. Ambiguous providers are reported with the Ghost key and component types. The Ghost Inspector shows Emas-owned metadata read-only in Play Mode while application fields remain editable.

## Spatial coordinates and reference frames

| Member | Use |
| --- | --- |
| `Realm.ReferenceFrame` | Optional spatial projection configuration; null preserves ordinary positioning |
| `Double3(x, y, z)` | Double-precision Cartesian position or displacement; preserve SDK double values |
| `Double3.Distance(a, b)` | Distance between shared Cartesian positions in double precision |
| `Spatial.SetPosition(position)` / `Position` / `HasPosition` | Publish and read the root's independent double-precision position channel |
| `Spatial.SetRotation(rotation)` / `Rotation` / `HasRotation` | Publish and read the optional orientation channel; without it Emas leaves root rotation untouched |
| `Spatial.IsInRange` | Whether the latest spatial projection can be presented |
| `ReferenceFrame.Position` / `Rotation` | Manual reference pose in shared Cartesian coordinates, or the latest resolved followed pose |
| `ReferenceFrame.UnityPosition` / `UnityRotation` | Desired Unity world pose of the reference; defaults to zero/identity |
| `ReferenceFrame.FollowedGhost` | Optional key to follow within this realm; null uses manual configuration |
| `ReferenceFrame.FollowRotation` | Follow reference orientation as well as position; defaults to true |
| `ReferenceFrame.MaxDistance` | Optional positive double presentation range; null disables the configured limit |
| `ReferenceFrame.HasPosition` | Whether manual configuration or following has provided a usable cached reference position |
| `ReferenceFrame.IsReferenceAvailable` | Whether the configured reference is currently available; loss preserves the last valid pose |
| `TryToUnityPosition(position, out result)` | Project with reference initialization and presentation-range checks |
| `ToSimulationPosition(position)` | Convert a Unity world position into shared Cartesian coordinates |
| `ToUnityRotation(rotation)` / `ToSimulationRotation(rotation)` | Convert orientations using the frame mapping |
| `DistanceTo(position)` | Double-precision distance from the cached reference |

Add enabled `Spatial` components to participating Ghost roots. Detector adapters or entity modules normalize positions into shared Cartesian units/axes for the realm. The realm subtracts the reference in doubles before converting to Unity floats and projects the root in world space, accounting for Anchor parents. Reference movement reprojects all spatial ghosts without requiring another entity publication. Position, rotation and articulation updates remain independent; the spatial API emits no general data-change events.

Before the first position/reference and outside the presentation range, spatial views and root rendering/colliders are suppressed while identity, availability and scripts remain active. Requested views return on range entry. Reference loss freezes its last valid pose and sets `IsReferenceAvailable` false; before any valid reference, presentation stays suppressed. Disabling `Spatial` or clearing `Realm.ReferenceFrame` releases spatial control. See [relative-world integration](Spatial.md) for complete setup, channel mapping and precision guidance.

## Queries and subscriptions

Use `TryGetGhost` when the full `Key` is known. It reads the realm registry without creating, activating or updating anything. Check `ghost.IsAvailable` before consuming its data; a found ghost may be prepared or awaiting publication during startup handover. Keys are case-sensitive and resolved only within the receiving realm. Removal stops lookup immediately, even before Unity finishes destroying the object.

Queries are immutable and combine all filters. They never create ghosts or components. `realm.Query()` searches one realm; `Query.All()` searches every live realm, including `Realm.Default`, realms created by code, and prefab-configured realms. An all-realm query also sees realms created after the query or subscription. Disposing a realm removes its matches. `realm.Query(Query.All().OfKind(kind))` applies that description to just `realm`.

| Filter/result | Meaning |
| --- | --- |
| `Query(partialName)` / `WithExactName(name)` | Case-insensitive substring / exact display-name match |
| `OfKind(kind)` / `InAnchor(id)` / `WithVariant(variant)` | Exact, case-sensitive ID match |
| `With<T>()` | Require a root contract |
| Enumeration / `Count` | Current available matches; empty when none match |
| `FirstOrDefault()` | First match or null; no ordering guarantee |
| `Single()` | Exactly one match; otherwise throws |
| `OnAvailable(callback)` | Notify current and future complete matches; dispose the returned subscription to stop |
| `Observe(onEnter, onLeave)` | Paired membership callbacks: `IGhost` on entry, `Key` on departure |
| `ObserveWithRealm(onEnter, onLeave)` | Paired callbacks with the owning `Realm` on entry and departure, so matching keys in separate realms remain distinguishable |

Within each realm, subscriptions notify once while a ghost remains a match. Availability loss permits a fresh notification on recovery. Outside that realm's detector, finalization and notification callbacks, current matches notify immediately. Inside those phases, notification is deferred; subscriptions created during notification wait for a later update in that realm. Cross-realm result and callback order is unspecified. Callback exceptions are isolated, and each match is rechecked before invoking the callback.

`Observe` reports departure when a previously delivered ghost is removed, becomes unavailable or no longer matches. Departures run at the update notification phase, before that subscription's arrivals. Removal and availability loss remain observable even if the identity returns before the next update; filter changes are evaluated at notification time. A departure receives a `Key` because its Unity object may already be destroyed. Keys identify ghosts within a realm, so two realms may have the same key. Use `ObserveWithRealm` when a consumer must tell those departures apart. Disposing a realm reports departures for previously observed matches of an all-realm query; a realm-scoped subscription instead ends without synthetic departures. Disposing either subscription cancels pending callbacks; consumers clear their own retained state.

## Typed values

```csharp
public static readonly Kind Car = new Kind("vehicles.car");
public static readonly Variant SmallCar = new Variant("small-car");
```

Declare named constants in application classes for autocomplete. Kinds and variants are distinct extensible value types; raw strings and kinds cannot be passed as variants. A variant does not enforce membership in a kind or guarantee a prefab mapping.

| Value | Meaning |
| --- | --- |
| `Key` | Identity tuple: anchor ID, kind and entity ID |
| Omitted/null variant in `Detect`, `Report`, `Prepare` or `GetOrCreate` | Preserve the existing appearance |
| `Variant.None` | Unspecified appearance; explicitly passing it clears the appearance |
| `WithVariant(None)` | Match unspecified appearances; omitting the filter matches any appearance |
| `DetailLevel` | Non-negative level: None=0, Minimal=1, Reduced=2, Full=3; custom levels allowed |

## Views and manifestation blueprints

| Operation | Effect |
| --- | --- |
| `Manifest(presence)` or `Manifest(ghost)` | Request Full for a new request; preserve an existing requested detail level |
| `Manifest(presence, detailLevel)` or `Manifest(ghost, detailLevel)` | Request the selected detail level; return the current view or null |
| `SetDetailLevel(presence, detailLevel)` or `SetDetailLevel(ghost, detailLevel)` | Update detail level; does not create a request for a never-requested entity |
| `Demanifest(presence)` or `Demanifest(ghost)` | Remove the view and request while retaining the Presence and Ghost root |

A `ManifestationBlueprint` describes one `Kind`: an optional Ghost root prefab, references to `ManifestationVariant` assets, and an optional fallback view prefab. Each variant asset describes one `Variant` and holds its view prefabs by `DetailLevel`. For example, an SUV kind can reference separate ModelA, ModelB and ModelC variant assets, each with Full and Reduced views. Create them through **Assets > Create > Emas > Manifestation Blueprint** and **Manifestation Variant**, or configure them in code with `ManifestationVariant.Configure(variant, detailMappings)` and `ManifestationBlueprint.Configure(kind, ghostPrefab, variants, fallbackViewPrefab)`.

Each anchor uses its registered blueprint first, then a realm-wide default for the same kind. `RealmSetup` registers its Inspector blueprints as realm defaults; `AnchorSetup` registers overrides on its own anchor. Each registration copies the blueprint and its variant assets. Editing either asset takes effect for that scope only after re-registration, which refreshes requested views without replacing existing Ghost roots. Re-registering after a kind change releases the old kind. A new Ghost prefab applies only to newly created roots. `ResolveViewPrefab(variant, detailLevel)` selects a prefab; `FallbackViewPrefab` exposes the optional fallback.

When upgrading an existing `Blueprint` asset with entries in its former `_views` list, create one `ManifestationVariant` asset per distinct variant, copy each detail level and prefab into that asset, then assign the variant assets to the renamed `ManifestationBlueprint`. The asset GUID is retained, so realm and anchor references remain assigned, but old `_views` entries cannot become variant asset references automatically. Record those entries before saving the upgraded asset, or recover them from version control. Replace code using `Blueprint.ViewMapping` with `ManifestationVariant.DetailMapping` and the renamed registration methods.

Selection for positive levels: **exact variant/detail level > highest lower positive detail level for that variant > fallback > no view**. `DetailLevel.None` always resolves to no prefab. `ManifestationVariant.DetailMapping(detailLevel, prefab)` requires a positive level and a non-null prefab; variant IDs must be unique within a blueprint, and detail levels must be unique within a variant. A configured blueprint with an unresolved requested view reports a diagnostic and removes any obsolete view. A kind with no assigned blueprint uses the built-in silent default. A one-generic detector gets the initializer's Ghost type or `DefaultGhost`; a direct-Ghost detector gets its requested Ghost type. The plain root has no view. An intentionally empty assigned blueprint is silent too; prefab setup skips automatic view requests for it.

`View.RequestedDetailLevel` records the requested level, even when a lower-detail prefab is selected. `ManifestationVariant.DetailMapping.DetailLevel` describes the level supported by that prefab.
Views require an available ghost and a positive request. View binding finishes before activation. Requests made during detector reports or finalization defer refresh, so `Manifest` may return the previous view or null until that phase completes. Requests outside those phases refresh immediately.

Emas catches view creation/refresh failures per ghost, cleans up the failed view and logs its key, prefab and requested detail. The detector and Ghosts stay available. The view request survives: retry with `Manifest`, re-register the manifestation blueprint, or change the variant or requested detail. Ordinary detector reports with unchanged appearance do not retry the failed view.

Implementation: [detectors](../Runtime/Tracking/PresenceDetector.cs), [Presence](../Runtime/Entities/Presence.cs), [entity modules](../Runtime/Entities/EntityModule.cs), [realm](../Runtime/Realm.cs), [queries](../Runtime/Queries/Query.cs), [manifestation blueprints](../Runtime/Views/ManifestationBlueprint.cs) and [variants](../Runtime/Views/ManifestationVariant.cs).

Integration policy: [Guidelines](Guidelines.md). Authoring errors appear in ManifestationBlueprint/ManifestationVariant/RealmSetup/AnchorSetup Inspectors using the same validation as runtime registration. **Window > Emas** passively shows existing default-realm anchors, detector labels, health, available/owned counts and failure context with expandable exception details; isolated realms can be inspected through the snapshot APIs.
