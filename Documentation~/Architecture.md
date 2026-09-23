# Architecture

Emas owns identity, availability and optional views. Applications own source integrations, data contracts, behaviors and prefab assets.

[Architecture diagram](Diagrams/Architecture.html) / [Lifecycle diagram](Diagrams/Lifecycle.html)

`SceneSetup` is an optional Inspector-configured owner around the default realm. `PollingPresenceSource` adapts complete source snapshots; `CallbackPresenceSource` queues individual publications and removals with subscription cleanup. All use the same tracking and lifecycle path described below.

## Ownership

```text
Realm
  Anchor (scene frame; one or more sources)
    Ghost (identity, application data, root behaviors)
      View (optional visual child)
```

Identity is `(anchor ID, kind, entity ID)`. Display names are labels. Each identity belongs to one source; compatible replacement transfers ownership without replacing its root. Prepared ghosts remain unowned and unavailable until claimed.

Queries see available ghosts only. Root components provide data contracts; visual children do not participate in interface lookup. A viewless available ghost remains active and runs its root behaviors.

Blueprints are resolved by anchor and kind: an anchor registration takes precedence over the realm-wide default. `SceneSetup` installs its Inspector blueprints on its owned anchor, so setups sharing a kind do not replace each other's configuration. Each realm or anchor registration holds a snapshot of its blueprint settings. Asset edits do not alter that scope until re-registration, which refreshes requested views on the next update while keeping existing roots. Re-registering after a kind change releases the old kind in that scope and refreshes both kinds. Removing an anchor override restores the realm default. Root prefab changes affect newly created ghosts.

## Update order

1. Process up to **256 queued actions** present at update entry, in FIFO order.
2. Run source updates and finish assigning data.
3. Publish initialized ghosts as available and activate their roots.
4. Refresh requested dirty views.
5. Notify query subscribers, delivering observed departures before arrivals for each paired subscription.

Newly queued actions wait for a later update. The budget limits action count, not execution time; application callbacks must remain short. Dispatch records the source's registration generation, so stale work is discarded even if the same instance is reattached. Source updates also capture that generation: a source removed and reattached during an update first ticks in the following update.

Successful startup outside an update finalizes directly populated ghosts immediately. Callback-source startup queues its initial publications for a later update. Variant changes and explicit view requests inside source/finalization callbacks defer refresh until source data is complete. Explicit requests outside those phases retain immediate behavior.

`Realm.Default` provides an automatically updated default realm. An isolated realm uses explicit `Update()` instead. All Emas calls require Unity's main thread. Applications handle SDK threading before publishing or removing entities. The queue and protected `Dispatch` defer main-thread work to later updates. Custom sources can capture a dispatcher per attachment so callbacks retained from an old attachment cannot enter a new one. Emas provides no thread synchronization or marshalling.

## Failure and cleanup

| Event | Result |
| --- | --- |
| Source update/dispatched action throws | Run that attachment's OnStop, then deactivate its population if it is still current; other sources continue |
| Restart/replace source | Preserve identities, root components and view requests; ghosts remain unavailable until republished |
| Failed restart/replacement | Retain unavailable records and failed registration for another explicit retry/replacement/removal |
| Failed initial attachment | Remove only newly created records; restore prepared identities to unowned/unavailable |
| Failed multi-source anchor attachment | Remove sources newly attached by that call in reverse order; restore prepared identities and retain an existing anchor's earlier sources |
| Attach an already registered source | Reject without changing its original population |
| Remove ghost/source | Remove the selected identity/owned population and associated views |
| Stop SceneSetup, dispose/remove anchor or unload its scene | Remove owned and prepared records; stop sources |
| Dispose realm | Remove anchors, records, views, subscriptions, blueprints and queued work |

Availability loss deactivates the root and excludes it from queries; retained data may be stale. Demanifesting only removes the visual child. Recovery is explicit through an active source republishing identities.

Sources retain the original first exception in `LastError` and its captured anchor/source/operation in `LastErrorContext`; cleanup errors cannot hide either and old registrations cannot change a restarted source's status. See [status contracts](API.md#presencesource-and-ghost-contracts).

Realm/anchor disposal is idempotent. Further mutations throw `ObjectDisposedException`; disposed-realm queries are empty and `Update()` is a no-op. Anchor disposal unregisters records immediately, before Unity's deferred destruction.

## Callback safety and internal boundaries

Registry traversal uses snapshots and rechecks membership/registration after callbacks. Removal invalidates identity immediately. `Observe` retains departure keys until notification, so removed Unity objects need not stay alive. Subscription or realm disposal cancels pending notifications. Nested scene activation/destruction waits until the outer Emas-triggered Unity scene effect returns, preventing unsafe hierarchy changes during activation callbacks.

| Component | Responsibility |
| --- | --- |
| [Realm](../Runtime/Realm.cs) | Orchestrate anchors, configuration and update phases |
| [Registry](../Runtime/Tracking/Registry.cs) | Store identity, ownership and pending state |
| [ViewManager](../Runtime/Views/ViewManager.cs) | Stage, bind, refresh and destroy views |
| [Subscriptions](../Runtime/Queries/Subscriptions.cs) | Reconcile matches with reusable sets; notify safely |
| [SceneEffects](../Runtime/Unity/SceneEffects.cs) | Serialize nested scene effects |
| [PresenceSource](../Runtime/Tracking/PresenceSource.cs) / [Anchor](../Runtime/Tracking/Anchor.cs) | Source lifecycle, registration and scene ownership |

Query interface filters use typed predicates and a reusable root-component list. Subscriptions reuse their match and departure buffers across updates while still scanning current ghosts and rechecking matches after callbacks. Scalar query results scan without building a match list; polling reuses its owned-ghost buffer. These are implementation choices, not measured performance guarantees.

Assembly dependencies: editor and tests may reference runtime; runtime never references editor, sample or SDK assemblies. The sample remains a separate application assembly. Package code targets C# 8, enforced by compiler response files.

## Source layout

| Folder | Contents |
| --- | --- |
| `Runtime/` | `Realm` entry point and package metadata |
| `Runtime/Entities/` | Ghost contract, component and identity values |
| `Runtime/Tracking/` | Anchors, sources and ownership storage |
| `Runtime/Queries/` | Filtering and subscriptions |
| `Runtime/Views/` | Blueprint, detail level and view lifecycle |
| `Runtime/Unity/` | Scene setup, automatic runner and nested scene effects |
| `Editor/Diagnostics/` | Passive default-realm diagnostics |
| `Editor/Inspectors/` | Blueprint and SceneSetup authoring validation |
| `Tests/Runtime/` | Tests grouped by the same responsibilities |
| `Samples~/Minimal/` / `Samples~/Callbacks/` | Polling and callback quick starts |
| `Samples~/Example/` | Contracts, entities, behaviors and source integrations |

Each top-level type has its own file. Runtime public types share the `Emas` namespace so application imports remain simple.
