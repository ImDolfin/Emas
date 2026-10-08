# Changelog

## 0.1.0

- Replace velocity direction rejection with motion-assisted position smoothing that accepts corrections in every direction. Remove `MinimumForwardSpeed`. Add optional Cartesian/ECEF acceleration, explicit ENU velocity and acceleration setters, and `GeoPosition.ToEarthCenteredVector` for tangent-vector conversion. Cap prediction at one position smoothing time after the last changed position. Update Relative World's separate optional motion traits to accept ENU observations with their own tangent origin.

- Split Spatial smoothing into independent `PositionSmoothingTime` and `RotationSmoothingTime`, with zero disabling only its own channel. Changing one setting preserves the other's history. The followed Ghost's rotation setting controls reference orientation and scene repositioning, independently of position smoothing and optional motion prediction.

- Rename data components to `Trait` / `Trait<TData>` and root membership to `Ghost.Traits` / `IGhost.Traits`. Align concrete sample and test component names, Inspector labels, diagnostics and documentation with Traits, preserving Unity script asset GUIDs.

- Add opt-in `Spatial.PositionSmoothingTime` and `RotationSmoothingTime` for double-precision position and quaternion smoothing before reference projection. Preserve raw pose access, smooth followed references consistently, inherit attachments once, and provide `ResetSmoothing` for teleports. Demonstrate optional motion binding with Relative World's traits.

- Make `Ghost.GetRequired<T>()` failures distinguish missing and duplicate root components, with entity identity, root GameObject, concrete component types, assemblies and instance IDs. Direct missing-component errors to the Kind's Manifestation Blueprint and its Ghost Prefab field, explain missing blueprint/prefab assignments and root-only lookup, and avoid suggesting both failure causes at once.

- Expose public `Anchor` and `Realm` context on `GhostInitializer`, `PresenceDetector` and `PresenceDetectorComponent`. Bind before startup callbacks, retain the actual owner across scene reparenting, and clear on detachment or Anchor disposal. Keep initializer context across detector restart/replacement and use direct Anchor access in the car sample.

- Expose `Realm.Ghosts` as a read-only membership snapshot for LINQ searches over entity IDs, types and traits. Include prepared, pending and retained unavailable roots so initialization can resolve already-created parents by partial ID; exclude removed or destroyed roots from new snapshots and return an empty list after disposal.

- Expose `Trait.Ghost` for direct access to the trait's root Ghost, including disabled traits and inactive roots before the first activation. Resolve and cache the same-GameObject component lazily, retry after a missing or destroyed owner, and demonstrate identity and sibling-component access in Relative World.

- Name newly created Ghost root GameObjects by their entity ID, including prefab clones and plain roots, before application initialization. Keep display metadata independent and preserve root names during metadata updates and reuse. Support Unity name-based lookup and Anchor-scoped hierarchy access.

- Expose `Ghost.Traits` and `IGhost.Traits` as read-only root-trait membership snapshots, including disabled and unbound traits. Observe component additions and removals on each access, reuse unchanged snapshots for realm updates, and use direct typed Ghost access in sample initializers. Keep identity, spatial pose and traits open in the Ghost Inspector, with projected Unity pose, trait enable switches and inline serialized settings.

- Demonstrate trait-driven attachment in Relative World with separate bird feet, SDK forward/right/down offsets and Geo Source context-menu controls to detach to absolute release poses and reattach to the moving bird. Keep the imported sample and its consumer scenario in sync.

- Add `Spatial.Attach(parentKey, localPosition, localRotation)` and `Detach()` for same-realm entity attachments. Parents may arrive after their parts; missing parents and cycles suppress presentation while preserving tracking. Attachment chains resolve parent-first without reparenting Ghost roots, and detach resumes the latest cached absolute pose.

- Make Spatial inputs explicit: replace `SetPosition` with `SetCartesianPosition` / `SetEarthCenteredPosition`, and `SetRotation` with `SetSourceRotation`. Add direct geographic yaw/pitch/roll degrees on Spatial and ReferenceFrame; heading is clockwise from true north, pitch nose-up and roll right-wing-down. Named angles keep their physical meaning across source-axis presets. Replace `UsesEarthCenteredRotation` with `RotationSpace`, and update Relative World to ingest SDK angles directly. Clarify full reference-attitude cancellation and fixed-camera use.

- Refresh architecture and lifecycle diagrams for authored trait readers, shared geographic/ECEF references and persistent view requests. Keep validation documentation focused on test setup and API contracts.

- Document publishing absolute WGS84 positions relative to another tracked entity, with verified geographic examples and coverage for bound readers on separate anchors.

- Clarify XML API documentation for detection, trait binding, query filters, view requests and spatial conversions. Add brief code comments explaining lifecycle reentry, handover timing, coordinate mathematics and sample SDK mappings.

- Add Geographic reference space with WGS84 `GeoPosition` input and ECEF double-precision storage. The followed or manual reference defines the local tangent frame directly, including entity-local attitude conversion, inverse helpers, visibility range and reference-loss behavior. Realm Setup exposes geographic coordinates and attitude axes. Update Relative World to feed geographic readings without a static ENU projection origin. Support native ECEF positions and body-to-ECEF attitudes, configurable right-handed body axes, independent input channels, mixed local/ECEF attitudes and inverse ECEF attitude helpers.

- Allow Realm Setup to follow an entity selected at runtime: leave Entity ID empty and assign `ReferenceFrame.FollowedGhost` once the target is known. The frame retains its authored settings and waits for a valid reference pose before showing spatial presentation. Inspector identity fields are optional for runtime assignment; configured IDs still require Anchor and Kind.

- Add per-reference-frame coordinate conventions with Unity, East North Up (ENU), North East Down (NED) and custom signed-axis mappings. Realm Setup exposes a compact preset selector with inline custom axes. Position and quaternion projection, inverse conversions and reference following honor the selected handedness while retaining double-precision displacement. Runtime changes reproject cached poses without replacing Ghosts or views.

- Remove the Setup Help foldouts from Ghost, Realm Setup and Anchor Setup Inspectors and the Open Emas Diagnostics buttons from Realm Setup and Anchor Setup Inspectors.

- Replace separate Manifestation Variant assets with serializable named rows inside each blueprint. Edit Name and View Prefab together in a reorderable Inspector table with add/remove controls and duplicate-name validation. Update all shipped and imported sample blueprints to this format.
- Remove DetailLevel, detail mappings, SetDetailLevel and detail-specific Manifest overloads. Each variant selects one prefab; model alternative LODs with distinct variant names. Use Manifest to request a view and Demanifest to cancel it; unspecified or unknown variants use the blueprint fallback.

- Remove the Callback sample and reduce the Emas example to three cars and SDK replacement, without aircraft or cockpit demonstrations.
- Rework Relative World as steady driving past stationary parked cars on alternating sides, with explicit membership cleanup and road markings driven by reference travel. Add an authored bird view whose SDK position and heading circle the moving reference.

- Add `PresenceDetectorComponent` for direct scene authoring and `GhostInitializer` for Anchor-local SDK mappings. Plain C# detectors, constructor injection and Realm initializer registration remain supported.
- Configure all sample prefabs with detector and initializer components; retain code-based detector replacement in the larger example.
- Reduce Ghost, Realm and Anchor inspector text, placing guidance in tooltips and optional help and runtime details in foldouts.

- Reorganized Ghost, Realm Setup and Anchor Setup Inspectors into guided sections with units, degree-based rotation editing, conditional settings and read-only runtime identity/health. Running startup configuration is locked until detached/stopped. Diagnostics now inspect every live Realm, with name filtering, grouped detector health and timing, plus an optional Scene view overlay.

- Removed Anchor blueprint registration/unregistration and Anchor Setup blueprint overrides. Each Kind now resolves to one Realm blueprint across all anchors. Move existing Anchor Setup assignments to Realm Setup and code registrations to `Realm.RegisterManifestationBlueprint`; use variants for appearances or separate realms for independent mappings. Automatic view requests remain configurable per anchor.

- Added protected `Ghost.OnUpdate()` for entity-specific behavior after all trait readers and before spatial projection. The realm invokes enabled, owned, available or pending roots once per update; startup finalization and view requests do not tick them. Exceptions use detector failure cleanup with entity context.

- Made `Ghost` a concrete, directly authorable component and removed `DefaultGhost`; code and prefabs can use `Ghost` with reusable traits without an entity subclass. All four samples now use plain Ghost roots with saved traits. Existing `DefaultGhost` components should be replaced with `Ghost`. Spatial pose application now lives on `Spatial`; the realm still updates all traits before capturing one reference and coordinating projection.

- Enabled `Spatial` components now use identity projection when `Realm.ReferenceFrame` is null: positions and rotations apply directly in Unity world space without a distance limit. Clearing a reference restores this default; disable `Spatial` to release transform control.

- Added optional `Detect(..., source: object)` and weak `Presence.Source` access for a discovered proxy, SDK client or application context. The source is available before initialization, replacement rebinds existing Ghost traits, and disappearance/handover/removal release it. Collected sources and destroyed Unity objects resolve to null; Emas never owns or disposes them. Sample readers resolve the supplied source each update instead of retaining it in closures.

- Removed `PresenceDetector.Report` and Presence-owned trait installation. Ghosts now define `Trait<TData>` components in their prefab or with `RequireComponent`; initializers bind source-independent value readers with `Bind(Func<TData>)`. The realm refreshes enabled traits before projection and queries, releases bindings on disappearance/handover/removal, and reconnects retained Ghosts on rediscovery. All four samples now separate arrival/departure detection from SDK data reads and ship with the traits authored on their Ghost prefabs.

- Rebuilt every sample around authored tracking, Ghost and view prefabs, manifestation variants, blueprints, materials and configured scenes. Replaced Bootstrap components and runtime scene construction with focused source providers; the multi-source and relative-world samples now use Inspector-configured isolated Realm Setup lifetimes.
- Added a short README introduction to the detector, trait, Ghost, View and Realm roles before the implementation steps.
- Split internal entity lifetime into `Population` and moved view-request/detail bookkeeping into `ViewManager`. `Realm` remains the public facade and update coordinator in one non-partial file; removed `Realm.Presences.cs` without changing the public API.
- Made realm identity and command handling explicit internal components: `IdentityMap` owns keyed records and instance-safe lookup/removal; `Realm` and `SceneChangeQueue` directly share `CommandQueue<T>` for FIFO ordering and reentrancy-safe draining while retaining their detector and Unity scene policies.
- Removed the generic polling and callback detector adapters from the core package. Applications now subclass `PresenceDetector` directly; the quick-start and relative-world samples demonstrate small SDK-specific detectors with data reporting, callback dispatch and subscription cleanup.
- Reduced the test suite to documented public API contracts and required sample consistency checks; removed duplicate scenarios, private implementation checks and test-assembly access to internals.
- Fixed direct detector reports leaving partially initialized Ghosts available after initializer or trait failures; failure cleanup now stops the affected registration.
- Fixed late-enabled prefab anchors skipping presence configuration and reparented anchors retaining detector subscriptions after disable.
- Fixed cached `MarkPublished` updates failing to restore retained Ghosts or cancel disappearance grace after inactivity.

- Reworded package comments, tooltips and documentation around generic SDK data and shared coordinates.
- Added Inspector tooltips to prefab realm settings and preserved blueprint tooltips in custom inspectors.
- Added a generic README setup and usage guide, including how SDK geodetic readings pass through Traits into Spatial and a ReferenceFrame.
- Renamed `PresenceSource` to `PresenceDetector`, the polling and callback detector types, `ISourceProvider.CreateSource()` to `IDetectorProvider.CreateDetector()`, and Anchor detector-management APIs. Unity script GUIDs were preserved.
- Added a stable realm-owned `Presence` handle for each detected identity, with labels, visual variants, typed capability interfaces, availability and a Ghost root. Per-Kind `RegisterPresenceInitializer<TGhost>` callbacks install developer-supplied `Trait<TData>` instances before SDK data is applied; `IRealmConfigurator` runs these registrations before prefab detectors start.
- Added one-generic polling and callback detectors that identify and report SDK data without creating or mutating Ghosts. A report with no matching trait still tracks its Presence and ignores that payload; an unassigned manifestation blueprint keeps the root viewless. Two-generic adapters and direct code integrations remain available.
- Added detector `DisappearanceGracePeriod`: disappearance makes a Presence unavailable immediately, then retains its root and handle until the grace deadline so a returning report can reuse them. Zero keeps immediate removal; `InactivityTimeout` follows the same disappearance path.
- Renamed `Blueprint` to `ManifestationBlueprint` and split each variant's detail-level prefabs into its own `ManifestationVariant` asset; unassigned kinds keep the built-in silent Ghost root, and empty blueprints no longer request or warn about missing views. Existing assets with nonempty `_views` mappings require one `ManifestationVariant` asset per variant; copy each detail mapping and assign those assets to the blueprint.
- Renamed the internal `SceneEffects` helper to `SceneChangeQueue` to clarify its role in handling nested GameObject changes.
- Added `Query.All()` to search and subscribe across every live realm, including realms started later; `ObserveWithRealm` distinguishes duplicate keys across realms and reports departures when a realm is disposed.
- Added optional realm reference frames, double-precision shared coordinates and independent root spatial channels; moving origins reproject entities without republication, preserve the last reference on loss and suppress distant presentation while retaining tracking.
- Replaced `SceneSetup` with prefab-configured `RealmSetup` and `AnchorSetup`: each setup owns an isolated, automatically updated realm with any number of one-detector anchors, realm blueprint defaults, anchor overrides and an optional reference frame. Direct realm setup remains unchanged.
- Reworked the importable Relative world sample around complete WGS84 SDK snapshots for a moving origin and target. A one-generic polling detector forwards raw geodetic readings; per-Presence position and orientation traits convert them into a fixed double-precision ENU frame, and a rotating ReferenceFrame keeps the origin fixed in Unity.

- Isolated view creation/refresh failures to the affected view, preserving tracking and view requests for explicit or appearance-driven retry.
- Removed detector populations on failure and unreported identities after restart/replacement startup handover; compatible republished roots still survive successful handovers.
- Added optional per-entity `PresenceDetector.InactivityTimeout` with unscaled timing and protected `MarkPublished(ghost)` for custom detectors updating cached ghosts; expired roots and views are removed automatically.

- Captured blueprint settings per realm or anchor registration so live asset edits cannot change another scope's views; None now resolves to no prefab, and Inspector validation rejects whitespace-only variant IDs.
- Made detector failure cleanup finish before ghost deactivation can reattach the detector; added `PresenceDetector.CaptureDispatcher()` for registration-safe custom callbacks and clarified direct publication timing.
- Added `Anchor.UnregisterManifestationBlueprint(kind)` to restore realm defaults; re-registering a blueprint after changing its kind clears its old registration and refreshes requested views for both kinds.
- Deferred updates for detectors removed and reattached during the same update until the following update.
- Made ghost contract errors identify the ghost and duplicate root components without repeated logs; added a `GetRequired<T>()` extension for required `IGhost` contracts and a Ghost Inspector with read-only live state.
- Rolled back new detectors when a multi-detector `GetOrCreateAnchor` call fails on an existing anchor, preserving earlier detectors and prepared identities.
- Avoided temporary result lists for scalar queries and reused polling ownership buffers.
- Reused subscription match/departure buffers and ghost root-component lookup lists to reduce allocations during query notifications.
- Scoped prefab-configured blueprints to their anchors, added anchor blueprint overrides, and refreshed requested views when a blueprint is registered again while preserving ghost roots.

- Removed overlapping tests and consolidated related value checks; retained distinct lifecycle, failure and sample regressions.

- Added `Realm.TryGetGhost(key, out ghost)` for exact identity lookup, including retained unavailable ghosts.
- Added optional `PollingPresenceDetector.PollEvery(TimeSpan)` with immediate startup, unscaled main-thread timing, fresh restart deadlines and no catch-up bursts.

- Capture and verify intentional test exceptions in scoped assertions, with concise Test Runner output instead of expected exception stacks in the Console.

- Added explicit prefab realm teardown and `Anchor.RestartDetector(detector)` with retained ghost identities, view requests and stale-callback protection.
- Added `Query.Observe(onEnter, onLeave)` for paired arrivals and departures, including removal, lost availability and filter changes.
- Added detector labels and `LastErrorContext`; logs lead with operation context and diagnostics show expandable exception details. Expanded samples and lifecycle regression coverage.

- Made all Emas APIs main-thread-only and removed internal thread synchronization; applications now own SDK event handoff. Deferred callbacks, ordering and lifecycle protection remain.

- Added read-only detector health (`IsAttached`, `IsActive`, `LastError`) and copied anchor/detector snapshots, with generation-safe failure reporting.
- Added passive diagnostics and ManifestationBlueprint, RealmSetup and AnchorSetup Inspectors sharing runtime validation; optional ghost prefabs no longer produce warnings.
- Made example contracts read-only; expanded detector replacement and cleanup demonstrations, integration guidance and Unity regression coverage.
- Validated Unity 2022.3.62f3 and Unity 6.3 LTS (6000.3.24f1), including Windows Mono players and fresh sample imports.
- Declared the MIT license and removed empty author and invalid URL metadata; installation remains passive.

- Added a preconfigured Unity test project with imported samples and native Test Runner checks; clarified opening it through Unity Hub and testing packages in existing projects.
- Added `CallbackPresenceDetector` for event-driven integration, registration-safe dispatch and unsubscribe cleanup, with a runnable Callback quick start sample.
- Clarified detail-level, prefab selection and anchor reuse APIs; added Inspector guidance and specific configuration errors.

- Added the `Realm` entry point and `Anchor.Realm` ownership property.

- Simplified polling construction with named read, identity, data and appearance steps.

- Added complete-snapshot polling, Inspector-configured scene setup and a minimal Quick start sample.

- Added Emas entity tracking with anchors, presence detectors, typed identities, queries and optional views.
- Added `Realm.Default` for automatic Unity updates and isolated realms for explicit updates.
- Added callback-safe lifecycle handling, detector replacement, scene cleanup and bounded dispatch.
- Organized runtime, editor, tests and sample code by responsibility under Emas namespaces and assemblies.
- Added an importable sample, API and architecture references, and validated lifecycle coverage.
