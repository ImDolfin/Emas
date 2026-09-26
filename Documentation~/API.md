# API reference

Runtime APIs use the `Emas` namespace. All operations require Unity's main thread, including callback-source publish/remove delegates and protected `Dispatch`. Applications handle SDK threading before calling Emas; Emas provides no thread synchronization or marshalling. `Realm.Default` is updated automatically by Unity; an isolated `Realm` implements `IDisposable` and advances through explicit `Update()`.

## Fast integration

| API | Contract |
| --- | --- |
| `PollingPresenceSource<TSource, TGhost>(kind)` | Poll on startup and every update by default; create/update entities and remove those absent from a successful complete snapshot |
| `PollingPresenceSource.PollEvery(interval)` | Optionally space reads by a non-negative `TimeSpan`; zero restores every-update polling |
| `CallbackPresenceSource<TSource, TGhost>(kind)` | Subscribe once per attachment; queue individual publications and explicit removals |
| `ISourceProvider.CreateSource()` | A colocated MonoBehaviour creates one source whenever its prefab anchor starts |
| `RealmSetup.Realm` | The isolated realm owned and automatically updated by this component, or null while stopped |
| `RealmSetup.StartRealm()` / `StopRealm()` | Start an isolated realm from prefab settings or dispose it and all of its anchors and sources |
| `AnchorSetup.Anchor` | The live anchor configured by this component, or null while stopped; use it for source restart or replacement |

For polling, configure `.ReadFrom(read)`, `.IdentifyBy(idSelector)` and `.Apply(copyData)` before tracking; optional `.WithVariant(selector)` selects appearances. Callbacks cannot change while attached to an anchor.

`PollEvery(TimeSpan.FromMilliseconds(500))` reads immediately on attachment, then on the first realm update at least 500 ms after the previous read started. It uses unscaled real time on Unity's main thread, runs at most once per update and skips missed intervals without catch-up reads. Restart and reattachment read immediately and reset the deadline. Without inactivity expiry, data and membership stay unchanged between polls. Configure the interval while detached and outside a read; negative intervals are rejected.

Polling requires a non-null full snapshot and unique, non-empty IDs. The entire read is validated before mapping; omitted identities are removed after all mapping callbacks succeed. Failures stop the source and remove its population. SDK clients remain application-owned.

For callbacks, configure `.IdentifyBy(idSelector)`, `.Apply(copyData)` and `.Listen(subscribe)`; optional `.WithVariant(selector)` has the same appearance semantics.

`Listen(Func<Action<TSource>, Action<string>, Action> subscribe)` supplies publish-item and remove-ID callbacks. Return an unsubscribe action, or null when cleanup is unnecessary.

| Callback contract | Behavior |
| --- | --- |
| Configuration | Change only while detached and outside subscription startup; a failed attached source remains unconfigurable |
| Scheduling | Publish/remove on Unity's main thread; all events use the bounded FIFO queue. Selectors and mapping run on a later realm update |
| Identity | Repeated IDs update the same ghost; unknown removals do nothing; an optional inactivity timeout removes silent entities |
| Lifetime | Subscribe once per attachment; cleanup once on stop, including interrupted startup. Old callbacks cannot affect a restarted registration |
| Failure | Null items, empty/null IDs or selector/mapping exceptions stop the source, unsubscribe, remove its ghosts and discard queued work. Cleanup exceptions are logged and retained when no primary failure exists |
| Replacement | Preserve compatible roots during startup handover; republished identities become available and unreported identities are then removed |
| Adapter responsibilities | Keep payloads unchanged until processed; copy mutable SDK data. Order initial publications with live events and undo partial subscriptions before throwing |

Subscription and cleanup run on the Unity thread. Initial items may be published inside `Listen`; they are also deferred.

Put one `RealmSetup` on a prefab or scene object and any number of `AnchorSetup` components on that object or its children. Each active anchor needs a unique ID within its realm and exactly one enabled `MonoBehaviour` implementing `ISourceProvider` on the same GameObject. Its `CreateSource()` returns one new or detached `PresenceSource` whenever the anchor starts. Assign zero or more realm default manifestation blueprints and zero or more anchor overrides, with at most one per kind in either list. Automatic views apply to configured kinds with a view prefab; an empty anchor override can make a kind silent. Nested Realm Setup objects own their own anchors. Each Realm Setup starts on the first update after enable in Play Mode, updates its isolated realm each frame, and stops on disable. `StopRealm()` is repeatable; `StartRealm()` starts a fresh realm while enabled. Source startup failure disposes the partial realm. Configured reference-frame settings create a separate `ReferenceFrame` for each realm. Direct `Realm.Default` and `new Realm()` integrations retain their existing lifecycle and APIs.

## Tracking and lifecycle

| Operation | Contract |
| --- | --- |
| `Realm.Anchors` / `Anchor.Sources` | Copied, read-only membership snapshots; earlier snapshots stay unchanged; disposed owners return empty snapshots. Objects retain their own lifetimes |
| `Realm.RegisterManifestationBlueprint(manifestationBlueprint)` | Register a realm-wide default by kind for anchors without an override; assets stay application-owned |
| `Anchor.RegisterManifestationBlueprint(manifestationBlueprint)` | Register or replace this anchor's override for a kind; released with the anchor |
| `Anchor.UnregisterManifestationBlueprint(kind)` | Remove this anchor's override so its realm default applies; missing overrides are ignored |
| `GetOrCreateAnchor(id, params PresenceSource[] sources)` | Create or reuse an anchor and attach/start supplied sources; overload accepts a `Transform` frame, which must match when reusing. On failure, sources newly attached by this call are removed and prepared identities are restored; existing sources remain |
| `Prepare<TGhost>(anchorId, kind, entityId, variant = null)` | Optionally create an unavailable identity before discovery |
| `TryGetGhost(key, out ghost)` | Look up an exact identity, including prepared ghosts and unavailable ghosts during startup handover; return false/null for missing, invalid or destroyed identities and disposed realms |
| `Query(partialName = null)` | Describe filters over available ghosts in one realm |
| `Query.All(partialName = null)` | Describe filters over available ghosts in every live realm, including realms created later |
| `Query(description)` | Rebind an existing query description to this realm |
| `RemoveAnchor(id)` | Stop its sources and remove all its records, including prepared ghosts |
| `Update()` | Advance an explicitly managed realm; the default realm advances automatically |
| `realm.Dispose()` | Release the realm and all owned state |

An `Anchor` exposes `Id`, `Transform`, `Realm`, `RegisterManifestationBlueprint(manifestationBlueprint)`, `UnregisterManifestationBlueprint(kind)`, `AddSource`, `RemoveSource`, `RestartSource(source)`, `ReplaceSource(current, replacement)` and `Dispose()`. Restart reuses an attached source with its existing configuration; replacement uses a different instance.

During a successful handover, compatible roots and view requests survive when the new registration republishes them. Roots remain unavailable until republished. After the first subsequent realm update and all publications queued during startup have run, identities still unreported are removed.

Source failure removes its population, so recovery creates new roots. Failed restart/replacement startup leaves the failed registration attached for another retry. Restart rejects calls during startup, cleanup or another restart; old callbacks remain invalid. Removal destroys the removed source's population. A source removed and reattached during an update waits until the next update to tick. See [lifecycle rules](Architecture.md#failure-and-cleanup).

## PresenceSource and ghost contracts

| Member | Use |
| --- | --- |
| `IsAttached` | An anchor still owns this source, including a failed registration |
| `IsActive` | The current attachment is starting or accepting updates |
| `InactivityTimeout` | Optional positive `TimeSpan`; remove each ghost after this much unscaled time without publication. Null disables expiry; configure while detached |
| `Name` | Optional application label for diagnostics; empty labels use the source type. Does not affect identity |
| `LastError` | Original first exception from the latest attachment; cleared before startup, retained after stopping/detachment, never overwritten by cleanup or an old registration |
| `LastErrorContext` | Captured anchor, source label and operation for `LastError`; built-in sources also include kind/entity ID when known. Same retention and reset rules |
| `OnStart`, `OnUpdate`, `OnStop` | Override lifecycle hooks; cleanup runs once for a started attachment, including startup failure |
| `GetOrCreate<TGhost>(entityId, kind, variant = null)` | Obtain a stable owned ghost and reset its inactivity deadline; another overload accepts a display name |
| `MarkPublished(ghost)` | Protected hook to reset activity after a custom source writes fresh data to a cached owned ghost |
| `Remove(kind, entityId)` | Remove one owned ghost |
| `Dispatch(action)` | Queue main-thread work for the current attachment; queued work is discarded if that attachment stops |
| `CaptureDispatcher()` | Capture a main-thread dispatcher for the current attachment; callbacks from an earlier attachment are ignored |
| `OwnedGhosts` | Snapshot of owned ghosts, including unavailable ones |
| `IGhost.Key`, `Name`, `Variant`, `IsAvailable` | Read identity, label, appearance and availability |
| `IGhost.TryGet<T>(out part)` | Resolve an optional root component contract; excludes view children and returns false for missing or ambiguous providers |
| `GetRequired<T>()` extension on `IGhost` | Return the single root provider or throw with the ghost key and requested contract |

Custom sources can call `GetOrCreate` and `Remove` on Unity's main thread whenever `IsActive` is true. New or unavailable ghosts created by direct calls outside lifecycle or dispatched callbacks become available on the next realm update; complete their data before that update. Already available ghosts can be updated in place. Inactive `GetOrCreate` throws, while inactive `Remove` is ignored.

`InactivityTimeout` defaults to null. Every `GetOrCreate` publication resets that entity's deadline, including partial updates such as position or articulation. Built-in sources do this automatically. A custom source updating a cached ghost calls `MarkPublished(ghost)` after writing fresh data. Expiry runs after source processing and before query notifications, removes the root and view, and reports ordinary query departures. Later publication creates a new root with the same key. Unowned prepared ghosts do not expire. Choose a timeout that allows for the feed's normal publication interval, including any polling interval.

Capture a dispatcher in `OnStart` when subscribing to SDK callbacks and release the subscription in `OnStop`. The returned callback accepts an `Action` to run during a later realm update and ignores calls after its attachment ends, even if the same source instance restarts. `Dispatch` called directly from an old SDK callback would instead use the source's current attachment. Applications must move SDK events to Unity's main thread before calling either dispatcher.

Application interfaces should be read-only; concrete ghost setters are for source mapping. `Ghost` supplies `IGhost`; root activation happens after publication, so `Awake` must not assume mapped data. Source-specific types and coordinate conversion stay in application sources. One source owns each identity; an application source can compose multiple feeds.

Use `TryGet<T>` when a contract is optional and `GetRequired<T>` when its absence is a setup error. Both inspect only root MonoBehaviours. An ambiguous `TryGet<T>` logs the ghost key, matching component types and instance IDs with a clickable ghost context. It logs once per ghost and contract until a later lookup observes zero or one provider; `GetRequired<T>` also throws on ambiguity. The Ghost Inspector keeps Emas-owned metadata out of prefab editing and shows live key, display name, variant and availability as read-only values in Play Mode. Application fields remain editable.

## Spatial coordinates and reference frames

| Member | Use |
| --- | --- |
| `Realm.ReferenceFrame` | Optional spatial projection configuration; null preserves ordinary positioning |
| `Double3(x, y, z)` | Double-precision Cartesian position or displacement; preserve doubles from the source |
| `Double3.Distance(a, b)` | Distance between simulation positions in double precision |
| `Spatial.SetPosition(position)` / `Position` / `HasPosition` | Publish and read the root's independent double-precision position channel |
| `Spatial.SetRotation(rotation)` / `Rotation` / `HasRotation` | Publish and read the optional orientation channel; without it Emas leaves root rotation untouched |
| `Spatial.IsInRange` | Whether the latest spatial projection can be presented |
| `ReferenceFrame.Position` / `Rotation` | Manual simulation reference pose, or the latest resolved followed pose |
| `ReferenceFrame.UnityPosition` / `UnityRotation` | Desired Unity world pose of the reference; defaults to zero/identity |
| `ReferenceFrame.FollowedGhost` | Optional key to follow within this realm; null uses manual configuration |
| `ReferenceFrame.FollowRotation` | Follow reference orientation as well as position; defaults to true |
| `ReferenceFrame.MaxDistance` | Optional positive double presentation range; null disables the configured limit |
| `ReferenceFrame.HasPosition` | Whether manual configuration or following has provided a usable cached reference position |
| `ReferenceFrame.IsReferenceAvailable` | Whether the configured reference is currently available; loss preserves the last valid pose |
| `TryToUnityPosition(position, out result)` | Project with reference initialization and presentation-range checks |
| `ToSimulationPosition(position)` | Convert a Unity world position back into simulation coordinates |
| `ToUnityRotation(rotation)` / `ToSimulationRotation(rotation)` | Convert orientations using the frame mapping |
| `DistanceTo(position)` | Double-precision distance from the cached simulation reference |

Add enabled `Spatial` components to participating Ghost roots. Source adapters normalize positions into shared Cartesian units/axes for the realm. The realm subtracts the reference in doubles before converting to Unity floats and projects the root in world space, accounting for Anchor parents. Reference movement reprojects all spatial ghosts without requiring another entity publication. Position, rotation and articulation updates remain independent; the spatial API emits no general data-change events.

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

Within each realm, subscriptions notify once while a ghost remains a match. Availability loss permits a fresh notification on recovery. Outside that realm's source, finalization and notification callbacks, current matches notify immediately. Inside those phases, notification is deferred; subscriptions created during notification wait for a later update in that realm. Cross-realm result and callback order is unspecified. Callback exceptions are isolated, and each match is rechecked before invoking the callback.

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
| Omitted/null variant in `Prepare` or `GetOrCreate` | Preserve the existing appearance |
| `Variant.None` | Unspecified appearance; explicitly passing it clears the appearance |
| `WithVariant(None)` | Match unspecified appearances; omitting the filter matches any appearance |
| `DetailLevel` | Non-negative level: None=0, Minimal=1, Reduced=2, Full=3; custom levels allowed |

## Views and manifestation blueprints

| Operation | Effect |
| --- | --- |
| `Manifest(ghost)` | Request Full for a new request; preserve an existing requested detail level |
| `Manifest(ghost, detailLevel)` | Request the selected detail level; return the current view or null |
| `SetDetailLevel(ghost, detailLevel)` | Update detail level; does not create a request for a never-requested ghost |
| `Demanifest(ghost)` or detail level None | Remove the view and request, preserving the ghost |

A `ManifestationBlueprint` describes one `Kind`: an optional Ghost root prefab, references to `ManifestationVariant` assets, and an optional fallback view prefab. Each variant asset describes one `Variant` and holds its view prefabs by `DetailLevel`. For example, an SUV kind can reference separate ModelA, ModelB and ModelC variant assets, each with Full and Reduced views. Create them through **Assets > Create > Emas > Manifestation Blueprint** and **Manifestation Variant**, or configure them in code with `ManifestationVariant.Configure(variant, detailMappings)` and `ManifestationBlueprint.Configure(kind, ghostPrefab, variants, fallbackViewPrefab)`.

Each anchor uses its registered blueprint first, then a realm-wide default for the same kind. `RealmSetup` registers its Inspector blueprints as realm defaults; `AnchorSetup` registers overrides on its own anchor. Each registration copies the blueprint and its variant assets. Editing either asset takes effect for that scope only after re-registration, which refreshes requested views without replacing existing Ghost roots. Re-registering after a kind change releases the old kind. A new Ghost prefab applies only to newly created roots. `ResolveViewPrefab(variant, detailLevel)` selects a prefab; `FallbackViewPrefab` exposes the optional fallback.

When upgrading an existing `Blueprint` asset with entries in its former `_views` list, create one `ManifestationVariant` asset per distinct variant, copy each detail level and prefab into that asset, then assign the variant assets to the renamed `ManifestationBlueprint`. The asset GUID is retained, so realm and anchor references remain assigned, but old `_views` entries cannot become variant asset references automatically. Record those entries before saving the upgraded asset, or recover them from version control. Replace code using `Blueprint.ViewMapping` with `ManifestationVariant.DetailMapping` and the renamed registration methods.

Selection for positive levels: **exact variant/detail level > highest lower positive detail level for that variant > fallback > no view**. `DetailLevel.None` always resolves to no prefab. `ManifestationVariant.DetailMapping(detailLevel, prefab)` requires a positive level and a non-null prefab; variant IDs must be unique within a blueprint, and detail levels must be unique within a variant. A configured blueprint with an unresolved requested view reports a diagnostic and removes any obsolete view. A kind with no assigned blueprint uses the built-in silent default: Emas creates the requested Ghost component on a plain GameObject and no view. An intentionally empty assigned blueprint is silent too; prefab setup skips automatic view requests for it.

`View.RequestedDetailLevel` records the requested level, even when a lower-detail prefab is selected. `ManifestationVariant.DetailMapping.DetailLevel` describes the level supported by that prefab.
Views require an available ghost and a positive request. View binding finishes before activation. Requests made during source changes/finalization defer refresh, so `Manifest` may return the previous view or null until that phase completes. Requests outside those phases refresh immediately.

Emas catches view creation/refresh failures per ghost, cleans up the failed view and logs its key, prefab and requested detail. The source and ghosts stay available. The view request survives: retry with `Manifest`, re-register the manifestation blueprint, or change the variant or requested detail. Ordinary source updates with unchanged appearance do not retry the failed view.

Source: [realm](../Runtime/Realm.cs), [queries](../Runtime/Queries/Query.cs), [manifestation blueprints](../Runtime/Views/ManifestationBlueprint.cs) and [variants](../Runtime/Views/ManifestationVariant.cs).

Integration policy: [Guidelines](Guidelines.md). Authoring errors appear in ManifestationBlueprint/ManifestationVariant/RealmSetup/AnchorSetup Inspectors using the same validation as runtime registration. **Window > Emas** passively shows existing default-realm anchors, source labels, health, available/owned counts and failure context with expandable exception details; isolated realms can be inspected through the snapshot APIs.
