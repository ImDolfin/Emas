# Integration and contribution guidelines

## Integrate an SDK

- **Keep SDK types in the mapping.** Detectors report membership. Initializers bind authored `Trait<TData>` components to source-independent values. Consumers use small read-only contracts on the Ghost root.
- **Own the source.** Applications retain SDK clients/proxies; `Presence.Source` is weak. Resolve it inside readers and handle null. Detector cleanup releases subscriptions, not shared SDK clients.
- **Use Unity's main thread.** All Emas calls, including `Dispatch`, require it. Capture a dispatcher per attachment in `OnStart`, retain exact event delegates and unsubscribe in `OnStop`. Copy reused payloads before deferred work.
- **Choose expiry deliberately.** Leave inactivity timeout disabled for change-only feeds. For regularly reported entities, choose a timeout longer than the expected interval. Disappearance grace preserves unavailable roots for recovery; failure/removal bypasses grace.
- **Bind before activation.** Traits must exist on the root prefab or through `RequireComponent`. Readers can run before initial activation; do not rely on `OnEnable` for binding dependencies. A scene `GhostInitializer` takes precedence over Realm registration for its Anchor.
- **Dispose consumers.** Keep query subscription handles and clear retained membership when the consumer stops or its Realm changes. Query results contain available roots regardless of view or range.

See [Getting started](GettingStarted.md) for setup and [API](API.md) for exact lifecycle contracts.

## Own presentation clearly

- Define units and axes in application contracts. Preserve large positions as doubles until projection; see [Spatial](Spatial.md).
- Give each root one transform writer. Enabled Spatial owns its world pose; keep articulation on children and integrate dynamic physics explicitly.
- Configure one reference per Realm. Keep its Unity placement near the origin and set a suitable presentation range. Attachments use same-Realm Keys and Unity-local offsets.
- Treat buffering, smoothing and extrapolation as separate controls. Use original timestamps from one SDK clock. Clear unavailable motion channels rather than repeatedly supplying stale values.
- Re-register edited blueprints. Each Realm keeps a configuration snapshot; root-prefab changes affect new roots. Variants select named appearances, including any application-controlled LODs.

## Diagnose and recover

Use **Window > Emas > Tracking** or the Scene view **Emas** overlay to inspect existing Realms. Read a failed detector's `LastErrorContext` and `LastError`, fix the cause, then restart or replace it explicitly. There is no automatic retry.

A view failure leaves tracking active. Fix its prefab/binding and call `Manifest`, or change the blueprint/Variant. Missing or ambiguous root contracts identify the Key, requested type and matching components; check the Ghost prefab for that Kind.

## Measure update cost

Use the Unity CPU Profiler with Deep Profile off. Emas provides these samples in Editor and development builds:

| Sample | Work included |
| --- | --- |
| `Emas.Update` | One Realm update, including detector callbacks and expiry checks |
| `Emas.Traits` | Trait discovery and bound application readers |
| `Emas.Ghosts` | Application `Ghost.OnUpdate` hooks |
| `Emas.Projection` | Shared clock/reference capture, playback, extrapolation, smoothing, attachments and root placement |
| `Emas.Views` | Root activation, pending view changes and final range suppression |
| `Emas.Queries` | Query subscriptions and their application callbacks |

Measure a development player as well as the Editor. Compare the same entity count, source rate, strategy, view prefabs and subscriptions. Inspect rendering and physics separately from `Emas.Update`. Per-frame pose changes, query filters and hierarchy size all affect cost.

For distant entities, set `Realm.PoseProcessingDistance` to bypass motion processing across all Anchors while retaining reference-relative placement. `ReferenceFrame.MaxDistance` controls visibility separately. Realm Setup groups these distances with automatic view requests under **Presentation**. Views outside the visibility limit retain their roots; range suppression checks their renderers and colliders for changes. Query membership remains independent of range.

Use subscriptions to maintain consumer collections instead of repeatedly enumerating query snapshots. Subscribe once and dispose when the consumer stops. Each active subscription still checks live membership during updates; component filters inspect root components so runtime additions and removals remain visible.

## Contribute

Use C# 8, Allman braces, one top-level type per file and Unity metadata. Keep Runtime independent of Editor, sample and SDK assemblies. Public APIs and protected extension points need concise XML summaries with caller-relevant units, lifecycle and validation constraints; detailed walkthroughs belong in the guides. Visible serialized settings need useful tooltips and clear units.

Protect distinct public contracts with focused Unity tests. Update the changelog and synchronize modified samples with `Tests/Unity~/Assets/Samples`. Store execution reports under ignored `TestResults/`; keep [Validation](Validation.md) about setup and contract coverage. Version changes belong to an explicit release.
