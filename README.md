# Emas

A Unity 2022.3+ package that tracks SDK entities as stable Presences, creates invisible Ghost roots, and manifests optional views. Applications supply detectors, data modules, kinds, and view prefabs.

## Setup and Usage

Add `package.json` through **Package Manager > Add package from disk**. This example uses a generic tracked item. Put each C# class in its own file; an application asmdef must reference `Emas.Runtime`.

The integration uses five roles:

| Role | Responsibility |
| --- | --- |
| `PresenceDetector` | Detects SDK entity arrivals, metadata, and disappearances. |
| `EntityModule<TData>` | A Ghost component that reads a mapped value and updates entity state. |
| `Ghost` | The entity's Unity root, carrying its components and behavior independently of its view. |
| `View` | An optional visual child of the Ghost, created when manifestation is requested. |
| `Realm` | Owns tracked entities and coordinates detector updates, data application, and views. |

### 1. Choose a Kind and its views

`Kind("tracked.item")` identifies the category. A `Variant("standard")` identifies an appearance within that Kind. The detector reports both IDs; asset filenames do not select them.

Create **Assets > Create > Emas > Manifestation Blueprint** with **Kind Id** `tracked.item`. It holds an optional **Ghost Prefab**, a **Variants** table, and an optional **Fallback View Prefab**. Assign a Ghost prefab containing a plain `Ghost` and the modules from section 2. For metadata-only entities, leave Ghost Prefab empty: the Realm creates a plain Ghost without modules. Add a table row with **Name** `standard` and assign its **View Prefab**. Each row is stored inside the blueprint; no separate variant asset is needed. For a simpler appearance, add another row such as `standard_low` and report that variant from the detector. An unspecified or unknown name uses the fallback. With no blueprint, tracking still works but has no view.

### 2. Define reusable modules on the Ghost

The SDK reading keeps its raw WGS84 fields. The initializer maps these fields to a position reader; the module accepts optional `GeoPosition` readings. Both the moving origin (`origin`) and another item (`item-1`) use the same reusable module:

```csharp
using Emas;
using UnityEngine;

/// <summary>Applies WGS84 positions when a reading is available.</summary>
[RequireComponent(typeof(Spatial))]
public sealed class PositionModule : EntityModule<GeoPosition?>
{
    /// <summary>Updates the Ghost's shared position.</summary>
    public override void Apply(GeoPosition? position)
    {
        if (position.HasValue)
        {
            GetComponent<Spatial>().SetGeographicPosition(position.Value);
        }
    }
}
```

`GeoPosition` accepts WGS84 latitude/longitude in degrees and ellipsoidal height in metres. Emas stores Earth-centered positions in doubles and derives local east/north/up from the followed reference each update. There is no separate static ENU origin. Convert mean-sea-level SDK altitude to ellipsoidal height before supplying it. If the SDK also supplies attitude, decode it into a quaternion in the selected local axes; see the [orientation example](Samples~/RelativeWorld/GeoInitializer.cs).

For geographic attitude in degrees, call `spatial.SetGeographicRotation(yawDegrees, pitchDegrees, rollDegrees)`: heading clockwise from true north, nose-up pitch, then right-wing-down roll. Emas performs the conversion independently of the selected source quaternion axes. For an SDK already providing ECEF, use `spatial.SetEarthCenteredPosition(new Double3(x, y, z))` in metres and `spatial.SetEarthCenteredRotation(bodyToEcefRotation)`. The default quaternion body axes are forward/right/down; other right-handed body mappings can be supplied explicitly. Both inputs use Geographic reference mode. See [Spatial input contracts](Documentation~/Spatial.md#choose-the-input-contract).

Save a prefab with **Emas > Ghost**, `PositionModule` and `Spatial`, and assign it to the blueprint. No Ghost subclass is needed. Derive one only when the entity has additional behavior that combines its modules. Override `protected virtual void OnUpdate()` for that behavior: the Realm invokes it once per update after all module readers finish and before spatial projection. It can run before initial activation, so use the initializer for required setup. Disabled or unavailable Ghosts are skipped, except roots awaiting their first activation; view requests do not trigger this hook.

### 3. Connect the detector and realm

Keep your SDK client or a proxy lookup in the application component. The detector only announces arrivals and departures. This example exposes two application-facing methods; call them from your SDK's membership callbacks on Unity's main thread:

```csharp
internal sealed class TrackedDetector : PresenceDetector
{
    internal static readonly Kind Kind = new Kind("tracked.item");

    internal Presence Arrive(string id, object source, string name = null)
    {
        return Detect(id, Kind, name, variant: new Variant("standard"), source: source);
    }

    internal void Leave(string id)
    {
        Disappear(Kind, id);
    }
}
```

For an SDK that supplies membership events, subscribe in `OnStart`, capture a dispatcher with `CaptureDispatcher()`, and unsubscribe in `OnStop`. Queue detections and explicit disappearances through that captured dispatcher so callbacks retained from an older attachment are ignored. All detector operations and callbacks run on Unity's main thread; the application handles any thread transfer.

Here `blueprint` is the asset from section 1 with the configured Ghost prefab. `SdkProxy` represents your SDK's proxy class; `originProxy` and `itemProxy` are objects already received during discovery. Supply them when announcing arrival:

```csharp
var detector = new TrackedDetector();
Realm realm = new Realm();
realm.RegisterPresenceInitializer<Ghost>(TrackedDetector.Kind, (presence, root) =>
{
    root.GetComponent<PositionModule>().Bind(() =>
    {
        var proxy = presence.Source as SdkProxy;
        return proxy == null ? (GeoPosition?)null : new GeoPosition(
            proxy.LatitudeDegrees, proxy.LongitudeDegrees, proxy.AltitudeMeters);
    });
});
realm.RegisterManifestationBlueprint(blueprint);

realm.ReferenceFrame = new ReferenceFrame
{
    Space = ReferenceSpace.Geographic,
    FollowedGhost = new Key("items", TrackedDetector.Kind, "origin"),
    UnityPosition = Vector3.zero,
    FollowRotation = false
};
realm.GetOrCreateAnchor("items", detector);
detector.Arrive("origin", originProxy);
detector.Arrive("item-1", itemProxy);
```

The Ghost prefab defines its modules as saved components. The initializer only binds their inputs, and runs again if a retained Ghost is rediscovered or handed to another detector. No data payload travels through the detector. Each realm update reads the enabled modules, applies both converted positions, and then projects them relative to `origin`. Updating a proxy requires no further detection call. `FollowRotation = false` follows position only; use `true` after binding a rotation module.

`Presence.Source` resolves a weak reference. It can hold a proxy, SDK client or another application source object. It returns null if collected or destroyed as a Unity object; the example preserves the last position in that case. Resolve Source inside the reader, as shown, to avoid a closure retaining the proxy strongly. The application owns its lifetime. Disappearance, handover and removal release the reference. Passing a different source for the same identity reruns initialization; omitting it preserves the current source.

For snapshot SDKs, supply the SDK client or a stable proxy that exposes its current snapshot. The initializer can use that source directly; reusable modules still consume only their own input types.

Call `realm.Update()` each frame and `realm.Dispose()` when done. `Realm.Default` updates automatically. To request a view for one available Presence:

```csharp
if (realm.TryGetPresence(new Key("items", TrackedDetector.Kind, "item-1"), out Presence item)
    && item.IsAvailable)
{
    realm.Manifest(item);
}
```

`realm.Demanifest(item)` removes only its view.

### 4. Use a prefab instead

```text
World  (RealmSetup: blueprint; Use Reference Frame; Follow Ghost)
  Items  (AnchorSetup: id "items"; SDK detector; Ghost initializer)
```

Put `RealmSetup` on the root and assign the optional blueprint. On `Items`, add `AnchorSetup`, your `PresenceDetectorComponent` subclass and an optional `GhostInitializer` subclass. Override the detector's `OnStart`, `OnUpdate` and `OnStop` to handle SDK membership. Override `GhostInitializer.Initialize(Presence, Ghost)` to bind the modules already authored on the Ghost prefab. Leave **Automatic Views** on for prefab-managed manifestations. Realm Setup handles updates and cleanup. The [three samples](Samples~) ship with these components and assets already configured.

Code setup stays the same: construct a plain `PresenceDetector` with injected dependencies, register `Realm.RegisterPresenceInitializer<TGhost>`, then call `Anchor.AddDetector`. A scene `GhostInitializer` takes precedence for its own Anchor; without one enabled at attachment, the Realm's Kind registration applies. `IDetectorProvider` and `IRealmConfigurator` remain optional integration hooks when an application needs a factory or Realm-wide setup.

Choose **Geographic** under **Reference space**. **Source quaternion axes** describes input to `SetSourceRotation`: Unity means east/up/north, with ENU, NED and custom mappings also available. Direct `SetGeographicRotation` yaw/pitch/roll uses its documented angle convention regardless of this setting. For the moving origin, enable **Follow a Ghost** and enter entity `origin`, anchor `items`, Kind `tracked.item`. If the ID is known only at runtime, leave Entity ID empty and assign `realmSetup.Realm.ReferenceFrame.FollowedGhost = ghost.Key` after startup. For a fixed geographic reference, leave following off and enter latitude, longitude and ellipsoidal height, or assign `ReferenceFrame.GeographicPosition` by code. `Unity Position` chooses where the reference appears. For ordinary Cartesian data, choose **Cartesian** and use `Spatial.SetCartesianPosition(Double3)`. See [Spatial](Documentation~/Spatial.md) for projection and reference loss.

## Tests

1. In **Unity Hub**, add the project from **`Tests/Unity~`** and open it with **Unity 2022.3.62f3**.
2. Wait for package import and compilation to finish.
3. Open **Window > General > Test Runner** and choose **Run All** in both **EditMode** and **PlayMode**.

This project already includes the samples and test configuration. No manifest editing or sample import is needed. The repository root is a UPM package; `Tests/Unity~` is the Unity project to open. See [validation](Documentation~/Validation.md) for testing in other projects.

## Documentation

- [Getting started](Documentation~/GettingStarted.md): install and integrate a source.
- [Guidelines](Documentation~/Guidelines.md): contracts, ownership and contributions.
- [API reference](Documentation~/API.md): operations and behavior contracts.
- [Architecture](Documentation~/Architecture.md): ownership, update phases and lifecycle diagrams.
- [Validation](Documentation~/Validation.md): Unity Test Runner setup and API contract coverage.
