# Changelog

## 0.1.0

- Capture and verify intentional test exceptions in scoped assertions, with concise Test Runner output instead of expected exception stacks in the Console.

- Added `SceneSetup.StopTracking()` and `Anchor.RestartSource(source)` with retained ghost identities, view requests and stale-callback protection.
- Added `Query.Observe(onEnter, onLeave)` for paired arrivals and departures, including removal, lost availability and filter changes.
- Added source labels and `LastErrorContext`; logs lead with operation context and diagnostics show expandable exception details. Expanded samples and lifecycle regression coverage.

- Made all Emas APIs main-thread-only and removed internal thread synchronization; applications now own SDK event handoff. Deferred callbacks, ordering and lifecycle protection remain.

- Added read-only source health (`IsAttached`, `IsActive`, `LastError`) and copied anchor/source snapshots, with generation-safe failure reporting.
- Added passive diagnostics and Blueprint/SceneSetup Inspectors sharing runtime validation; optional ghost prefabs no longer produce warnings.
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
