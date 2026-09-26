# Architecture

Emas owns identity, availability and optional views. Applications own source integrations, data contracts, behaviors and prefab assets.

[Architecture diagram](Diagrams/Architecture.html) / [Lifecycle diagram](Diagrams/Lifecycle.html)

`RealmSetup` is an optional Inspector-configured owner of one isolated, automatically updated realm. Child `AnchorSetup` components each configure one anchor and create one source through their colocated `ISourceProvider`. `PollingPresenceSource` adapts complete source snapshots; `CallbackPresenceSource` queues individual publications and removals with subscription cleanup. All use the same tracking and lifecycle path described below.

## Ownership

```text
Realm (direct code setup or one RealmSetup)
  Anchor (one source in a prefab setup; code may attach more)
    Ghost (identity, application data, root behaviors)
      View (optional visual child)
```

Identity is `(anchor ID, kind, entity ID)` within one realm; separate realms may use the same key. Display names are labels. Each identity belongs to one source; compatible replacement reuses roots republished during startup handover. Prepared ghosts remain unowned and unavailable until claimed.

Queries see available ghosts only. `realm.Query()` is scoped to one realm; `Query.All()` includes every live realm and follows realms created later. Its filters, scalar results, enumeration and subscriptions use the same available-ghost rules. `realm.Query(globalQuery)` reuses the global query's filters within that realm. Root components provide data contracts; visual children do not participate in interface lookup. A viewless available ghost remains active and runs its root behaviors.

Manifestation blueprints are resolved by anchor and kind: an anchor registration takes precedence over the realm-wide default. `AnchorSetup` installs its Inspector manifestation blueprints on its own anchor, so anchors sharing a kind can use different views. Each realm or anchor registration holds a snapshot of the blueprint and its referenced variant assets. Asset edits do not alter that scope until re-registration, which refreshes requested views on the next update while keeping existing roots. Re-registering after a kind change releases the old kind in that scope and refreshes both kinds. Removing an anchor override restores the realm default. Root prefab changes affect newly created ghosts. With no blueprint, Emas creates a plain Ghost root and no view; empty blueprints also remain silent.

## Update order

1. Process up to **256 queued actions** present at update entry, in FIFO order.
2. Run source updates and finish assigning data.
3. Remove ghosts past their configured inactivity timeout and unreported ghosts whose startup handover has completed.
4. Resolve the optional realm reference frame and project spatial ghosts, updating presentation range.
5. Publish initialized ghosts as available and activate their roots.
6. Refresh requested dirty views within presentation range.
7. Notify query subscribers, delivering observed departures before arrivals for each paired subscription.

Newly queued actions wait for a later update. The budget limits action count, not execution time; application callbacks must remain short. Dispatch records the source's registration generation, so stale work is discarded even if the same instance is reattached. Source updates also capture that generation: a source removed and reattached during an update first ticks in the following update.

Successful startup outside an update finalizes directly populated ghosts immediately. Callback-source startup queues its initial publications for a later update. Variant changes and explicit view requests inside source/finalization callbacks defer refresh until source data is complete. Explicit requests outside those phases retain immediate behavior.

`Realm.Default` provides an automatically updated default realm. A direct-code isolated realm uses explicit `Update()`; `RealmSetup` updates its own isolated realm each frame. An all-realm query reads current state without advancing any realm. Each realm delivers its own subscription notifications during its update. A global subscription follows new realms and reports departures for its observed matches when a realm is disposed. All Emas calls require Unity's main thread. Applications handle SDK threading before publishing or removing entities. The queue and protected `Dispatch` defer main-thread work to later updates. Custom sources can capture a dispatcher per attachment so callbacks retained from an old attachment cannot enter a new one. Emas provides no thread synchronization or marshalling.

## Optional spatial projection

`Realm.ReferenceFrame` and root `Spatial` components opt into shared simulation coordinates. `Double3` preserves global positions until the reference displacement has been calculated in doubles. The spatial phase maps the result to a configured Unity world pose, compensating for Anchor parents; ghosts without enabled spatial components retain application positioning.

A manual reference or a followed spatial ghost provides the origin. Following resolves once per spatial phase, so reference movement reprojects entities whose cached position has not changed. Rotation is an independent optional channel. Losing a followed entity retains its last valid reference pose and exposes that loss without jumping to the global origin.

Presentation range is measured in simulation coordinates before float conversion. Out-of-range entities keep their identities, query availability and root scripts; their views, root rendering and colliders are suppressed. The view request survives and resumes on range entry. This keeps distant presentation out of Unity's large-coordinate range without requiring entity republication. See [spatial integration](Spatial.md).

## Failure and cleanup

| Event | Result |
| --- | --- |
| Source update/dispatched action throws | Run that attachment's OnStop, then remove its population if it is still current; leave the failed source attached for explicit recovery; other sources continue |
| Restart/replace source | Reuse compatible roots and view requests republished during startup handover; remove identities still unreported when handover completes |
| Failed restart/replacement | Remove the population; retain the failed registration for another explicit retry/replacement/removal |
| Inactivity timeout reached | Remove that ghost and its view; keep the source running |
| View creation/refresh throws | Clean up that view and log its context; keep the source and ghost available, retaining the view request for retry |
| Failed initial attachment | Remove only newly created records; restore prepared identities to unowned/unavailable |
| Failed multi-source anchor attachment | Remove sources newly attached by that call in reverse order; restore prepared identities and retain an existing anchor's earlier sources |
| Attach an already registered source | Reject without changing its original population |
| Remove ghost/source | Remove the selected identity/owned population and associated views |
| Stop RealmSetup, disable AnchorSetup, dispose/remove anchor or unload its scene | Remove owned and prepared records; stop sources |
| Dispose realm | Remove anchors, records, views, subscriptions, manifestation blueprints and queued work |

A successful restart or replacement has a bounded startup handover. Existing roots are unavailable until republished; cleanup waits for the first subsequent realm update and for publications queued during startup to run, including any dispatch backlog. It then removes still-unreported roots. Source failure removes roots immediately, so later recovery creates new instances. Unowned prepared ghosts remain until claimed or explicitly removed with their anchor.

Sources can opt into per-entity expiry with `InactivityTimeout`. Each publication records unscaled activity time; any partial data update counts. Custom sources updating cached ghosts call `MarkPublished`. Expiry removes the identity and view before subscription notifications; later publication creates a fresh root.

Demanifesting removes only the visual child. Failed view requests can retry through `Manifest` or a manifestation blueprint, variant or detail change; unchanged source updates leave them alone.

Sources retain the original first exception in `LastError` and its captured anchor/source/operation in `LastErrorContext`; cleanup errors cannot hide either and old registrations cannot change a restarted source's status. See [status contracts](API.md#presencesource-and-ghost-contracts).

Realm/anchor disposal is idempotent. Further mutations throw `ObjectDisposedException`; disposed-realm queries are empty and `Update()` is a no-op. Anchor disposal unregisters records immediately, before Unity's deferred destruction. A disposed realm leaves global query results immediately. Realm-scoped subscriptions end without synthetic departures; global observers receive a departure for each match they had seen in that realm.

## Callback safety and internal boundaries

Registry traversal uses snapshots and rechecks membership/registration after callbacks. Removal invalidates identity immediately. `Observe` retains departure keys until notification, so removed Unity objects need not stay alive. All-realm observations keep memberships separate per realm so identical keys do not collapse into one match. `ObserveWithRealm` supplies the owning realm on both entry and departure. Subscription disposal cancels pending notifications. The scene change queue waits for an Emas-triggered Unity activation or destruction call to return before applying scene changes requested by its callbacks, preventing unsafe hierarchy changes during activation callbacks.

| Component | Responsibility |
| --- | --- |
| [Realm](../Runtime/Realm.cs) | Orchestrate anchors, configuration and update phases |
| [Registry](../Runtime/Tracking/Registry.cs) | Store identity, ownership and pending state |
| [ViewManager](../Runtime/Views/ViewManager.cs) | Stage, bind, refresh and destroy views; contain presentation failures per ghost |
| [Subscriptions](../Runtime/Queries/Subscriptions.cs) | Reconcile matches with reusable sets; notify safely |
| [SceneChangeQueue](../Runtime/Unity/SceneChangeQueue.cs) | Apply GameObject activation and destruction requested during Unity callbacks after the current scene change returns |
| [PresenceSource](../Runtime/Tracking/PresenceSource.cs) / [Anchor](../Runtime/Tracking/Anchor.cs) | Source lifecycle, registration and scene ownership |

Query interface filters use typed predicates and a reusable root-component list. Subscriptions reuse their match and departure buffers across updates while still scanning current ghosts and rechecking matches after callbacks. Scalar query results scan without building a match list; polling reuses its owned-ghost buffer. These are implementation choices, not measured performance guarantees.

Assembly dependencies: editor and tests may reference runtime; runtime never references editor, sample or SDK assemblies. The sample remains a separate application assembly. Package code targets C# 8, enforced by compiler response files.

## Source layout

| Folder | Contents |
| --- | --- |
| `Runtime/` | `Realm` entry point and package metadata |
| `Runtime/Entities/` | Ghost contracts, spatial state and identity/coordinate values |
| `Runtime/Tracking/` | Anchors, sources and ownership storage |
| `Runtime/Queries/` | Filtering and subscriptions |
| `Runtime/Views/` | ManifestationBlueprint, ManifestationVariant, detail level and view lifecycle |
| `Runtime/Unity/` | Prefab realm and anchor setup, automatic runner and queued scene changes |
| `Editor/Diagnostics/` | Passive default-realm diagnostics |
| `Editor/Inspectors/` | ManifestationBlueprint, ManifestationVariant, RealmSetup and AnchorSetup authoring validation |
| `Tests/Runtime/` | Tests grouped by the same responsibilities |
| `Samples~/Minimal/` / `Samples~/Callbacks/` | Polling and callback quick starts |
| `Samples~/Example/` | Contracts, entities, behaviors and source integrations |
| `Samples~/RelativeWorld/` | Double-precision spatial placement and a moving reference |

Each top-level type has its own file. Runtime public types share the `Emas` namespace so application imports remain simple.
