# API reference

Runtime APIs use the `Emas` namespace. All operations require Unity's main thread, including detector publish/disappear callbacks and protected `Dispatch`. Applications handle SDK threading before calling Emas; Emas provides no thread synchronization or marshalling. `Realm.Default` is updated automatically by Unity; an isolated `Realm` implements `IDisposable` and advances through explicit `Update()`.

## Fast integration

| API | Contract |
| --- | --- |
| Application subclass of `PresenceDetector` | Observe SDK membership and call `Detect` or `Disappear` |
| `Realm.RegisterPresenceInitializer<TGhost>(kind, initialize)` | Choose the Ghost root type and bind its configured traits before attaching detectors |
| `Ghost.OnUpdate()` | Optional protected override, called once per realm update after traits and before spatial projection |
| `Trait<TData>.Bind(read)` / `Apply(data)` | Bind a value reader in the initializer; the Ghost component applies that value on realm updates |
| `PresenceDetectorComponent` | Attach a scene detector using the same lifecycle as a plain detector |
| `GhostInitializer.Initialize(presence, ghost)` | Bind configured traits for this Anchor before availability |
| `GhostInitializer.Anchor` / `Realm` | Access this initializer's bound context, including during initialization |
| `PresenceDetector.Anchor` / `Realm` and component equivalents | Access the detector's attached context from application code and lifecycle callbacks |
| `IRealmConfigurator.ConfigureRealm(realm)` | Optional Realm-wide registration before scene detectors attach |
| `IDetectorProvider.CreateDetector()` | Create one new or detached detector for a prefab anchor whenever it starts |
| `RealmSetup.Realm` | Access the isolated, automatically updated realm while the setup is running |
| `RealmSetup.StartRealm()` / `StopRealm()` | Start from prefab settings or dispose the realm and its anchors |
| `AnchorSetup.Anchor` | Access the live anchor for detector restart or replacement |

A detector identifies and labels arrivals and reports disappearances. The realm creates a stable Presence and its Ghost root, then runs the registered per-kind initializer to bind readers on the Ghost's configured traits. Register before any Ghost of that kind exists. For scenes, use a `GhostInitializer` beside the Anchor to supply local bindings. Traits belong on the Ghost prefab or in its `RequireComponent` declaration. Initialization runs again when the source object or capabilities change, after disappearance, and during source handover; bindings replace old readers without creating traits.

Implement one small, application-specific `PresenceDetector` subclass for an SDK feed. Override `OnStart` to publish its initial state or subscribe, `OnUpdate` for any polling, and `OnStop` to release subscriptions. Pass the application-owned SDK client into its constructor. `OnStop` runs once per started attachment, including startup failure; it must be able to clean up partially initialized subscriptions. Do not dispose a shared SDK client there.

`GhostInitializer`, `PresenceDetector` and `PresenceDetectorComponent` expose public `Anchor` and `Realm` properties. Inside their callbacks, use `Anchor.Id`, `Anchor.RestartDetector(...)` or `Realm.Ghosts` directly. Detector context is set before `OnStart`, remains available through `OnStop`, and clears on detachment. An attached detector that has stopped after an update or restart failure retains its context for retry. The selected initializer is bound before detectors start and remains bound for that Anchor's lifetime, including detector restarts and replacements; Anchor disposal clears it after detector cleanup. Both properties return null while unbound. Moving a live scene object under another Realm Setup preserves its current attachment until it is detached and reattached.

Call protected `Detect(id, kind, name, variant, capabilities, source)` to announce identity and metadata. The optional capability list contains interface `Type` values: null preserves previous capabilities, while an empty collection clears them. Capabilities describe the SDK entity; the initializer decides which configured traits to bind or enable. The detector does not carry SDK data updates.

The optional `source` argument supplies an application proxy, SDK client or other context object. It is assigned to `Presence.Source` before the initializer runs and held with `WeakReference<object>`. The property resolves the target and returns null for collected objects or destroyed Unity objects. Emas never disposes the source. Resolve Source inside readers instead of capturing its target strongly, and choose how the reader handles a missing target. Collection alone does not change detection membership. A different non-null source reference clears old trait bindings and reruns initialization on the same Ghost; null preserves the current source. Disappearance, source handover and removal release it, so supply it again on the next arrival.

`Trait<TData>` is a Ghost-root component. Its `Bind(Func<TData>)` reader supplies exactly the value the trait needs, without a dependency on the SDK proxy. Enabled, bound traits read and apply their values during realm finalization before spatial projection, activation and query notifications. Unbound or disabled traits skip updates. Reader or Apply exceptions stop the owning detector and remove its population. Detect no longer carries data; there is no Report function. With no initializer or Ghost prefab, detection creates the silent `Ghost` root.

Custom Ghost subclasses can override `protected virtual void OnUpdate()` to combine their traits. The realm invokes enabled, owned Ghosts that are available or awaiting activation, after all trait readers finish and before capturing the reference pose. The hook can therefore run before the root first activates; it must not depend on `Start` having run. Prepared and disappeared Ghosts are skipped; viewless and spatially suppressed Ghosts still update. Disabling the Ghost component skips its hook without disabling its traits or spatial placement. There is no ordering guarantee between Ghost hooks. Startup finalization and explicit view requests do not invoke it; nested realm updates are rejected. An exception stops the owning detector and removes its population, with `Ghost.OnUpdate` and entity identity in the error context.

For a polled SDK, call your read method from `OnStart` and `OnUpdate`. The detector decides its polling interval. If each read returns the complete current population, collect the current IDs, report its readings, then call `Disappear(kind, id)` for previously owned identities absent from that successful read. `OwnedPresences` supplies a membership snapshot for this comparison. Validate the SDK snapshot before applying it when malformed or duplicate entries should reject the read. A feed that returns only changes needs an explicit disappearance signal or an appropriate `InactivityTimeout`; an omitted ID alone does not remove anything in Emas.

For SDK membership events, capture `Action<Action> dispatch = CaptureDispatcher()` in `OnStart`. Queue arrival callbacks with `dispatch(() => Detect(id, kind))` and departures with `dispatch(() => Disappear(kind, id))`. Retain the delegates for unsubscribe in `OnStop`. SDK data updates belong to the proxy or application lookup used by the trait readers.

| Detector contract | Behavior |
| --- | --- |
| Identity | Repeated IDs update the same Presence; unknown disappearances are ignored |
| Lifecycle | `OnStart` begins an attachment, `OnUpdate` runs during realm updates, and `OnStop` releases that attachment's resources |
| Deferred work | Captured dispatchers bind queued work to one attachment; retained old callbacks cannot change a later attachment |
| Failure | Exceptions escaping a lifecycle override or queued action stop the detector, remove its population and discard queued work |
| Replacement | Compatible Ghost roots and manifestation requests survive when a replacement detector reports the same identities during handover |
| Application responsibility | Choose polling and omission rules, order initial state with live events, copy mutable SDK data, and undo partial subscriptions |

Put `RealmSetup` on the root, then `AnchorSetup` and a `PresenceDetectorComponent` subclass on each source object. Anchors need unique IDs within their Realm. The component forwards `OnStart`, `OnUpdate`, `OnStop`, `Detect`, `Disappear`, `Dispatch` and `CaptureDispatcher` to a plain detector. Its public `Detector` exposes diagnostics, detached timeout/grace configuration and a handle for `Anchor.RestartDetector` / `ReplaceDetector`. Disabling or destroying the component releases its Anchor; disabled components opt out of startup. Use the Realm lifecycle hooks instead of defining Unity enable/disable messages in a subclass.

An optional, enabled `GhostInitializer` beside the Anchor overrides `Initialize(Presence presence, Ghost ghost)`. It replaces the Realm's per-Kind initializer on that Anchor. The blueprint selects the root and traits; without a blueprint, a plain Ghost is created. With no enabled local initializer, `Realm.RegisterPresenceInitializer<TGhost>` remains the fallback for both root type and binding. The selected initializer is captured at attachment: restart the Anchor after changing this configuration. Bindings read each Realm update, and initialization repeats after source or metadata changes and retained-root handover. Initializer exceptions use the same detector failure and rollback handling as code registrations.

`IDetectorProvider.CreateDetector()` remains an alternative for factories returning a new or detached plain detector. Enabled `IRealmConfigurator` components register Realm defaults once per lifetime before detectors attach. Nested Realm Setups own their own configurators and Anchors. Reparenting a live Anchor retains its original owner until disabled and re-enabled.

Assign blueprints on Realm Setup, at most one per Kind, shared by all its Anchors. Automatic views are optional per Anchor. Realm Setup starts on the first update after enable in Play Mode, updates its isolated Realm and disposes it on disable or `StopRealm()`. `StartRealm()` creates a fresh one. Direct `Realm.Default` and `new Realm()` integrations are unchanged.

## Tracking and lifecycle

| Operation | Contract |
| --- | --- |
| `Realm.Anchors` / `Anchor.Detectors` | Copied, read-only membership snapshots; earlier snapshots stay unchanged and disposed owners return empty snapshots |
| `Realm.Ghosts` | Copied, read-only snapshot of all live tracked roots, including prepared and unavailable Ghosts; supports LINQ |
| `Realm.RegisterPresenceInitializer<TGhost>(kind, initialize)` | Register a root type and input bindings before a Ghost of this kind exists |
| `Realm.RegisterManifestationBlueprint(blueprint)` | Register or replace the blueprint for a Kind across every anchor in this realm |
| `GetOrCreateAnchor(id, params PresenceDetector[] sources)` | Create or reuse an anchor and attach supplied detectors; overload accepts a parent `Transform` |
| `Prepare<TGhost>(anchorId, kind, entityId, variant = null)` | Optionally create an unavailable Ghost identity before detection |
| `TryGetPresence(key, out presence)` | Find a stable Presence, including one unavailable during a grace period or handover |
| `TryGetGhost(key, out ghost)` | Find its Ghost root, including unavailable and prepared roots |
| `Query(partialName = null)` / `Query.All(partialName = null)` | Query available Ghosts in one realm or across all live realms |
| `Query(description)` | Rebind an immutable query description to this realm |
| `RemoveAnchor(id)` | Stop its detectors and remove its presences, Ghosts and prepared roots |
| `Update()` | Advance an explicitly managed realm; the default realm advances automatically |
| `realm.Dispose()` | Release the realm and all owned state |

An `Anchor` exposes `Id`, `Transform`, `Realm`, `Detectors`, blueprint registration, `AddDetector`, `RemoveDetector`, `RestartDetector`, `ReplaceDetector` and `Dispose()`. Restart reuses an attached detector; replacement attaches a different instance. During successful handover, compatible Presence handles, Ghost roots and manifestation requests survive when identities are reported again. Roots are unavailable until republished. Identities still unreported after the first subsequent update and queued startup reports are removed.

Detector failure or removal deletes its population immediately and discards its queued callbacks, regardless of the configured disappearance grace period. Failed restart or replacement startup leaves that detector attached for retry. A detector removed and reattached during an update waits until the next update to tick. See [lifecycle rules](Architecture.md#failure-and-cleanup).

## PresenceDetector, Presence and traits

| Member | Use |
| --- | --- |
| `PresenceDetector.IsAttached` / `IsActive` | Check whether an anchor owns the detector and whether its current registration accepts updates |
| `PresenceDetector.Anchor` / `Realm` | Current attached context; null after detachment |
| `PresenceDetector.Name` | Label the detector for diagnostics; it does not name its individual entities |
| `PresenceDetector.LastError` / `LastErrorContext` | Read the first error and its anchor, detector and operation context from the latest attachment |
| `PresenceDetector.OnStart` / `OnUpdate` / `OnStop` | Implement a custom SDK subscription or polling lifecycle |
| `PresenceDetector.Detect(id, kind, name, variant, capabilities, source)` | Protected metadata-only detection; returns the stable Presence |
| `PresenceDetector.Disappear(kind, id)` | Protected explicit disappearance signal for an owned identity |
| `PresenceDetector.OwnedPresences` | Protected snapshot of monitored presences, including unavailable ones |
| `PresenceDetector.InactivityTimeout` | Optional positive period without reports before a presence disappears; null disables expiry |
| `PresenceDetector.DisappearanceGracePeriod` | Nonnegative period retaining a disappeared Presence and Ghost root while unavailable; zero removes immediately |
| `PresenceDetector.Dispatch(action)` / `CaptureDispatcher()` | Queue Unity-thread work for the current registration; captured dispatchers reject stale callbacks |
| `Presence.Source` | Resolves the weak application proxy or SDK object; null when missing, collected or destroyed |
| `Presence.Key` / `Name` / `Variant` | Stable identity, per-entity label and visual variant |
| `Presence.IsAvailable` / `IsRemoved` / `Root` | Detection availability, final removal state and the initialized Ghost root |
| `Presence.Capabilities` / `HasCapability<T>()` | Snapshot and exact-interface check of SDK-reported capabilities |
| `Ghost.Traits` / `IGhost.Traits` | Read-only membership snapshot of root traits, including disabled and unbound components |
| `Trait.Ghost` | Access the Ghost on this trait's GameObject, including while inactive or disabled |
| `IGhost.TryGet<T>(out part)` / `GetRequired<T>()` | Resolve root MonoBehaviour interfaces for consumers; these are separate from reported capabilities |

A Presence is stable from its first detection or report until final removal. Its Key combines anchor ID, Kind and SDK entity ID; the same key in a different realm identifies a different Presence. `TryGetPresence` can return an unavailable Presence, so check `IsAvailable` before treating its data as current. `IsRemoved` becomes true after final removal, and a later report creates a new handle. `Root` holds the invisible Ghost even when no view has been requested. `Prepare<TGhost>` creates only an unavailable Ghost; `TryGetPresence` stays false until a detector first reports that identity. Consumers still use `IGhost` queries and root interfaces.

Emas names each created Ghost root GameObject exactly `Key.EntityId`, including prefab instances, before application initialization. `Ghost.Name` remains separate SDK display metadata; changing it does not rename the GameObject. Active roots are accessible through `GameObject.Find("bird-left-foot")`. Entity IDs can repeat across Kinds, Anchors and Realms, so a global Unity name lookup can be ambiguous. `anchor.Transform.Find("bird-left-foot")` scopes hierarchy lookup to an Anchor and can find inactive retained roots; use `Realm.TryGetGhost(key, out ghost)` when the full identity is needed. Prepared or unavailable roots are inactive and are excluded from `GameObject.Find`.

Declare SDK capabilities as interface types through `Detect`'s optional `capabilities` argument. Emas validates and snapshots the types and removes duplicates. A changed set reruns the initializer, which can use `presence.HasCapability<T>()` to bind or enable Ghost traits. Capability metadata does not add components. Configure traits on the Ghost and access them through `ghost.Traits`, `ghost.GetRequired<TTrait>()` or `ghost.TryGet<TTrait>(out trait)`.

Subclass `Trait<TData>` and implement `Apply(TData data)` using source-independent values. In the initializer, call `root.GetRequired<PositionTrait>().Bind(() => ((SdkProxy)presence.Source).Position)`. The Ghost determines which traits exist; the initializer knows the SDK and supplies the readers. For immutable SDK snapshots, read the current lookup entry on every invocation. Bindings are released on disappearance, handover and removal. A retained Ghost reconnects when its initializer runs again. For a viewless entity, leave its blueprint unassigned.

Inside a trait, `Ghost` provides direct access to its root: use `Ghost.Key`, `Ghost.Traits` or `Ghost.GetRequired<Spatial>()`. The property resolves lazily and works before the root's first activation, without requiring `Awake` or `OnEnable`. It only searches the trait's own GameObject; no Ghost there returns null, and a later access retries after a missing or destroyed component. It does not add a Ghost automatically. Emas initializes identity before application initialization and trait reads. For example:

```csharp
public override void Apply(GeoPosition position)
{
    Ghost.GetRequired<Spatial>().SetGeographicPosition(position);
}
```

`Traits` is available on both concrete `Ghost` roots and queried `IGhost` instances. Each access observes the current root components. Previously returned lists keep their membership, while their trait instances remain live Unity components that can be edited, disabled or destroyed. Child and view traits are excluded; `Spatial` is a separate component and is accessible through `GetRequired<Spatial>()`. Use `GetRequired<TTrait>()` for one required trait or `TryGet<TTrait>()` for an optional one; both reject duplicate providers.

```csharp
foreach (Trait trait in ghost.Traits)
{
    Debug.Log(trait.GetType().Name);
}
PositionTrait position = ghost.GetRequired<PositionTrait>();
```

`InactivityTimeout` defaults to null. Each detection resets the individual identity's inactivity deadline. Choose a timeout longer than the feed's normal interval; feeds that publish only changed values should normally leave it disabled. `DisappearanceGracePeriod` defaults to zero. An explicit `Disappear` or inactivity expiry makes the presence unavailable immediately; with positive grace, its stable Presence and Ghost root remain inactive until the deadline. A detection before that deadline restores them and retains an existing manifestation request. Repeated disappearances do not extend the deadline. Query subscriptions see a departure on availability loss. Once grace expires, the realm removes the root and view; a later detection creates a new Presence. Detector failure, anchor removal and realm disposal bypass grace. Trait reads do not refresh presence deadlines; arrival/departure feeds should leave inactivity expiry disabled.

The direct-Ghost path remains available: detectors can use protected `GetOrCreate<TGhost>`, `MarkPublished(ghost)` and `OwnedGhosts` when the integration deliberately writes root state itself. `GetOrCreate` and `MarkPublished` reset inactivity deadlines. Publishing cached data with `MarkPublished` during disappearance grace cancels removal and restores availability during finalization, retaining the same root and view request. Use `Detect` and Ghost-owned traits for the presence-only detector workflow.

Capture a dispatcher in `OnStart` when subscribing to SDK callbacks and release the subscription in `OnStop`. The returned callback accepts an `Action` to run during a later realm update; calls retained from an earlier attachment are ignored. Calling `Dispatch` directly from an old callback instead targets the detector's current attachment. Move SDK events to Unity's main thread before using either mechanism.

Use `IGhost.TryGet<T>` for optional application interfaces and `GetRequired<T>` when absence is a setup error. Both inspect only root MonoBehaviours, including disabled components. `GetRequired<T>` failures distinguish zero and multiple matches, identify the Ghost key and root GameObject, and list component types and instance IDs. A missing trait must be authored on the Ghost prefab selected for that entity's Kind or required by its Ghost subclass; traits on the Anchor, parents, children or views do not participate. An Anchor-local initializer runs for every detected Kind on that Anchor, so configure the required traits for each Kind it handles or branch by Kind for different compositions. Ambiguous providers are also logged with the Ghost key and component types. The Ghost Inspector keeps identity, spatial pose and trait sections open. Emas-owned metadata and cached/projected poses are read-only; application fields, trait enable switches and serialized trait settings remain editable. Each trait has a Select button to open its own component Inspector.

## Spatial coordinates and reference frames

`Spatial.Attach(parentKey, localPosition, localRotation)` selects a same-realm entity's projected pose with a Unity-local offset. The parent can arrive later; pending, unavailable and cyclic attachments suppress presentation while retaining identity. `Attach(parentKey, localPosition)` uses identity relative rotation. `AttachedTo` exposes the requested key; `Detach()` resumes the latest cached absolute channels on the next projection. Offsets use Unity axes and units independently of source-axis configuration, and Ghost roots retain their Anchor parents. See [entity attachment](Spatial.md#attach-and-detach-entities) for arrival, recovery and release-pose behavior.

| Member | Use |
| --- | --- |
| `Realm.ReferenceFrame` | Optional reference configuration; null maps spatial poses directly to Unity world space |
| `ReferenceFrame.Space` / `ReferenceSpace` | Cartesian projection (default) or Geographic WGS84 projection around the current reference |
| `GeoPosition(latitudeDegrees, longitudeDegrees, heightMeters)` | WGS84 coordinates with ellipsoidal height; properties expose the same units |
| `GeoPosition.ToEarthCentered()` / `FromEarthCentered(position)` | Convert between WGS84 and double-precision ECEF metres |
| `Spatial.SetEarthCenteredRotation(rotation, bodyAxes)` | Publish active body-to-ECEF attitude for Geographic space; body axes default to forward/right/down |
| `Spatial.RotationSpace` / `ReferenceFrame.RotationSpace` | Identify the cached quaternion basis: Source, Geographic (east/up/north), or EarthCentered (body-to-ECEF) |
| `Spatial.SetGeographicRotation(yawDegrees, pitchDegrees, rollDegrees)` | Publish intrinsic heading clockwise from north, nose-up pitch, then right-wing-down bank; independent of Coordinates |
| `ReferenceFrame.SetGeographicRotation(yawDegrees, pitchDegrees, rollDegrees)` | Set manual geographic reference attitude with the same degree convention |
| `ReferenceFrame.SetEarthCenteredRotation(rotation, bodyAxes)` | Set manual body-to-ECEF reference attitude; assigning Rotation switches back to local/source attitude |
| `ToUnityEarthCenteredRotation(rotation, bodyAxes)` / `ToEarthCenteredRotation(rotation, bodyAxes)` | Convert ECEF attitudes to/from Unity using a Geographic reference |
| `Spatial.SetGeographicPosition(position)` | Publish WGS84 coordinates; `Spatial.Position` retains the ECEF value |
| `Spatial.SetEarthCenteredPosition(position)` | Publish absolute ECEF XYZ metres for Geographic space |
| `ReferenceFrame.GeographicPosition` | Manual or last followed WGS84 reference position, available in Geographic space |
| `TryToUnityPosition(GeoPosition, out result)` / `ToGeographicPosition(Vector3)` | Geographic point projection and its inverse |
| `ToUnityRotation(rotation, GeoPosition)` / `ToSimulationRotation(rotation, GeoPosition)` | Convert local attitude at the entity's own geographic location |
| `Double3(x, y, z)` | Double-precision Cartesian position or displacement; preserve SDK double values |
| `Double3.Distance(a, b)` | Distance between shared Cartesian positions in double precision |
| `Spatial.SetCartesianPosition(position)` / `Position` / `HasPosition` | Publish shared Cartesian XYZ in Coordinates axes; Position exposes Cartesian input or ECEF storage after geographic input |
| `Spatial.SetSourceRotation(rotation)` / `Rotation` / `HasRotation` | Publish a source quaternion in Coordinates axes; Rotation and RotationSpace identify stored attitude; without it root rotation is left alone |
| `Spatial.IsInRange` | Whether the latest spatial projection can be presented |
| `Spatial.PositionSmoothingTime` / `RotationSmoothingTime` | Independent unscaled time constants; zero applies that channel directly. Changing one preserves the other's history. The followed Ghost's rotation setting governs shared reference orientation when FollowRotation is enabled |
| `Spatial.ResetSmoothing()` | Reset both smoothing histories so the next projection uses the latest input pose directly |
| `ReferenceFrame.Coordinates` | Cartesian pose axes or geographic source quaternion axes; named geographic angles and ECEF inputs have explicit conventions |
| `CoordinateSystem.Unity` / `EastNorthUp` / `NorthEastDown` | Presets for Unity, ENU and NED source coordinates |
| `new CoordinateSystem(right, up, forward)` | Custom signed source axes mapping to Unity directions; use each of X, Y and Z once via `Axis.PositiveX`, `Axis.NegativeX`, etc. |
| `CoordinateSystem.Right` / `Up` / `Forward` | Read the source axis mapped to each Unity direction |
| `ReferenceFrame.Position` / `Rotation` | Manual reference pose in shared Cartesian coordinates, or the latest resolved followed pose |
| `ReferenceFrame.UnityPosition` / `UnityRotation` | Desired Unity world pose of the reference; defaults to zero/identity |
| `ReferenceFrame.FollowedGhost` | Key to follow within this realm; assign or replace it at runtime. Null uses manual configuration or waits if no reference position exists |
| `ReferenceFrame.FollowRotation` | Follow reference orientation as well as position; defaults to true |
| `ReferenceFrame.MaxDistance` | Optional positive double presentation range; null disables the configured limit |
| `ReferenceFrame.HasPosition` | Whether manual configuration or following has provided a usable cached reference position |
| `ReferenceFrame.IsReferenceAvailable` | Whether the configured reference is currently available; loss preserves the last valid pose |
| `TryToUnityPosition(position, out result)` | Project with reference initialization and presentation-range checks |
| `ToSimulationPosition(position)` | Convert a Unity world position into shared Cartesian coordinates |
| `ToUnityRotation(rotation)` / `ToSimulationRotation(rotation)` | Convert orientations using the frame mapping; in Geographic space these overloads use the reference location |
| `DistanceTo(position)` | Double-precision distance from the cached reference |

Add enabled `Spatial` components to participating Ghost roots. Geographic space stores ECEF positions and projects them into the moving reference's local tangent frame, including local geographic attitudes or explicit body-to-ECEF attitudes. Cartesian application traits supply poses in shared units/axes for the realm; `ReferenceFrame.Coordinates` maps that convention to Unity. Changing it reinterprets cached poses on the next update, while conversion helpers use it immediately. The realm subtracts the reference and maps axes in doubles before converting to Unity floats and projects the root in world space, accounting for Anchor parents. Reference movement reprojects all spatial ghosts without requiring another entity publication. Position, rotation and articulation updates remain independent; the spatial API emits no general data-change events.

Before the first position, while an explicitly assigned reference is uninitialized, or outside the presentation range, spatial views and root rendering/colliders are suppressed while identity, availability and scripts remain active. Requested views return on range entry. Reference loss freezes its last valid pose and sets `IsReferenceAvailable` false; before any valid reference, presentation stays suppressed. With no reference frame, enabled `Spatial` components use identity projection with no distance limit; positions that cannot fit in finite Unity floats remain suppressed. Clearing `Realm.ReferenceFrame` returns to this default. Disable `Spatial` to release transform control. See [relative-world integration](Spatial.md) for complete setup, channel mapping and precision guidance.

## Queries and subscriptions

Use `TryGetGhost` when the full `Key` is known. It reads the realm registry without creating, activating or updating anything. Check `ghost.IsAvailable` before consuming its data; a found ghost may be prepared or awaiting publication during startup handover. Keys are case-sensitive and resolved only within the receiving realm. Removal stops lookup immediately, even before Unity finishes destroying the object.

Use `realm.Ghosts` for list-style searches by partial entity ID, root type or trait data. The read-only snapshot includes all live tracked roots in that realm: prepared entities, entities awaiting activation and entities retained during disappearance grace or handover. It is available inside an initializer for roots created so far. Each access takes a fresh membership snapshot; earlier lists retain their membership, while their Unity components remain subject to removal and destruction. New snapshots exclude removed or destroyed roots, and disposed realms return an empty list. No ordering is guaranteed. Use `System.Linq` to filter it:

```csharp
using System.Linq;

// Inside GhostInitializer.Initialize; select a parent already created on this Anchor.
IGhost parent = Realm.Ghosts.SingleOrDefault(candidate =>
    candidate.Key.AnchorId == Anchor.Id
    && candidate.Key.Kind == new Kind("vehicles.aircraft")
    && candidate.Key.EntityId.Contains("aircraft-main-"));

if (parent != null)
{
    ghost.GetRequired<Spatial>().Attach(parent.Key, new Vector3(0f, -0.1f, 0f));
}
```

`Contains` in this example performs a case-sensitive entity-ID substring match; `IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0` opts into case-insensitive matching. `SingleOrDefault` returns null for no match and rejects multiple matches, so scope the search by Anchor, Kind or another distinguishing property. `Where(...).ToList()` collects every match, and `OfType<MyGhost>()` selects a concrete Ghost subtype. Check `IsAvailable` before consuming source data. A parent discovered after the initializer cannot appear in the earlier snapshot; retry against a fresh `Ghosts` snapshot from your trait or use a query subscription for later available parents. Once an attachment has a complete parent Key, `Spatial.Attach` handles parent availability and rediscovery.

Queries are immutable and combine all filters. They never create ghosts or components. `realm.Query()` searches one realm; `Query.All()` searches every live realm, including `Realm.Default`, realms created by code, and prefab-configured realms. An all-realm query also sees realms created after the query or subscription. Disposing a realm removes its matches. `realm.Query(Query.All().OfKind(kind))` applies that description to just `realm`.

Queries also implement `IEnumerable<IGhost>` and support LINQ, such as `realm.Query().Where(ghost => ghost.Key.EntityId.Contains("aircraft-main-"))`. They continue to return only available roots. LINQ returns ordinary enumerable results; query subscription methods belong to the Emas `Query` description before applying LINQ.

| Filter/result | Meaning |
| --- | --- |
| `Query(partialName)` / `WithExactName(name)` | Case-insensitive substring / exact display-name match |
| `OfKind(kind)` / `InAnchor(id)` / `WithVariant(variant)` | Exact, case-sensitive ID match |
| `With<T>()` | Require a root contract |
| Enumeration / `Count` | Current available matches; empty when none match |
| `FirstOrDefault()` | First match or null; no ordering guarantee |
| `Single()` | Exactly one match; otherwise throws |
| `OnAvailable(callback)` | Notify current and future complete matches; dispose the returned subscription to stop |
| `Observe(onEnter, onLeave)` | Paired membership callbacks: `IGhost` on entry, `Key` on departure |
| `ObserveWithRealm(onEnter, onLeave)` | Paired callbacks with the owning `Realm` on entry and departure, so matching keys in separate realms remain distinguishable |

Within each realm, subscriptions notify once while a ghost remains a match. Availability loss permits a fresh notification on recovery. Outside that realm's detector, finalization and notification callbacks, current matches notify immediately. Inside those phases, notification is deferred; subscriptions created during notification wait for a later update in that realm. Cross-realm result and callback order is unspecified. Callback exceptions are isolated, and each match is rechecked before invoking the callback.

`Observe` reports departure when a previously delivered ghost is removed, becomes unavailable or no longer matches. Departures run at the update notification phase, before that subscription's arrivals. Removal and availability loss remain observable even if the identity returns before the next update; filter changes are evaluated at notification time. A departure receives a `Key` because its Unity object may already be destroyed. Keys identify ghosts within a realm, so two realms may have the same key. Use `ObserveWithRealm` when a consumer must tell those departures apart. Disposing a realm reports departures for previously observed matches of an all-realm query; a realm-scoped subscription instead ends without synthetic departures. Disposing either subscription cancels pending callbacks; consumers clear their own retained state.

## Typed values

```csharp
public static readonly Kind Car = new Kind("vehicles.car");
public static readonly Variant SmallCar = new Variant("small-car");
```

Declare named constants in application classes for autocomplete. Kinds and variants are distinct extensible value types; raw strings and kinds cannot be passed as variants. A variant does not enforce membership in a kind or guarantee a prefab mapping.

| Value | Meaning |
| --- | --- |
| `Key` | Identity tuple: anchor ID, kind and entity ID |
| Omitted/null variant in `Detect`, `Prepare` or `GetOrCreate` | Preserve the existing appearance |
| `Variant.None` | Unspecified appearance; explicitly passing it clears the appearance |
| `WithVariant(None)` | Match unspecified appearances; omitting the filter matches any appearance |

## Views and manifestation blueprints

| Operation | Effect |
| --- | --- |
| `Manifest(presence)` or `Manifest(ghost)` | Request or refresh the view selected by the current variant; return the current view or null |
| `Demanifest(presence)` or `Demanifest(ghost)` | Remove the view and cancel its request while retaining the Presence and Ghost root |

A `ManifestationBlueprint` describes one `Kind`: an optional Ghost root prefab, an inline table of named variants, and an optional fallback view prefab. Create it through **Assets > Create > Emas > Manifestation Blueprint**. Under **Variants**, use **+** to add a row, enter its **Name**, and assign its **View Prefab**. Drag rows to reorder them; select a row and use **?** to remove it. Adding a row creates a unique starter name and an empty prefab field. Names are case-sensitive and must be non-empty and unique within the blueprint. Each row requires a prefab. Row order does not affect selection.

| Name | View Prefab |
| --- | --- |
| `small_car` | `SmallCar.prefab` |
| `small_car_low` | `SmallCarLow.prefab` |
| `truck` | `Truck.prefab` |

Report `new Variant("small_car_low")` to select that appearance. Different LODs are ordinary named variants; Emas does not choose an LOD automatically. Renaming a row changes the identifier, so update the corresponding code value too. There are no separate variant assets.

For code configuration, `new ManifestationVariant(name, prefab)` creates one serializable row. Its `Name`, `Variant` and `Prefab` properties expose that mapping:

```csharp
blueprint.Configure(CarKind, ghostPrefab, new[]
{
    new ManifestationVariant("small_car", smallCarPrefab),
    new ManifestationVariant("small_car_low", smallCarLowPrefab)
}, fallbackViewPrefab);
```

Each Kind resolves to one blueprint in its Realm, shared by all anchors. Assign it on `RealmSetup` or call `Realm.RegisterManifestationBlueprint`. Each registration copies the blueprint's variant rows. Editing the asset takes effect in that realm only after re-registration, which refreshes requested views across all its anchors without replacing existing Ghost roots. Other realms retain their own registration snapshots. Re-registering after a kind change releases the old kind. A new Ghost prefab applies only to newly created roots. `ResolveViewPrefab(variant)` selects a prefab; `FallbackViewPrefab` exposes the optional fallback.

Selection is **exact variant name > fallback > no view**. `Variant.None` selects the fallback, if configured. Use `Demanifest` to hide a view. A configured blueprint with an unresolved requested view reports a diagnostic and removes any obsolete view. A kind with no assigned blueprint uses the built-in silent default. A report gets the initializer's Ghost type or `Ghost`; direct `GetOrCreate<TGhost>` calls get their requested Ghost type. The plain root has no view. An intentionally empty assigned blueprint is silent too; prefab setup skips automatic view requests for it.

Views require an available ghost and an active manifestation request. View binding finishes before activation. Variant changes refresh an existing request; they do not create a new request after `Demanifest`. Requests inside detector reports or finalization defer refresh, so `Manifest` may return the previous view or null until that phase completes. Requests outside those phases refresh immediately.

Emas catches view creation/refresh failures per ghost, cleans up the failed view and logs its key and prefab. The detector and Ghosts stay available. The view request survives: retry with `Manifest`, re-register the manifestation blueprint, or change the variant. Ordinary detector reports with unchanged appearance do not retry the failed view.

Implementation: [detectors](../Runtime/Tracking/PresenceDetector.cs), [Presence](../Runtime/Entities/Presence.cs), [entity traits](../Runtime/Entities/Trait.cs), [realm](../Runtime/Realm.cs), [queries](../Runtime/Queries/Query.cs), [manifestation blueprints](../Runtime/Views/ManifestationBlueprint.cs) and [variants](../Runtime/Views/ManifestationVariant.cs).

Integration policy: [Guidelines](Guidelines.md). Authoring errors appear in ManifestationBlueprint/RealmSetup/AnchorSetup Inspectors using the same validation as runtime registration. **Window > Emas** passively inspects every live Realm, including Realm Setup instances and realms created from code. Select a Realm, filter by Anchor or detector name, and inspect health, available/owned counts, timing in seconds and expandable failure details. The optional **Emas** Scene view overlay provides the same Realm selector and a compact health summary. Neither UI starts tracking.
