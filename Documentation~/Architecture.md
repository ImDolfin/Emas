# Architecture

Emas owns identity, availability and optional views. Applications own source integrations, data contracts, behaviors and prefab assets.

[Architecture diagram](Diagrams/Architecture.html) / [Lifecycle diagram](Diagrams/Lifecycle.html)

## Ownership

```text
Context
  Origin (scene frame; one or more coordinators)
    Ghost (identity, application data, root behaviors)
      View (optional visual child)
```

Identity is `(origin ID, kind, entity ID)`. Display names are labels. Each identity belongs to one coordinator; compatible replacement transfers ownership without replacing its root. Prepared ghosts remain unowned and unavailable until claimed.

Queries see available ghosts only. Root components provide data contracts; visual children do not participate in interface lookup. A viewless available ghost remains active and runs its root behaviors.

## Update order

1. Process up to **256 queued actions** present at update entry, in FIFO order.
2. Run coordinator source updates and finish assigning data.
3. Publish initialized ghosts as available and activate their roots.
4. Refresh requested dirty views.
5. Notify availability subscriptions.

Newly queued actions wait for a later update. The budget limits action count, not execution time; application callbacks must remain short. Dispatch records the coordinator's registration generation, so stale work is discarded even if the same instance is reattached.

Successful startup outside an update finalizes its population immediately. Variant changes and explicit view requests inside source/finalization callbacks defer refresh until source data is complete. Explicit requests outside those phases retain immediate behavior.

`Context.Default` provides an automatically updated default context. An isolated context uses explicit `Update()` instead. Unity object operations belong on the main thread; SDK callbacks use coordinator `Dispatch`.

## Failure and cleanup

| Event | Result |
| --- | --- |
| Source update/dispatched action throws | Stop that coordinator and deactivate its population; other coordinators continue |
| Replace coordinator | Preserve identities and root components; ghosts remain unavailable until republished |
| Failed replacement | Retain unavailable records and failed registration for another explicit replacement/removal |
| Failed initial attachment | Remove only newly created records; restore prepared identities to unowned/unavailable |
| Attach an already registered coordinator | Reject without changing its original population |
| Remove ghost/coordinator | Remove the selected identity/owned population and associated views |
| Dispose/remove origin or unload its scene | Remove owned and prepared records; stop coordinators |
| Dispose context | Remove origins, records, views, subscriptions, blueprints and queued work |

Availability loss deactivates the root and excludes it from queries; retained data may be stale. Demanifesting only removes the visual child. Recovery is explicit through an active coordinator republishing identities.

Context/origin disposal is idempotent. Further mutations throw `ObjectDisposedException`; disposed-context queries are empty and `Update()` is a no-op. Origin disposal unregisters records immediately, before Unity's deferred destruction.

## Callback safety and internal boundaries

Registry traversal uses snapshots and rechecks membership/registration after callbacks. Removal invalidates identity immediately. Nested scene activation/destruction waits until the outer Emas-triggered Unity scene effect returns, preventing unsafe hierarchy changes during activation callbacks.

| Component | Responsibility |
| --- | --- |
| [Context](../Runtime/Context.cs) | Orchestrate origins, configuration and update phases |
| [Registry](../Runtime/Tracking/Registry.cs) | Store identity, ownership and pending state |
| [ViewManager](../Runtime/Views/ViewManager.cs) | Stage, bind, refresh and destroy views |
| [Subscriptions](../Runtime/Queries/Subscriptions.cs) | Reconcile matches with reusable sets; notify safely |
| [SceneEffects](../Runtime/Unity/SceneEffects.cs) | Serialize nested scene effects |
| [Coordinator](../Runtime/Tracking/Coordinator.cs) / [Origin](../Runtime/Tracking/Origin.cs) | Source lifecycle, registration and scene ownership |

Query interface filters use typed predicates; subscription reconciliation avoids repeated per-key scans. These are implementation choices, not measured performance guarantees.

Assembly dependencies: editor and tests may reference runtime; runtime never references editor, sample or SDK assemblies. The sample remains a separate application assembly. Package code targets C# 8, enforced by compiler response files.

## Source layout

| Folder | Contents |
| --- | --- |
| `Runtime/` | `Context` entry point and package metadata |
| `Runtime/Entities/` | Ghost contract, component and identity values |
| `Runtime/Tracking/` | Origins, coordinators and ownership storage |
| `Runtime/Queries/` | Filtering and subscriptions |
| `Runtime/Views/` | Blueprint, detail level and view lifecycle |
| `Runtime/Unity/` | Automatic runner and nested scene effects |
| `Editor/Diagnostics/` | Emas diagnostics window |
| `Tests/Runtime/` | Tests grouped by the same responsibilities |
| `Samples~/Example/` | Contracts, entities, behaviors and source integrations |

Each top-level type has its own file. Runtime public types share the `Emas` namespace so application imports remain simple.
