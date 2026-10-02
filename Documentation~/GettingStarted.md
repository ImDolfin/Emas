# Getting started

Emas tracks SDK entities as stable Presences, initializes invisible Ghost roots and adds visual manifestations when requested. Use Unity 2022.3 or newer; see [test project setup](Validation.md#open-the-included-test-project).

## Run the sample

Add `package.json` through **Package Manager > Add package from disk**, import **Quick start**, open its `QuickStart.unity` scene and press Play. One cube moves along its anchor's X axis. The scene is already configured with a reusable `Tracking.prefab`, Ghost and view prefabs, a blueprint with an inline named variant, and its camera and environment. Inspect and edit those assets before changing code. Disable and re-enable the Tracking object to exercise cleanup and restart.

## Build the same integration

The [Quick start](../Samples~/Minimal/) uses three small classes and a plain `Ghost` prefab. Put each class in its own file in an application assembly referencing `Emas.Runtime`. Imported sample files are already configured.

The saved `MarkerRoot.prefab` contains `Ghost` and `MarkerPositionModule`; no entity subclass is required.

1. `MarkerPositionModule : EntityModule<Vector3>` applies a local position. It knows nothing about the SDK or its proxy type.
2. `MarkerInitializer.Initialize` binds the configured module to a value reader.
3. `MarkerSource` calls `Detect("one", MarkerSource.Kind, variant: new Variant("marker"))` once in `OnStart`. Later position changes do not go through the detector.

For a real SDK, pass the discovered proxy with `Detect(id, MarkerSource.Kind, variant: new Variant("marker"), source: proxy)`. The initializer resolves it directly; `SdkProxy` below stands for your SDK's concrete proxy type:

```csharp
public sealed class MarkerInitializer : GhostInitializer
{
    protected override void Initialize(Presence presence, Ghost marker)
    {
        marker.GetRequired<MarkerPositionModule>().Bind(() =>
            (presence.Source as SdkProxy)?.Position ?? marker.transform.localPosition);
    }
}
```

The module only consumes the mapped value:

```csharp
public sealed class MarkerPositionModule : EntityModule<Vector3>
{
    public override void Apply(Vector3 position)
    {
        transform.localPosition = position;
    }
}
```

The realm reads and applies enabled, bound modules before spatial projection and query notifications. Source is weak: resolve it inside the reader and handle null if it is collected or destroyed. For immutable SDK snapshots, supply the SDK client or a stable application cache as the source. Disappearance and source handover release old readers; the initializer reconnects the same module components when a retained Ghost returns. The sample positions are **anchor-local** Unity coordinates. For shared double-precision positions, use `Spatial` and a `Double3` module as shown in [Spatial](Spatial.md).

Every `EntityModule` exposes `Ghost` for its same-GameObject root. Inside a module, use `Ghost.Key` for identity, `Ghost.Modules` for its module list, and `Ghost.GetRequired<T>()` or `Ghost.TryGet<T>()` for another root component. This works while the module or root is disabled and before the first activation; a GameObject without a Ghost returns null.

### Configure the scene and view

The imported sample already contains this setup. To create it in another scene:

1. Create an empty GameObject with **Emas > Ghost** and `MarkerPositionModule` components and save it as `MarkerRoot.prefab`. Keep its transform at the identity; this is the invisible Ghost root that receives position data.
2. Create a cube and save it as `MarkerView.prefab`. Keep its local position and rotation at zero and scale at one. Assign a material to its renderer, then remove both temporary objects from the scene.
3. Create **Assets > Create > Emas > Manifestation Blueprint**, named `MarkerBlueprint`. Set **Kind Id** to `minimal.marker` and assign `MarkerRoot.prefab` as **Ghost Prefab**. In the **Variants** table, press **+**, set **Name** to `marker`, and assign `MarkerView.prefab` in **View Prefab**. Leave **Fallback View Prefab** empty.
4. Create a scene object named Tracking. Add **Emas > Realm Setup**, **Emas > Anchor Setup** and the `MarkerSource` and `MarkerInitializer` components. On Realm Setup, assign `MarkerBlueprint` as the realm mapping for `minimal.marker`. On Anchor Setup, set **Anchor Id** to `quick-start` and leave **Automatic Views** enabled. Save the object as `Tracking.prefab` and keep its instance in the scene. Add a camera and light if the scene has none.
5. Press Play. Realm Setup creates its realm and attaches the detector, invoking `MarkerInitializer` for each detected Ghost. Emas creates a `Presence` and instantiates the authored plain `Ghost` root beneath the anchor, reads the mapped position through `MarkerPositionModule` and attaches the cube view selected by the `marker` variant. Move Tracking to move its anchor frame.

For several appearances of one Kind, add named rows to the blueprint's **Variants** table. Each row selects one view prefab. Use names such as `small_car` and `small_car_low` for alternative LODs, and report the desired name as a `Variant` from the detector. A Kind with no blueprint still gets its Ghost root and remains visually silent until a view is configured.

Each Realm Setup owns one isolated realm and can have several Anchor Setup objects beneath it. Add one `PresenceDetectorComponent` subclass and optionally one `GhostInitializer` subclass beside each Anchor Setup. Disabling the detector stops that Anchor; enabling it starts a fresh attachment. An enabled initializer is selected at attachment and replaces the Realm's per-Kind initializer for this Anchor, including its root-type selection. Author the Ghost type and modules in the blueprint. Without an enabled local initializer, `RegisterPresenceInitializer<TGhost>` supplies the root type and bindings. Restart the Anchor after changing its initializer configuration.

Assign blueprints on Realm Setup, at most one per Kind; all Anchors use those mappings. A blueprint without view prefabs is silent. Realm Setup starts on the first update after enable in Play Mode, updates its realm each frame and disposes it on disable or `StopRealm()`. `StartRealm()` creates a fresh realm; `realmSetup.Realm` is null while stopped. Nested Realm Setups own their own Anchors.

For integrations that already construct detectors, `IDetectorProvider.CreateDetector()` can supply a new or detached plain C# detector. `IRealmConfigurator.ConfigureRealm(Realm)` remains available for Realm-wide registration before detectors start; each active configurator runs once per Realm lifetime. Neither interface is required for component-based samples.

`Realm.Default` is still updated automatically. With `new Realm()`, register initializers and blueprints, attach detectors to anchors, and call `Update()` yourself.

### Read the Inspectors

Section descriptions live in tooltips. Configuration errors remain visible.

- **Realm Setup** groups Kind mappings, coordinate reference, Unity placement and visibility range. Shared positions and radii use your SDK mapping's units; Unity placement uses Unity units. Rotation fields display Euler angles in degrees, wrapping every 360?. A range must be finite and greater than zero; there is no arbitrary maximum. Inapplicable fields are disabled. Startup settings become read-only while the Realm runs; stop it before changing those settings.
- **Anchor Setup** shows identity, automatic views and the connected Realm, detector and optional initializer. Disable an attached Anchor Setup to edit its startup settings. Ghost construction and appearance belong in the Realm's blueprint list.
- **Ghost** keeps identity, spatial pose and modules visible in open sections. Inspect cached absolute input beside the projected Unity pose, edit serialized module settings inline, or select a named module to open its component Inspector. The detector supplies IDs, display name and Variant; they are not prefab settings. Disabling the Ghost only skips its `OnUpdate` hook; module and spatial components have their own enable switches. Code can enumerate `ghost.Modules` and use `ghost.GetRequired<TModule>()` or `ghost.TryGet<TModule>()` for typed access.
- **Window > Emas** groups live data by Realm, Anchor and detector. The Scene view **Overlays** menu also offers an optional **Emas** summary. Both are passive; they never create a Realm or start a detector.

### Find and manifest a presence

Ghost root GameObjects are named by their entity ID, including prefab instances. You can find an active root through Unity, for example `GameObject.Find("one")`, or use `anchor.Transform.Find("one")` to scope lookup to one Anchor and include inactive roots. Display names remain separate metadata. IDs can repeat across Kinds, Anchors and Realms; use the full Emas `Key` for an unambiguous identity lookup.

A query can find available Ghost roots across all live realms, including realms created later:

```csharp
Query markers = Query.All().OfKind(MarkerSource.Kind);
System.IDisposable subscription = markers.OnAvailable(ghost => Debug.Log(ghost.Name));
```

Dispose the subscription when its consumer stops. Use `realmSetup.Realm.Query(markers)` to apply the same filters to one setup.

When you know the identity, use the stable `Presence` handle. This example requests a view explicitly; turn off **Automatic Views** on the anchor when you want to control manifestation yourself.

```csharp
Realm realm = GetComponent<RealmSetup>().Realm;
Key key = new Key("quick-start", MarkerSource.Kind, "one");
Presence presence;
if (realm != null && realm.TryGetPresence(key, out presence) && presence.IsAvailable)
{
    realm.Manifest(presence);
}
```

`realm.Demanifest(presence)` removes its view while retaining the detected Presence and Ghost root. Report another named variant from the detector to switch a requested view. `TryGetPresence` can also find an unavailable Presence during startup handover or disappearance grace; check `IsAvailable` before consuming its data. `TryGetGhost` remains available when you only need the root.

For components, use the `Detector` property as the handle. Use `anchorSetup.Anchor.RestartDetector(detector)` to restart an attached detector and `ReplaceDetector` to change its instance. A successful handover reuses compatible roots and Presence handles when IDs are reported again. Detector failure removes its population immediately, so recovery creates new handles and roots.

For root interfaces, paired query arrivals and departures, and detector replacement, import **Emas sample** and follow its [file guide](../Samples~/Example/README.md). Its Tracking prefab owns an isolated Realm Setup and a `cars` anchor; `CarSource` demonstrates replacement while the authored blueprints and variants select each view. Consumers use `IGhost.TryGet<T>` for optional root interfaces or `ghost.GetRequired<T>()` when a missing provider is an error.

## Implement your SDK detector

For scenes, subclass `PresenceDetectorComponent` and attach it beside Anchor Setup. For code and tests, subclass the plain C# `PresenceDetector` and inject the SDK through its constructor. Both use the same lifecycle hooks:

| Override | Responsibility |
| --- | --- |
| `OnStart()` | Read initial data or subscribe to SDK events for this attachment |
| `OnUpdate()` | Poll the SDK when needed; choose any polling interval in your detector |
| `OnStop()` | Unsubscribe and release attachment-owned resources, including after startup failure |

Call `Detect(id, kind, name, variant, capabilities, source)` when an entity arrives, and `Disappear(kind, id)` when it leaves. The optional name labels the entity; the variant chooses its appearance. The minimal sample has one permanent entity, so its detector only handles startup. The SDK proxy changes independently; Ghost modules read its mapped fields on each realm update.

For a complete-snapshot SDK, compare each successful read's IDs with `OwnedPresences` and explicitly call `Disappear` for missing IDs. A missing item in a change-only feed is not a removal. Your detector owns the SDK's validation, scheduling and omission rules; Emas owns the resulting Presence lifecycle. The [README example](../README.md#3-connect-the-detector-and-realm) demonstrates snapshot comparison.

For SDK membership events, call `CaptureDispatcher()` in `OnStart` and close each event handler over the returned dispatcher. Queue `Detect` or `Disappear` through it, retain the exact delegates, and unsubscribe in `OnStop`. Each captured dispatcher belongs to one attachment, so callbacks retained after a restart cannot change the new attachment. `OnStop` also follows failed startup; make cleanup safe when only some subscriptions were acquired. SDK clients remain application-owned.

Pass SDK capability interface types through the optional `capabilities` argument of Detect. This metadata does not add components. A realm initializer can inspect `presence.HasCapability<T>()` to bind or enable modules already configured on the Ghost. It runs again when capabilities change. Reader or module exceptions stop the owning detector and remove its population; unbound and disabled modules do not read data.

To detect silence in a feed that should publish regularly, set `detector.InactivityTimeout = System.TimeSpan.FromSeconds(10)` before attachment. Null, the default, disables inactivity expiry. Each detection or data report resets that entity's deadline. Set `detector.DisappearanceGracePeriod` to retain a disappeared Presence and Ghost root for a while; zero, the default, removes them immediately. Disappearance makes it unavailable to queries at once. A new detection or report during grace restores the same handle and root; after grace expires, either creates a new one. Detector failure and anchor removal always remove immediately.

Call all Emas APIs, including SDK publish and disappear callbacks, on Unity's main thread. The application handles any thread transfer; a captured dispatcher defers work on the main thread and does not transfer it between threads. Copy mutable callback payloads before publishing or keep them unchanged until their queued report runs. See the exact lifecycle and module contracts in [API](API.md).

## Keep a network vehicle fixed in Unity

For moving-reference worlds or large global coordinates, configure the **Reference Frame** section of Realm Setup or assign `realm.ReferenceFrame` by code, and add `Spatial` to participating Ghost roots. Choose **Cartesian** for shared XYZ input or **Geographic** for WGS84 input. Geographic modules call `Spatial.SetGeographicPosition(new GeoPosition(latitude, longitude, ellipsoidalHeight))`; the reference itself defines local ENU. Choose a manual position or a Ghost key to follow. For a runtime-selected target, enable **Follow a Ghost**, leave **Entity ID** empty and assign `realmSetup.Realm.ReferenceFrame.FollowedGhost = ghost.Key` after startup; optionally set a presentation distance. Publish positions in that shared system as `Double3`; the realm calculates the relative displacement before converting to Unity floats. Position and orientation publications can arrive independently. A presentation range hides distant views while keeping their data tracked.

Follow the [relative-world guide](Spatial.md) for a fixed ego car, reference loss and SDK data mapping. Import the separate **Relative world** sample, open `RelativeWorld.unity`, and press Play to see the demonstration with a prefab-configured realm and authored car roots, view prefabs, variants and blueprints. Its `GeoSource` component connects the SDK data; Realm Setup owns the reference frame.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Nothing appears | Check the Realm Setup, Anchor Setup, Manifestation Blueprint Inspector for errors. Verify the kind ID and view prefab; an available ghost may intentionally have no view. |
| A detector stops | Its Presences and Ghosts are removed. While the prefab realm runs, find the detector in `anchorSetup.Anchor.Detectors` and inspect `LastErrorContext` and `LastError`. Fix the cause, then call `anchorSetup.Anchor.RestartDetector(detector)`. If startup stopped the realm, use the Console or an application-held detector reference. **Window > Emas** lets you select any live Realm and inspect its Anchors and detectors. The optional **Emas** overlay in the Scene view shows a compact health summary. |
| One view fails | Read its ghost/prefab error in the Console. Tracking stays active. Fix the cause and call `Manifest`, or change its manifestation blueprint, variant to retry. |
| Polled entities disappear unexpectedly | Check your detector's snapshot comparison and timeout. Compare omissions only for complete reads; use explicit SDK removals for change-only feeds. Exceptions escaping lifecycle methods or dispatched actions stop the detector. |
| Restart creates duplicates | Unsubscribe in `OnStop`, capture a new dispatcher in each `OnStart`, and dispose consumer query subscriptions when their owner stops. |
| No tests appear | Open the prepared **`Tests/Unity~`** project through Unity Hub. Package import alone does not opt a consumer into tests. See [Validation](Validation.md). |
