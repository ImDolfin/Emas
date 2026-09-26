# Changelog

## 0.1.0

- Added optional realm reference frames, double-precision simulation coordinates and independent root spatial channels; moving origins reproject entities without republication, preserve the last reference on loss and suppress distant presentation while retaining tracking.
- Replaced `SceneSetup` with prefab-configured `RealmSetup` and `AnchorSetup`: each setup owns an isolated, automatically updated realm with any number of one-source anchors, realm blueprint defaults, anchor overrides and an optional reference frame. Direct realm setup remains unchanged.
- Split the runnable relative-world example into its own importable sample with a scene and assembly; it shows a fixed ego car, inverse traffic movement and automatic view restoration on range entry.

- Isolated view creation/refresh failures to the affected view, preserving tracking and view requests for explicit or appearance-driven retry.
- Removed source populations on failure and unreported identities after restart/replacement startup handover; compatible republished roots still survive successful handovers.
- Added optional per-entity `PresenceSource.InactivityTimeout` with unscaled timing and protected `MarkPublished(ghost)` for custom sources updating cached ghosts; expired roots and views are removed automatically.

- Captured blueprint settings per realm or anchor registration so live asset edits cannot change another scope's views; None now resolves to no prefab, and Inspector validation rejects whitespace-only variant IDs.
- Made source failure cleanup finish before ghost deactivation can reattach the source; added `PresenceSource.CaptureDispatcher()` for registration-safe custom callbacks and clarified direct publication timing.
- Added `Anchor.UnregisterBlueprint(kind)` to restore realm defaults; re-registering a blueprint after changing its kind clears its old registration and refreshes requested views for both kinds.
- Deferred updates for sources removed and reattached during the same update until the following update.
- Made ghost contract errors identify the ghost and duplicate root components without repeated logs; added a `GetRequired<T>()` extension for required `IGhost` contracts and a Ghost Inspector with read-only live state.
- Rolled back new sources when a multi-source `GetOrCreateAnchor` call fails on an existing anchor, preserving earlier sources and prepared identities.
- Avoided temporary result lists for scalar queries and reused polling ownership buffers.
- Reused subscription match/departure buffers and ghost root-component lookup lists to reduce allocations during query notifications.
- Scoped prefab-configured blueprints to their anchors, added anchor blueprint overrides, and refreshed requested views when a blueprint is registered again while preserving ghost roots.

- Removed overlapping tests and consolidated related value checks; retained distinct lifecycle, failure and sample regressions.

- Added `Realm.TryGetGhost(key, out ghost)` for exact identity lookup, including retained unavailable ghosts.
- Added optional `PollingPresenceSource.PollEvery(TimeSpan)` with immediate startup, unscaled main-thread timing, fresh restart deadlines and no catch-up bursts.

- Capture and verify intentional test exceptions in scoped assertions, with concise Test Runner output instead of expected exception stacks in the Console.

- Added explicit prefab realm teardown and `Anchor.RestartSource(source)` with retained ghost identities, view requests and stale-callback protection.
- Added `Query.Observe(onEnter, onLeave)` for paired arrivals and departures, including removal, lost availability and filter changes.
- Added source labels and `LastErrorContext`; logs lead with operation context and diagnostics show expandable exception details. Expanded samples and lifecycle regression coverage.

- Made all Emas APIs main-thread-only and removed internal thread synchronization; applications now own SDK event handoff. Deferred callbacks, ordering and lifecycle protection remain.

- Added read-only source health (`IsAttached`, `IsActive`, `LastError`) and copied anchor/source snapshots, with generation-safe failure reporting.
- Added passive diagnostics and Blueprint, RealmSetup and AnchorSetup Inspectors sharing runtime validation; optional ghost prefabs no longer produce warnings.
- Made example contracts read-only; expanded source replacement and cleanup demonstrations, integration guidance and Unity regression coverage.
- Validated Unity 2022.3.62f3 and Unity 6.3 LTS (6000.3.24f1), including Windows Mono players and fresh sample imports.
- Declared the MIT license and removed empty author and invalid URL metadata; installation remains passive.

- Added a preconfigured Unity test project with imported samples and native Test Runner checks; clarified opening it through Unity Hub and testing packages in existing projects.
- Added `CallbackPresenceSource` for event-driven integration, registration-safe dispatch and unsubscribe cleanup, with a runnable Callback quick start sample.
- Clarified detail-level, prefab selection and anchor reuse APIs; added Inspector guidance and specific configuration errors.

- Added the `Realm` entry point and `Anchor.Realm` ownership property.

- Simplified polling construction with named read, identity, data and appearance steps.

- Added complete-snapshot polling, Inspector-configured scene setup and a minimal Quick start sample.

- Added Emas entity tracking with anchors, presence sources, typed identities, queries and optional views.
- Added `Realm.Default` for automatic Unity updates and isolated realms for explicit updates.
- Added callback-safe lifecycle handling, source replacement, scene cleanup and bounded dispatch.
- Organized runtime, editor, tests and sample code by responsibility under Emas namespaces and assemblies.
- Added an importable sample, API and architecture references, and validated lifecycle coverage.
