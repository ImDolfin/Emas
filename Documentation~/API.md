# API reference

Runtime APIs use the `Emas` namespace. All operations require Unity's main thread, including callback-source publish/remove delegates and protected `Dispatch`. Applications handle SDK threading before calling Emas; Emas provides no thread synchronization or marshalling. `Realm.Default` is updated automatically by Unity; an isolated `Realm` implements `IDisposable` and advances through explicit `Update()`.

## Fast integration

| API | Contract |
| --- | --- |
| `PollingPresenceSource<TSource, TGhost>(kind)` | Poll on startup and every update by default; create/update entities and remove those absent from a successful complete snapshot |
| `PollingPresenceSource.PollEvery(interval)` | Optionally space reads by a non-negative `TimeSpan`; zero restores every-update polling |
| `CallbackPresenceSource<TSource, TGhost>(kind)` | Subscribe once per attachment; queue individual publications and explicit removals |
| `SceneSetup.Track(params PresenceSource[] sources)` | Register Inspector blueprints on one owned anchor under the component transform |
| `SceneSetup.Anchor` | Current owned anchor, or null when stopped; use it for source restart or replacement |
| `SceneSetup.StopTracking()` | Release the owned anchor, ghosts, views and subscriptions; keep the component enabled and ready for another `Track` |

For polling, configure `.ReadFrom(read)`, `.IdentifyBy(idSelector)` and `.Apply(copyData)` before tracking; optional `.WithVariant(selector)` selects appearances. Callbacks cannot change while attached to an anchor.

`PollEvery(TimeSpan.FromMilliseconds(500))` reads immediately on attachment, then on the first realm update at least 500 ms after the previous read started. It uses unscaled real time on Unity's main thread, runs at most once per update and skips missed intervals without catch-up reads. Restart and reattachment read immediately and reset the deadline. Data and membership stay unchanged between polls. Configure the interval while detached and outside a read; negative intervals are rejected.

Polling requires a non-null full snapshot and unique, non-empty IDs. The entire read is validated before mapping; departures run only after all mapping callbacks succeed. Failures use normal source stop/unavailability behavior. SDK clients remain application-owned.

For callbacks, configure `.IdentifyBy(idSelector)`, `.Apply(copyData)` and `.Listen(subscribe)`; optional `.WithVariant(selector)` has the same appearance semantics.

`Listen(Func<Action<TSource>, Action<string>, Action> subscribe)` supplies publish-item and remove-ID callbacks. Return an unsubscribe action, or null when cleanup is unnecessary.

| Callback contract | Behavior |
| --- | --- |
| Configuration | Change only while detached and outside subscription startup; a failed attached source remains unconfigurable |
| Scheduling | Publish/remove on Unity's main thread; all events use the bounded FIFO queue. Selectors and mapping run on a later realm update |
| Identity | Repeated IDs update the same ghost; unknown removals do nothing; untouched entities remain |
| Lifetime | Subscribe once per attachment; cleanup once on stop, including interrupted startup. Old callbacks cannot affect a restarted registration |
| Failure | Null items, empty/null IDs or selector/mapping exceptions stop the source, unsubscribe, retain unavailable ghosts and discard queued work. Cleanup exceptions are logged and retained when no primary failure exists |
| Replacement | Preserve compatible roots; each becomes available when republished. Unreported identities are not deleted |
| Adapter responsibilities | Keep payloads unchanged until processed; copy mutable SDK data. Order initial publications with live events and undo partial subscriptions before throwing |

Subscription and cleanup run on the Unity thread. Initial items may be published inside `Listen`; they are also deferred.

`SceneSetup` must be enabled and its anchor ID unused. Call `Track` once per tracking lifetime. Automatic views apply only to its assigned blueprint kinds. `StopTracking` and disable clean up tracking and subscriptions; call `Track` again to start another lifetime. Repeated stops are harmless. Stopping during startup cancels that attempt and cleans up any returned callback subscription. Inspector blueprint registrations end with the owned anchor; other anchors are unaffected.

## Tracking and lifecycle

| Operation | Contract |
| --- | --- |
| `Realm.Anchors` / `Anchor.Sources` | Copied, read-only membership snapshots; earlier snapshots stay unchanged; disposed owners return empty snapshots. Objects retain their own lifetimes |
| `Realm.RegisterBlueprint(blueprint)` | Register a realm-wide default by kind for anchors without an override; assets stay application-owned |
| `Anchor.RegisterBlueprint(blueprint)` | Register or replace this anchor's override for a kind; released with the anchor |
| `Anchor.UnregisterBlueprint(kind)` | Remove this anchor's override so its realm default applies; missing overrides are ignored |
| `GetOrCreateAnchor(id, params PresenceSource[] sources)` | Create or reuse an anchor and attach/start supplied sources; overload accepts a `Transform` frame, which must match when reusing. On failure, sources newly attached by this call are removed and prepared identities are restored; existing sources remain |
| `Prepare<TGhost>(anchorId, kind, entityId, variant = null)` | Optionally create an unavailable identity before discovery |
| `TryGetGhost(key, out ghost)` | Look up an exact identity, including prepared and retained unavailable ghosts; return false/null for missing, invalid or destroyed identities and disposed realms |
| `Query(partialName = null)` | Describe filters over available ghosts |
| `Query(description)` | Rebind an existing query description to this realm |
| `RemoveAnchor(id)` | Stop its sources and remove all its records, including prepared ghosts |
| `Update()` | Advance an explicitly managed realm; the default realm advances automatically |
| `realm.Dispose()` | Release the realm and all owned state |

An `Anchor` exposes `Id`, `Transform`, `Realm`, `RegisterBlueprint(blueprint)`, `UnregisterBlueprint(kind)`, `AddSource`, `RemoveSource`, `RestartSource(source)`, `ReplaceSource(current, replacement)` and `Dispose()`. Restart reuses the attached active or failed source with its existing configuration; replacement uses a different instance. Both retain compatible identities and view requests, marking ghosts unavailable until republished. Restart rejects calls during startup, cleanup or another restart. Startup failure leaves the restarted source attached for another retry; callbacks from its previous registration remain invalid. Removal destroys the removed source's population. A source removed and reattached during an update waits until the next update for its new registration to tick. See [lifecycle rules](Architecture.md#failure-and-cleanup).

## PresenceSource and ghost contracts

| Member | Use |
| --- | --- |
| `IsAttached` | An anchor still owns this source, including a failed registration |
| `IsActive` | The current attachment is starting or accepting updates |
| `Name` | Optional application label for diagnostics; empty labels use the source type. Does not affect identity |
| `LastError` | Original first exception from the latest attachment; cleared before startup, retained after stopping/detachment, never overwritten by cleanup or an old registration |
| `LastErrorContext` | Captured anchor, source label and operation for `LastError`; built-in sources also include kind/entity ID when known. Same retention and reset rules |
| `OnStart`, `OnUpdate`, `OnStop` | Override lifecycle hooks; cleanup runs once for a started attachment, including startup failure |
| `GetOrCreate<TGhost>(entityId, kind, variant = null)` | Obtain a stable owned ghost; another overload accepts a display name |
| `Remove(kind, entityId)` | Remove one owned ghost |
| `Dispatch(action)` | Defer source work from the main thread to a later update; stopped/stale registrations cannot execute it |
| `OwnedGhosts` | Snapshot of owned ghosts, including unavailable ones |
| `IGhost.Key`, `Name`, `Variant`, `IsAvailable` | Read identity, label, appearance and availability |
| `IGhost.TryGet<T>(out part)` | Resolve an optional root component contract; excludes view children and returns false for missing or ambiguous providers |
| `GetRequired<T>()` extension on `IGhost` | Return the single root provider or throw with the ghost key and requested contract |

Application interfaces should be read-only; concrete ghost setters are for source mapping. `Ghost` supplies `IGhost`; root activation happens after publication, so `Awake` must not assume mapped data. Source-specific types and coordinate conversion stay in application sources. One source owns each identity; an application source can compose multiple feeds.

Use `TryGet<T>` when a contract is optional and `GetRequired<T>` when its absence is a setup error. Both inspect only root MonoBehaviours. An ambiguous `TryGet<T>` logs the ghost key, matching component types and instance IDs with a clickable ghost context. It logs once per ghost and contract until a later lookup observes zero or one provider; `GetRequired<T>` also throws on ambiguity. The Ghost Inspector keeps Emas-owned metadata out of prefab editing and shows live key, display name, variant and availability as read-only values in Play Mode. Application fields remain editable.

## Queries and subscriptions

Use `TryGetGhost` when the full `Key` is known. It reads the realm registry without creating, activating or updating anything. Check `ghost.IsAvailable` before consuming its data; a found ghost may be prepared or retained after failure or replacement. Keys are case-sensitive and resolved only within the receiving realm. Removal stops lookup immediately, even before Unity finishes destroying the object.

Queries are immutable and combine all filters. They never create ghosts or components.

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

Subscriptions notify once while a ghost remains a match. Availability loss permits a fresh notification on recovery. Outside source/finalization/notification callbacks, current matches notify immediately. Inside those phases, notification is deferred; subscriptions created during notification wait for a later update. Callback exceptions are isolated, and each match is rechecked before invoking the callback.

`Observe` reports departure when a previously delivered ghost is removed, becomes unavailable or no longer matches. Departures run at the update notification phase, before that subscription's arrivals. Removal and availability loss remain observable even if the identity returns before the next update; filter changes are evaluated at notification time. A departure receives a `Key` because its Unity object may already be destroyed. Disposing the subscription or realm cancels pending callbacks without synthesizing departures; consumers clear their own retained state.

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

## Views and blueprints

| Operation | Effect |
| --- | --- |
| `Manifest(ghost)` | Request Full for a new request; preserve an existing requested detail level |
| `Manifest(ghost, detailLevel)` | Request the selected detail level; return the current view or null |
| `SetDetailLevel(ghost, detailLevel)` | Update detail level; does not create a request for a never-requested ghost |
| `Demanifest(ghost)` or detail level None | Remove the view and request, preserving the ghost |

A blueprint supplies a kind, optional ghost prefab, view mappings and optional fallback. Each anchor uses its own registered blueprint first, then a realm-wide default for that kind. `SceneSetup` registers Inspector blueprints only on its anchor. Re-registering either scope refreshes existing requested views on the next realm update. Re-register an asset after changing its kind to release its old registration and refresh both kinds. Removing an anchor override restores the realm default on the next update. Existing ghost roots remain intact; a new ghost prefab applies to roots created later. `ResolveViewPrefab(variant, detailLevel)` selects a prefab; `FallbackViewPrefab` exposes the configured fallback asset. Configure through the Inspector or `Configure(kind, ghostPrefab, views, fallbackViewPrefab)`. A mapping is `ViewMapping(variant, detailLevel, prefab)`; duplicate variant/detail level pairs, non-positive mapping detail levels and null view prefabs are rejected.

Selection: **exact variant/detail level > highest lower positive detail level for that variant > fallback > no view**. Missing selection removes an obsolete view and reports a diagnostic. Selecting the same prefab rebinds it; another prefab replaces only the child. Without a ghost prefab, Emas creates a root with the requested ghost component.

`View.RequestedDetailLevel` records the requested level, even when a lower-detail prefab is selected. `ViewMapping.DetailLevel` describes the level supported by that mapping.

Views require an available ghost and a positive request. View binding finishes before activation. Requests made during source changes/finalization defer refresh, so `Manifest` may return the previous view or null until that phase completes. Requests outside those phases refresh immediately.

Source: [realm](../Runtime/Realm.cs), [queries](../Runtime/Queries/Query.cs), [blueprints](../Runtime/Views/Blueprint.cs).

Integration policy: [Guidelines](Guidelines.md). Authoring errors appear in Blueprint/SceneSetup Inspectors using the same validation as runtime registration. **Window > Emas** passively shows existing default-realm anchors, source labels, health, available/owned counts and failure context with expandable exception details; isolated realms can be inspected through the snapshot APIs.
