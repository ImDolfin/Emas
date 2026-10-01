# Architecture

Emas owns stable `Presence` handles, Ghost roots, availability and optional views. Applications own SDK clients, detector integrations, entity modules, behaviors and prefab assets.

[Architecture diagram](Diagrams/Architecture.html) / [Lifecycle diagram](Diagrams/Lifecycle.html)

`RealmSetup` is an optional Inspector-configured owner of an isolated, automatically updated realm. Each child `AnchorSetup` attaches a colocated `PresenceDetectorComponent`; its private adapter forwards to the same plain `PresenceDetector` used by code and tests. The component implements the provider bridge so application scripts only override `OnStart`, `OnUpdate` and `OnStop`. `Detect` and `Disappear` announce membership; module readers handle data changes.

A colocated `GhostInitializer` maps `Presence.Source` to the modules authored on that Anchor's Ghost prefabs. It takes precedence over the Realm's per-Kind initializer, preventing one SDK's bindings from replacing another Anchor's mapping. Both routes use Population's existing reinitialization and cleanup boundaries. `IDetectorProvider` can still supply constructed detectors, and `IRealmConfigurator` can still register Realm-wide defaults. Code setup keeps constructor injection, `RegisterPresenceInitializer<TGhost>` and `Anchor.AddDetector`.

## Ownership

```text
Realm (direct code setup or one RealmSetup)
  Anchor (one detector in a prefab setup; code may attach more)
    Presence (stable identity, label, capabilities, availability)
      Ghost (invisible root, configured modules and application behaviors)
        View (optional visual child)
```

Identity is `(anchor ID, kind, entity ID)` within one realm. The detector reports identity, optional label, visual variant, capability metadata and an application source object. Presence stores that source weakly and resolves destroyed Unity objects as null. A plain `Ghost` component carries identity and exposes the contracts on its root. Prefabs define their modules as saved components; custom Ghost subclasses are optional for entity-specific behavior and can also use `RequireComponent`. The per-kind initializer binds SDK-to-value readers on those components; it does not create modules. Each realm update reads and applies the enabled module inputs before projection and query notifications. A changed source reference, changed capability set or rediscovered/reassigned Ghost reruns the initializer. Disappearance, handover and removal clear old bindings and release the weak source reference. Each identity belongs to one detector attachment; compatible replacement preserves Ghosts and their modules. Prepared Ghosts remain unowned and unavailable until claimed.

Queries see available Ghost roots only; the corresponding `Presence.IsAvailable` follows the same lifecycle. `realm.Query()` is scoped to one realm; `Query.All()` includes every live realm and follows realms created later. Its filters, scalar results, enumeration and subscriptions use the same available-ghost rules. `realm.Query(globalQuery)` reuses the global query's filters within that realm. Root components provide data contracts; visual children do not participate in interface lookup. A viewless available ghost remains active and runs its root behaviors. Developers can call `Realm.Manifest(presence, detailLevel)` to request its optional view.

Manifestation blueprints are resolved by Kind within a Realm. `RealmSetup` registers the mappings for every anchor in that realm; anchors only group identities, attach detectors and optionally request views automatically. Variants select different appearances within a Kind. Each realm registration holds a snapshot of the blueprint and its inline named variant rows. Asset edits take effect in that realm after re-registration, which refreshes requested views across all anchors on the next update while keeping existing roots. Other realms retain their own mappings. Re-registering after a kind change releases the old kind in the realm and refreshes both kinds. Root prefab changes affect newly created ghosts. With no blueprint, Emas creates a viewless root of the registered Ghost type, or a plain `Ghost` when no initializer is registered; empty blueprints also remain silent.

## Identity and deferred commands

Each realm owns an internal `IdentityMap` keyed by `(anchor ID, kind, entity ID)`. It retains one current `Record` per key, so repeated reports update the same Ghost and Presence. Records supply their own keys when added; duplicate keys cannot overwrite existing records. Root-based lookup and removal also check the exact object instance, so a retained handle or cleanup from an old lifetime cannot affect a replacement with the same key. Disappearance grace retains the mapping; final removal releases it before Unity callbacks run. The internal `Population` owns root construction and lifecycle policy, including identity insertion and removal; `Realm` coordinates when those changes are projected, activated and observed.

`CommandQueue<T>` provides the shared FIFO, sequence tracking and reentrancy guard used by both detector dispatch and scene changes. Each subsystem supplies its command data and execution function. The queue has two drain modes: `ExecutePending(maximum)` processes only commands present at batch entry, while `ExecuteAll()` also drains commands enqueued by callbacks before returning. A nested drain returns immediately; the outer drain applies its own batch boundary to queued work. Clearing pending work during execution is safe.

`Realm` directly owns a `CommandQueue<DispatchCommand>` and calls `ExecutePending(256)` during each update. The realm checks each command's detector attachment generation, applies the action inside the source-change boundary and isolates failures to the current detector. Sequence tracking also determines when startup handover work has finished. Commands enqueued by those actions wait for a later update. Realm disposal clears the queue and stops the batch.

`SceneChangeQueue` uses the same `CommandQueue<T>` implementation with `ExecuteAll()`. Scene operations run immediately when safe; operations requested by nested Unity callbacks wait until the current scene operation returns, then drain before the outer request returns. The two subsystems use separate queue instances so scene cleanup can finish during realm disposal without running detector work. Both operate entirely on Unity's main thread, with no application command types or additional public interfaces.

## Update order

1. Process up to **256 queued actions** present at update entry, in FIFO order.
2. Run detector updates to detect arrivals, refresh presence metadata and remove departed identities.
3. Mark timed-out identities as disappeared, remove identities whose disappearance grace or startup handover has ended, and retain other reported identities.
4. Read and apply all enabled Ghost module bindings.
5. Invoke `Ghost.OnUpdate()` once on each enabled, available or pending Ghost.
6. Capture the optional reference frame and let each `Spatial` apply its pose.
7. Publish initialized roots and Presences as available and activate their roots.
8. Refresh requested dirty views within presentation range.
9. Notify query subscribers, delivering observed departures before arrivals for each paired subscription.

Newly queued actions wait for a later update. The budget limits action count, not execution time; application callbacks must remain short. Dispatch records the detector's registration generation, so stale work is discarded even if the same instance is reattached. Detector updates also capture that generation: a detector removed and reattached during an update first ticks in the following update.

Successful startup outside an update finalizes directly reported roots immediately, without invoking Ghost update hooks. Hooks run only in the realm update phase; view requests cannot invoke them again. They may run before initial activation, and their order across Ghosts is unspecified. Reports queued through a captured dispatcher during startup run in a later update. Variant changes and explicit view requests inside detector/finalization callbacks defer refresh until module updates are complete. Explicit requests outside those phases retain immediate behavior.

`Realm.Default` provides an automatically updated default realm. A direct-code isolated realm uses explicit `Update()`; `RealmSetup` updates its own isolated realm each frame. An all-realm query reads current state without advancing any realm. Each realm delivers its own subscription notifications during its update. A global subscription follows new realms and reports departures for its observed matches when a realm is disposed. All Emas calls require Unity's main thread. Applications handle SDK threading before reporting or removing presences. The queue and protected `Dispatch` defer main-thread work to later updates. Custom detectors can capture a dispatcher per attachment so callbacks retained from an old attachment cannot enter a new one. Emas provides no thread synchronization or marshalling.

## Optional spatial projection

Enabled root `Spatial` components opt into shared Cartesian coordinates. A null `Realm.ReferenceFrame` uses identity projection into Unity world space with no distance limit; an assigned frame configures relative placement. `Double3` preserves global positions until the reference displacement has been calculated in doubles. `SpatialManager` captures one shared projection after all module readers and Ghost hooks finish, then each `Spatial` applies its own world pose and range suppression. The manager tracks presentation availability for view refresh; no per-Ghost Unity `Update` is needed. World placement compensates for Anchor parents; ghosts without enabled spatial components retain application positioning.

A manual reference or a followed spatial ghost provides the origin. Following resolves once per spatial phase, so reference movement reprojects entities whose cached position has not changed. Rotation is an independent optional channel. Losing a followed entity retains its last valid reference pose and exposes that loss without jumping to the global origin.

Presentation range is measured in shared Cartesian coordinates before float conversion. Out-of-range entities keep their identities, query availability and root scripts; their views, root rendering and colliders are suppressed. The view request survives and resumes on range entry. This keeps distant presentation out of Unity's large-coordinate range without requiring entity republication. See [spatial integration](Spatial.md).

## Failure and cleanup

| Event | Result |
| --- | --- |
| Detector update, dispatched action, module reader, module Apply or Ghost.OnUpdate throws | Run that attachment's OnStop, then remove its population if it is still current; leave the failed detector attached for explicit recovery; other detectors continue |
| Restart/replace detector | Reuse compatible roots, Presence handles and view requests reported during startup handover; remove identities still unreported when handover completes |
| Failed restart/replacement | Remove the population; retain the failed registration for another explicit retry/replacement/removal |
| Explicit disappearance or inactivity timeout | Mark the Presence unavailable immediately; remove its root and view after `DisappearanceGracePeriod`, which defaults to zero |
| View creation/refresh throws | Clean up that view and log its context; keep the detector and Ghost available, retaining the view request for retry |
| Failed initial attachment | Remove only newly created records; restore prepared identities to unowned/unavailable |
| Failed multi-detector anchor attachment | Remove detectors newly attached by that call in reverse order; restore prepared identities and retain an existing anchor's earlier detectors |
| Attach an already registered detector | Reject without changing its original population |
| Remove presence/detector | Remove the selected identity/owned population and associated views |
| Stop RealmSetup, disable AnchorSetup, dispose/remove anchor or unload its scene | Remove owned and prepared records; stop detectors |
| Dispose realm | Remove anchors, records, views, subscriptions, manifestation blueprints and queued work |

A successful restart or replacement has a bounded startup handover. Existing roots and their Presences are unavailable until reported again; cleanup waits for the first subsequent realm update and for reports queued during startup to run, including any dispatch backlog. It then removes still-unreported roots. Detector failure removes roots immediately, so later recovery creates new instances. Unowned prepared ghosts remain until claimed or explicitly removed with their anchor.

Detectors can set `DisappearanceGracePeriod` before attachment. A disappearance makes the Presence unavailable, deactivates its root and removes it from available queries immediately. A report during grace reuses the same Presence and Ghost root; after the deadline the realm removes them. The default zero removes immediately. `InactivityTimeout` separately detects silent feeds using unscaled time since each report. A timeout follows the same disappearance path. Direct Ghost integrations that update cached roots can call `MarkPublished`; `Detect` records presence activity; module value reads do not refresh inactivity deadlines.

Demanifesting removes only the visual child and keeps the Presence and Ghost root. Failed view requests can retry through `Manifest` or a manifestation blueprint or variant change; unchanged detector reports leave them alone.

Detectors retain the original first exception in `LastError` and its captured anchor/detector/operation in `LastErrorContext`; cleanup errors cannot hide either and old registrations cannot change a restarted detector's status. See [detector contracts](API.md).

Realm/anchor disposal is idempotent. Further mutations throw `ObjectDisposedException`; disposed-realm queries are empty and `Update()` is a no-op. Anchor disposal unregisters records immediately, before Unity's deferred destruction. A disposed realm leaves global query results immediately. Realm-scoped subscriptions end without synthetic departures; global observers receive a departure for each match they had seen in that realm.

## Callback safety and internal boundaries

`Realm` is the public facade and coordinator, implemented in a single file. It delegates entity transitions to `Population` and presentation requests to `ViewManager`, while retaining source failure and mutation boundaries and the ordering of projection, activation, view refresh and notifications. `Population` receives resolved anchor and blueprint inputs and owns its lifecycle operations. Per-Kind initializers supply the existing strategy for application-specific root setup; no additional public interfaces are needed.

Identity map traversal uses snapshots and rechecks membership/registration after callbacks. Removal invalidates identity immediately. `Observe` retains departure keys until notification, so removed Unity objects need not stay alive. All-realm observations keep memberships separate per realm so identical keys do not collapse into one match. `ObserveWithRealm` supplies the owning realm on both entry and departure. Subscription disposal cancels pending notifications. The scene change queue waits for an Emas-triggered Unity activation or destruction call to return before applying scene changes requested by its callbacks, preventing unsafe hierarchy changes during activation callbacks.

| Component | Responsibility |
| --- | --- |
| [Realm](../Runtime/Realm.cs) | Public facade in one non-partial class; coordinate anchors, configuration, detector attachment/failure, source-change boundaries, dispatch and update phases |
| [Population](../Runtime/Tracking/Population.cs) | Create roots and apply per-Kind initializer strategies and module data; own identity mutations, availability, detector ownership, grace/expiry, handover, rollback and removal |
| [IdentityMap](../Runtime/Tracking/IdentityMap.cs) | Keep one current record per key and reject stale object references |
| [CommandQueue&lt;T&gt;](../Runtime/Tracking/CommandQueue.cs) | Share FIFO ordering, sequence tracking and reentrancy-safe bounded or full drains |
| [ViewManager](../Runtime/Views/ViewManager.cs) | Own view requests; stage, bind, refresh and destroy views; contain presentation failures per ghost |
| [Subscriptions](../Runtime/Queries/Subscriptions.cs) | Reconcile matches with reusable sets; notify safely |
| [SceneChangeQueue](../Runtime/Unity/SceneChangeQueue.cs) | Use the shared queue to apply nested GameObject changes after the current scene operation returns |
| [PresenceDetector](../Runtime/Tracking/PresenceDetector.cs) / [Anchor](../Runtime/Tracking/Anchor.cs) | SDK detection, attachment lifecycle and scene ownership |
| [Presence](../Runtime/Entities/Presence.cs) / [EntityModule](../Runtime/Entities/EntityModule.cs) | Stable identity and Ghost-owned value readers and application |

Query interface filters use typed predicates and a reusable root-component list. Subscriptions reuse their match and departure buffers across updates while still scanning current ghosts and rechecking matches after callbacks. Scalar query results scan without building a match list. These are implementation choices, not measured performance guarantees.

Assembly dependencies: editor and tests may reference runtime; runtime never references editor, sample or SDK assemblies. Samples remain separate application assemblies. Package code targets C# 8, enforced by compiler response files.

## Repository layout

| Folder | Contents |
| --- | --- |
| `Runtime/` | `Realm` entry point and package metadata |
| `Runtime/Entities/` | Presence handles, entity modules, Ghost contracts, spatial state and identity/coordinate values |
| `Runtime/Tracking/` | Anchors, detectors, population lifecycle, identity storage and command execution |
| `Runtime/Queries/` | Filtering and subscriptions |
| `Runtime/Views/` | ManifestationBlueprint, inline ManifestationVariant rows and view lifecycle |
| `Runtime/Unity/` | Prefab realm and anchor setup, automatic runner and queued scene changes |
| `Editor/Diagnostics/` | Passive multi-Realm diagnostics and optional Scene view overlay |
| `Editor/Inspectors/` | ManifestationBlueprint variant table, RealmSetup and AnchorSetup authoring validation |
| `Tests/Runtime/` | Tests grouped by the same responsibilities |
| `Samples~/Minimal/` | Quick start with one marker and a position module |
| `Samples~/Example/` | Three cars, reusable modules and SDK replacement |
| `Samples~/RelativeWorld/` | Double-precision spatial placement and a moving reference |

Each top-level type has its own file. Runtime public types share the `Emas` namespace so application imports remain simple.
