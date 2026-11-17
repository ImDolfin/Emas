# Architecture

Emas separates SDK membership, root state and presentation. All operations run on Unity's main thread. Applications own SDK clients, integrations and prefab assets; a Realm owns the tracked Unity objects and their lifecycle.

[Architecture diagram](Diagrams/Architecture.html) · [Lifecycle diagram](Diagrams/Lifecycle.html)

## Ownership

```text
Realm
  ReferenceFrame                 optional shared projection
  Anchor                         detector attachments and scene parent
    Presence                     stable handle for one identity
      Ghost                      root components, traits and behavior
        View                     optional visual child
```

A `Key` is `(anchor ID, kind, entity ID)` within one Realm. The same Key in another Realm is a different entity. A Presence and its Ghost survive disappearance grace and compatible detector handover; final removal ends that lifetime. Prepared Ghosts have no Presence until first detection.

Each root has one Ghost marker and its authored components. Initializers bind existing traits to SDK readers. `Presence.Source` holds the application source weakly; resolve it inside readers and handle a missing or destroyed source. Source/capability changes, rediscovery and handover reinitialize bindings. Label and Variant changes do not.

A `ManifestationBlueprint` maps one Kind to a root prefab, named view variants and an optional fallback. Registration snapshots its settings per Realm. Re-registration refreshes requested views while existing roots retain their components. Missing blueprints still permit viewless tracking.

## Runtime boundaries

| Area | Owner | Responsibility |
| --- | --- | --- |
| Coordination | [Realm](../Runtime/Realm.cs) | Public facade, update order, mutation/failure boundaries and dispatch. |
| Detection | [Anchor](../Runtime/Tracking/Anchor.cs), [PresenceDetector](../Runtime/Tracking/PresenceDetector.cs) | Attach, start, update, stop and replace application detectors. |
| Entity lifetime | [Population](../Runtime/Tracking/Population.cs) | Root creation, initializers, trait reads, availability, expiry, handover and cleanup. |
| Identity | [IdentityMap](../Runtime/Tracking/IdentityMap.cs), [Record](../Runtime/Tracking/Record.cs) | One current record per Key; reject stale object lifetimes. |
| Queries | [Query](../Runtime/Queries/Query.cs), [Subscriptions](../Runtime/Queries/Subscriptions.cs) | Immutable selection descriptions and membership notifications. |
| Views | [ViewManager](../Runtime/Views/ViewManager.cs) | Persist requests, resolve appearances, bind views and contain presentation failures. |
| Projection | [SpatialManager](../Runtime/Unity/SpatialManager.cs), [ReferenceFrame](../Runtime/Tracking/ReferenceFrame.cs) | One shared presentation time/reference and parent-first attachment projection. |
| Pose input | [Spatial](../Runtime/Entities/Spatial.cs), [PoseData](../Runtime/Unity/PoseData.cs), [PoseHistory](../Runtime/Unity/PoseHistory.cs) | Validate and retain independent raw channels, ordering and playback history. |
| Pose presentation | [PosePresenter](../Runtime/Unity/PosePresenter.cs) | Compose buffered playback, bounded prediction, packet correction, filtering and live-setting transitions before projection. |
| Motion presentation | [Extrapolation](../Runtime/Entities/Extrapolation.cs), [Smoothing](../Runtime/Entities/Smoothing.cs) | Bound prediction and independently filter position/rotation error. Strategies provide gap algorithms; state belongs to each root. |
| Presentation state | [PacketSmoother](../Runtime/Unity/PacketSmoother.cs), [PresentationTuning](../Runtime/Unity/PresentationTuning.cs), [PresentationSuppression](../Runtime/Unity/PresentationSuppression.cs) | Packet correction, live-setting transitions and owned renderer/collider suppression. |
| Unity setup | [RealmSetup](../Runtime/Unity/RealmSetup.cs), [AnchorSetup](../Runtime/Unity/AnchorSetup.cs) | Own scene-configured Realms and attach their detectors/initializers. |

Runtime has no Editor, sample or SDK assembly dependency. Editor tooling references Runtime. Samples are application assemblies. Public runtime types share the `Emas` namespace; editor APIs use `Emas.Editor`. Package code targets C# 8.

## Update order

Each `Realm.Update()` performs these phases:

1. Execute at most 256 queued actions present at update entry.
2. Tick current detector attachments.
3. Complete handovers and expire silent/disappeared identities.
4. Read and apply all enabled, bound traits.
5. Invoke enabled `Ghost.OnUpdate()` hooks.
6. Align the SDK clock, evaluate presentation and project roots against one reference.
7. Publish availability and activate initialized roots.
8. Refresh requested views and presentation suppression.
9. Notify query observers, delivering departures before arrivals for each paired subscription.

Trait reads complete across all roots before Ghost hooks. Hooks may run before initial activation; their order across roots is unspecified. Prepared and disappeared roots do not tick. Successful startup can finalize directly reported entities before the next update, but does not invoke Ghost hooks.

Dispatch defers main-thread work; it does not transfer SDK callbacks between threads. Actions queued during a batch wait for a later update. A captured dispatcher belongs to one detector attachment and rejects stale callbacks after restart. The count budget does not limit callback execution time.

`Realm.Default` updates automatically. A directly constructed Realm requires explicit updates and disposal. Each `RealmSetup` owns a separate automatically updated Realm, starts after providers enable, and disposes on disable. Nested setups own their nearest Anchors. A colocated `GhostInitializer` overrides Realm-wide bindings for its Anchor.

## Spatial presentation

Raw observations, processed presentation and Unity transforms are separate state. `Double3` preserves position precision until reference displacement is computed. `SpatialClock` aligns one SDK timeline per Realm; an interpolation delay buffers timed channels. Untimed channels remain immediate. Extrapolation fills bounded gaps, smoothing filters correction error, and the processed followed reference is shared with peers and attachments.

Cartesian inputs share application units and axes. Geographic inputs use WGS84/ECEF metres and a local tangent basis at the reference. Rotation setters state their own basis and units. Attachments keep independent root lifetimes and Anchor parents while inheriting a same-Realm parent's processed pose. Missing parents and cycles suppress dependent presentation. See [Spatial](Spatial.md) for input contracts, strategies and tuning.

Range suppression affects views, renderers and colliders; tracking and queries continue. Disabling Spatial releases transform control. Views requested during update notifications use that update's shared projection. Explicit requests outside those notifications can project current input immediately.

## Failure and cleanup

| Event | Result |
| --- | --- |
| Detector, dispatch, trait or Ghost-hook exception | Stop that attachment and remove its population; other detectors continue. Read `LastError` and `LastErrorContext`, then explicitly restart or replace. |
| Successful restart/replacement | Reuse compatible roots and handles reported during startup handover; remove unreported identities after queued startup work and the next update complete. |
| Failed restart/replacement | Remove the population; leave the failed detector attached for retry or removal. |
| Failed initial attachment | Roll back newly created records and restore prepared identities to unowned/unavailable. Multi-detector attachment rolls back newly attached detectors in reverse order. |
| Disappearance or inactivity timeout | Make the entity unavailable immediately. Positive grace retains an inactive root for recovery; zero removes it immediately. Trait reads do not refresh activity. |
| View creation/refresh failure | Remove that view, log context and retain tracking. Retry with `Manifest` or a changed blueprint/Variant. |
| Demanifest | Cancel the view request and remove only the visual child. |
| Anchor/Realm removal or scene unload | Stop owned detectors and remove owned objects. Realm disposal also clears subscriptions, mappings and queued work. |

Disposal is idempotent. Disposed-Realm reads are empty and `Update()` does nothing; further configuration/subscription mutations throw. Scoped subscriptions end without synthetic departures, while global observers receive departures for matches from a disposed Realm. Consumers dispose handles and clear their retained state.

## Callback safety and internal boundaries

Application callbacks can remove, restart or create objects. Traversal snapshots membership and rechecks the current record/attachment after callbacks. Identity removal happens before Unity destruction callbacks, so cleanup from an old lifetime cannot remove a replacement with the same Key.

`CommandQueue<T>` provides FIFO sequence tracking and a reentrancy guard. Detector dispatch drains a bounded entry batch. `SceneChangeQueue` uses a separate queue that drains nested activation/destruction changes after the current scene operation returns. This keeps scene cleanup independent of pending detector work.

Inspector edits use Unity serialization for Undo and prefab overrides. Delayed menu callbacks retain target objects and property paths, then open their own serialized state; they cannot retain per-draw state that has already been disposed. Diagnostics observe existing Realms without advancing or creating them.

## Repository layout

`Runtime/Entities`, `Tracking`, `Queries`, `Views` and `Unity` group the responsibilities above. `Editor/` contains inspectors and diagnostics. Package tests mirror runtime responsibilities; `Tests/Unity~` is the runnable validation project. `Samples~/` contains four independent examples. See [Validation](Validation.md) for coverage and execution.
