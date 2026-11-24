# API reference

Runtime types use `Emas`; application assemblies reference `Emas.Runtime`. All calls require Unity's main thread. XML comments on each API provide parameter, unit and exception details. This guide explains how the APIs fit together.

Examples assume `using Emas;`, `using System;` and `using UnityEngine;`. Variables such as `realm` and prefab references come from the calling integration.

## Fast integration

| Setup | Entry points |
| --- | --- |
| Scene-owned | `RealmSetup` owns an isolated Realm; child `AnchorSetup` components attach colocated detectors. |
| Code-owned | Construct `Realm`, register bindings/blueprints, attach detectors, call `Update()` and `Dispose()`. |
| Default world | `Realm.Default` updates automatically and is separate from scene-owned Realms. |
| SDK membership | Derive `PresenceDetectorComponent` for scenes or `PresenceDetector` for code. |
| Data binding | Derive `GhostInitializer` for an Anchor or register `Realm.RegisterPresenceInitializer<TGhost>`. |
| Optional factories | `IDetectorProvider.CreateDetector()` supplies a detached detector; `IRealmConfigurator.ConfigureRealm()` registers Realm-wide configuration before attachment. |

Author the Ghost's components before detection. Initializers bind readers; they do not add traits automatically. A colocated, enabled `GhostInitializer` overrides Realm registration for its Anchor. Restart that Anchor after changing initializer configuration. See [Getting started](GettingStarted.md).

## Tracking and lifecycle

| Realm member | Contract |
| --- | --- |
| `Anchors` / `Ghosts` | Read-only membership snapshots. Ghosts includes prepared and unavailable roots; component data remains live. |
| `RegisterPresenceInitializer<TGhost>(kind, initialize)` | Register the root type and bindings before any root of that Kind exists. |
| `RegisterManifestationBlueprint(blueprint)` | Snapshot one Kind mapping for every Anchor in this Realm. Re-register after asset edits. |
| `GetOrCreateAnchor(id, sources)` | Reuse/create an Anchor and attach detectors. An overload accepts its scene-parent Transform. |
| `Prepare<TGhost>(anchorId, kind, entityId, variant)` | Create an unowned, unavailable root before detection; no Presence exists yet. |
| `TryGetPresence(key, out presence)` / `TryGetGhost(key, out ghost)` | Exact identity lookup, including unavailable entities. Check `IsAvailable` before consuming current source data. |
| `Query()` / `Query(description)` | Select available roots or reuse another query's filters in this Realm. |
| `RemoveAnchor(id)` | Stop its detectors and remove tracked/prepared objects. |
| `Update()` | Advance an application-owned Realm. Reentrant updates are rejected; disposed Realms do nothing. |
| `Dispose()` | Idempotent cleanup. Subsequent reads are empty; configuration and subscription mutations throw. |

`Anchor` exposes `Id`, `Transform`, `Realm` and a `Detectors` snapshot. `PoseProcessingDistance` optionally limits buffering, extrapolation and smoothing to a radius around the Realm reference; farther roots project their latest raw pose directly and stay visible. Null, the default, processes all distances. `AddDetector`, `RemoveDetector`, `RestartDetector` and `ReplaceDetector` manage attachments. A detector can belong to only one attachment. `Dispose()` removes the Anchor and its objects. Blueprint registration belongs to the Realm.

Successful restart/replacement preserves compatible roots, Presence handles and view requests reported during startup handover. Unreported identities are removed after startup dispatch and the next update complete. Failure/removal bypasses grace and removes the population; failed restarts remain attached for explicit retry. See [failure and cleanup](Architecture.md#failure-and-cleanup).

## PresenceDetector, Presence and traits

| Detector member | Use |
| --- | --- |
| `Anchor` / `Realm` | Attachment context, bound before `OnStart`, available through `OnStop`, null after detachment. |
| `IsAttached` / `IsActive` | Distinguish ownership from an attachment currently accepting work. |
| `Name` | Diagnostic feed label, independent of entity names. |
| `LastError` / `LastErrorContext` | First failure and captured attachment/operation context; cleanup errors do not replace it. |
| `InactivityTimeout` | Optional positive TimeSpan since each entity's last report; null disables expiry. |
| `DisappearanceGracePeriod` | Nonnegative retention TimeSpan after disappearance; zero removes immediately. |
| `OnStart()` / `OnUpdate()` / `OnStop()` | Protected SDK lifecycle hooks. Stop also follows failed startup; clean up partial subscriptions. |
| `Detect(id, kind, name, variant, capabilities, source)` | Protected identity/metadata report returning the stable Presence. Records activity. |
| `Disappear(kind, id)` | Make an owned identity unavailable; apply configured grace. |
| `OwnedPresences` / `OwnedGhosts` | Protected snapshots, including unavailable entities. |
| `GetOrCreate<TGhost>()` / `MarkPublished(ghost)` | Direct-root integration when the detector deliberately writes state itself. Both record activity. |
| `Dispatch(action)` / `CaptureDispatcher()` | Defer main-thread work. Captured dispatchers reject callbacks from old attachments. |

Move SDK events to Unity's thread before calling Emas. Capture a dispatcher in each `OnStart` and unsubscribe exact delegates in `OnStop`. The update drains at most 256 actions present at entry; newly queued work waits. Shared SDK clients remain application-owned.

Repeated detection refreshes activity. Trait reads do not. Use inactivity expiry for regularly reported feeds, and explicit disappearances for change-only feeds. During grace the root is inactive and absent from queries; rediscovery reuses its handle. After removal, detection creates a new lifetime.

| Presence member | Contract |
| --- | --- |
| `Key` / `Name` / `Variant` | Full identity, display metadata and selected appearance. |
| `Root` / `IsAvailable` / `IsRemoved` | Root reference, current availability and final lifetime state. |
| `Source` | Weak application object; null when absent, collected or destroyed. Resolve inside readers. |
| `Capabilities` / `HasCapability<T>()` | Validated, deduplicated interface metadata. Does not create components or guarantee root contracts. |

Initializers rerun on source/capability changes, rediscovery and handover. Label/Variant changes alone do not. Existing readers are released on disappearance, handover and removal.

| Ghost/trait member | Contract |
| --- | --- |
| `Ghost.Anchor` / `IGhost.Anchor` | Actual owner, independent of scene reparenting; cleared before final cleanup callbacks. |
| `Traits` | Snapshot of root traits, including disabled/unbound ones; excludes children. |
| `Trait<TTrait>()` / `GetRequired<T>()` | Resolve exactly one root component, including while inactive; throw for missing/ambiguous matches. |
| `TryGet<T>(out value)` | Resolve an optional root contract; returns false for missing/ambiguous matches and logs ambiguity. |
| `Trait.Ghost` | Same-GameObject root, resolved lazily; null when absent. |
| `Trait<TData>.Bind(read)` | Replace the reader used during enabled trait updates. Bind before first activation. |
| `Trait<TData>.Apply(data)` | Application implementation that updates root state from an SDK-independent value. |
| `Ghost.OnUpdate()` | Optional protected behavior hook after all trait reads and before projection; may precede first activation. |

For example, a `Trait<GeoPosition?>` can preserve its previous pose when the mapped SDK reading is unavailable:

```csharp
public override void Apply(GeoPosition? position)
{
    if (position.HasValue)
    {
        Ghost.GetRequired<Spatial>().SetGeographicPosition(position.Value);
    }
}
```

Root GameObjects are named by `Key.EntityId`; `Ghost.Name` is separate display metadata. Use full-Key lookup for identity: Unity names can repeat across Kinds, Anchors and Realms. Root contract lookup never searches views or children.

## Spatial coordinates and reference frames

| API | Purpose |
| --- | --- |
| `Spatial.SetCartesianPosition(Double3, sampleTime)` | Shared source XYZ in application units/axes. |
| `SetGeographicPosition(GeoPosition, sampleTime)` / `SetEarthCenteredPosition(Double3, sampleTime)` | WGS84 or ECEF metres; stored Position is ECEF. |
| `SetSourceRotation()` / `SetGeographicRotation()` / `SetEarthCenteredRotation()` | Explicit quaternion/angle input conventions. Geographic angles are degrees; ECEF body axes default to forward/right/down. |
| `Position` / `Rotation` / `RotationSpace` / `PositionTime` / `RotationTime` | Last accepted raw channels and original SDK timestamps. |
| `HasPosition` / `HasRotation` / `IsInRange` | Independent channel validity and presentation permission. |
| `Attach(parentKey, localPosition, localRotation)` / `Detach()` | Same-Realm parent presentation with Unity-local offsets; absolute channels remain cached. |
| `ResetPresentation()` | Discard presentation histories after a deliberate discontinuity; supply the new pose first. |
| `Spatial.TryGetObservedUnityPose(out Pose)` | Project the latest raw observation through the cached reference without advancing presentation. |
| `Realm.ReferenceFrame` | Optional shared Cartesian/geographic projection; null is identity Cartesian placement. |
| `ReferenceFrame.FollowedGhost` / `FollowRotation` | Follow position and optionally cancel full reference orientation. |
| `ReferenceFrame.UnityPosition` / `UnityRotation` / `MaxDistance` | Scene placement/alignment and optional positive presentation radius. |
| `Realm.InterpolationDelay` | Nonnegative shared playback delay in seconds for timed channels. |
| `Realm.ProjectedTime` | Read-only timestamp selected by the last projection pass, or null before timed input/reset/after disposal. Unix creation timestamps on the followed Ghost automatically target current UTC through median alignment. All peers share that target but retain their own model and cap. |
| `ReferenceFrame.TimestampClock` | Code-only clock policy. `Automatic` recognizes reference timestamps within one day of calibrated host UTC as Unix seconds/nanoseconds. `Unix` forces UTC interpretation; `Simulation` retains an arbitrary epoch and relative-age alignment. |
| `ReferenceFrame.ClockOffset` | Code-only signed seconds added to host UTC to obtain simulation UTC. Zero by default; positive means the simulation clock is ahead. Applies once to the Realm in Unix mode. Changes reset alignment on the next pass without changing raw timestamps. |
| `ReferenceFrame.PredictionTime` | Code-only shared forward interval from the followed observation. Known age counts once. Default zero uses automatic age compensation; each entity retains its own strategy and cap. Independent of the followed Ghost's processing traits. |
| `Realm.ResetSpatialTime()` | Reset SDK clock alignment and sample ordering before a restarted clock publishes. |
| `Smoothing.PositionHalfLife` / `RotationHalfLife` | Independent nonnegative seconds to halve correction error; zero applies immediately. |
| `Extrapolation.Strategy` / `PredictionTime` / `MaximumExtrapolation` | Motion algorithm, individual forward interval when no timed reference is followed, and total prediction limit in seconds. The limit clamps to at least 0.007 s (rounded up from one 144 Hz frame); disable the trait to stop extrapolation. With a timed followed Ghost, ReferenceFrame.PredictionTime supplies the common target; individual trait intervals are ignored. |
| Extrapolation motion setters / `ClearVelocity`, `ClearAcceleration`, `ClearAngularVelocity` | Optional independently timed motion; clear channels when unavailable. |
| `ConstantAccelerationExtrapolation` | Estimates missing linear/angular acceleration from bounded timestamped histories. Supplied linear motion takes priority; zero angular rates hold orientation. Fits and integration are stateless across entities. |
| `SplineExtrapolation` | Fits cubic Bézier continuation from timestamped pose or SDK velocity history. Sparse observations reduce curve order; supplied acceleration constrains curvature and zero SDK angular rates hold orientation. Uses existing timing, caps and smoothing without packet correction or additional settings. |
| `ConstantTurnAccelerationExtrapolation` | Constant angular rate with constant body-frame acceleration. Estimates missing acceleration from SDK velocity or pose history, preserves supplied inertial acceleration, and shares coordinated-turn packet correction and bank fallback. Gravity is code-only in normal Inspectors. |
| `ConstantTurnClimbAccelerationExtrapolation` | Constant horizontal turn rate and tangential acceleration with independent constant vertical velocity. SDK vertical velocity or a linear height fit supplies climb rate; vertical acceleration is excluded. Full attitude prediction, packet correction and bank fallback use existing conventions. |
| `ExtrapolationContext.TryGetPositionSample` / `TryGetVelocitySample` / `TryGetRotationSample` / `TryGetAngularVelocitySample` | Read raw samples in the current estimation window newest-first, excluding timestamps after ObservationTime and before the last estimator reset. Use only during the strategy call on Unity's main thread. No allocations, interpolation or mutation; world SDK angular samples retain ECEF axes. |

Enabled Spatial owns the root's world pose. Optional Smoothing/Extrapolation modify presentation only. Original timestamps share one SDK clock; older/duplicate timed samples are ignored per channel. Supplied zero motion overrides estimates. Missing references/attachment parents and range suppression affect presentation, not tracking membership.

See [Spatial](Spatial.md) for input units/axes, strategy examples, conversion helpers, buffering, live-setting transitions and recovery. `GeoPosition`, `Double3`, `CoordinateSystem` and `Timestamp` retain the precision and conventions described there.

## Queries and subscriptions

See [Queries](Queries.md) for examples and the full filter/result/subscription reference. Filters return immutable descriptions and combine with AND; results contain available roots regardless of view/range.

```csharp
Query vehicles = realm.Query().OfKind(new Kind("vehicle")).With<Spatial>();
Query compact = vehicles.WithVariant(new Variant("compact"));
IDisposable subscription = compact.Observe(
    ghost => Debug.Log("Arrived: " + ghost.Key),
    key => Debug.Log("Left: " + key));
// Dispose subscription when this consumer stops, and clear retained state.
```

`Count`, `FirstOrDefault`, `Single` and enumeration evaluate current membership; ordering is unspecified. Known identities use exact lookup instead. `Query.All()` spans live Realms; `ObserveWithRealm` distinguishes identical Keys across them. Entries may be synchronous, so initialize callback state before subscribing. Departures precede arrivals at notification. Disposal cancels pending callbacks without clearing application collections.

## Typed values

Declare application constants for discoverability:

```csharp
public static readonly Kind Vehicle = new Kind("vehicle");
public static readonly Variant Compact = new Variant("compact");
```

Kinds and Variants are distinct case-sensitive value types. A Variant does not guarantee a blueprint mapping. Omitting a nullable variant in Detect/Prepare/GetOrCreate preserves the current value; explicitly passing `Variant.None` clears it. `WithVariant(Variant.None)` selects unspecified appearances; it does not clear the filter.

## Views and manifestation blueprints

`Manifest(presence)` or `Manifest(ghost)` requests/refreshes an optional visual child and returns it, or null while unavailable or unconfigured. Requests persist through temporary unavailability/range suppression. `Demanifest` cancels the request and removes only the view. `View.Ghost` exposes the owning root while bound.

A blueprint configures one Kind. Its root prefab contains the Ghost and traits; inline variant rows map names to view prefabs. Unspecified/unknown Variants use the optional fallback. Missing view mappings remain visually silent. Names must be non-empty, unique and case-sensitive; row order does not affect selection. Appearance LODs are application-selected Variants.

Create **Assets > Create > Emas > Manifestation Blueprint** and edit rows with **+** / **-**, or configure an asset in code:

```csharp
blueprint.Configure(Vehicle, ghostPrefab, new[]
{
    new ManifestationVariant("compact", compactViewPrefab),
    new ManifestationVariant("compact-low", compactLowViewPrefab)
}, fallbackViewPrefab);
realm.RegisterManifestationBlueprint(blueprint);
```

Registration copies settings into that Realm. Re-register after edits to refresh requested views across its Anchors; other Realms retain their mappings. Existing roots retain their components, so root-prefab changes affect new roots. A view failure logs context and leaves tracking active; fix the cause and call `Manifest` or change the mapping/Variant to retry.
