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

Create **Assets > Create > Emas > Manifestation Blueprint** with **Kind Id** `tracked.item`. It holds an optional **Ghost Prefab**, an optional **Fallback View Prefab**, and a list of **Manifestation Variant** assets. Leave Ghost Prefab empty to let the Realm create the registered `TrackedGhost` root. Create a variant asset with ID `standard` and map **Full (3)** to a detailed view prefab and **Reduced (2)** to a simpler one. A `ManifestationVariant` represents one appearance; `DetailLevel` selects its view. A missing mapping uses the closest lower level, then the blueprint fallback. With no blueprint, tracking still works but has no view.

### 2. Define reusable modules on the Ghost

The SDK reading keeps its raw WGS84 fields. The initializer maps these fields to a position reader; the module only accepts `Double3`. Both the moving origin (`origin`) and another item (`item-1`) use the same reusable module:

```csharp
using Emas;
using UnityEngine;

/// <summary>Defines an identified Ghost with a shared position module.</summary>
[RequireComponent(typeof(Spatial), typeof(PositionModule))]
public sealed class TrackedGhost : Ghost
{
    /// <summary>The category used by this integration.</summary>
    public static readonly Kind Kind = new Kind("tracked.item");
}

/// <summary>Applies source-independent Cartesian positions.</summary>
public sealed class PositionModule : EntityModule<Double3>
{
    /// <summary>Updates the Ghost's shared position.</summary>
    public override void Apply(Double3 position)
    {
        GetComponent<Spatial>().SetPosition(position);
    }
}
```

`YourGeo.Wgs84ToEnu` is **application code**, not an Emas API. Give it one fixed WGS84 latitude, longitude, and height as the local ENU conversion point. Convert each reading to Earth-centered XYZ, subtract that fixed point's XYZ, then rotate into east/up/north metres. This fixed conversion point is separate from the moving `origin` Ghost chosen below; **both** Ghosts must use the same conversion point and axes. `AltitudeMeters` here means WGS84 ellipsoidal height; convert mean-sea-level SDK altitude before passing it to the converter. The [working WGS84 conversion](Samples~/RelativeWorld/GeoProjection.cs) shows the full calculation. If the SDK also supplies orientation, the initializer converts it to a `Quaternion` for a separate rotation module; see the [orientation example](Samples~/RelativeWorld/GeoOrientationModule.cs).

### 3. Connect the detector and realm

Keep your SDK client or a proxy lookup in the application component. The detector only announces arrivals and departures. This example exposes two application-facing methods; call them from your SDK's membership callbacks on Unity's main thread:

```csharp
internal sealed class TrackedDetector : PresenceDetector
{
    internal Presence Arrive(string id, object source, string name = null)
    {
        return Detect(id, TrackedGhost.Kind, name, source: source);
    }

    internal void Leave(string id)
    {
        Disappear(TrackedGhost.Kind, id);
    }
}
```

For an SDK that supplies membership events, subscribe in `OnStart`, capture a dispatcher with `CaptureDispatcher()`, and unsubscribe in `OnStop`. Queue detections and explicit disappearances through that captured dispatcher so callbacks retained from an older attachment are ignored. All detector operations and callbacks run on Unity's main thread; the application handles any thread transfer. See the [callback sample](Samples~/Callbacks/FeedDetector.cs).

Here `blueprint` is the optional asset from section 1. `SdkProxy` represents your SDK's proxy class; `originProxy` and `itemProxy` are objects already received during discovery. Supply them when announcing arrival:

```csharp
var detector = new TrackedDetector();
Realm realm = new Realm();
realm.RegisterPresenceInitializer<TrackedGhost>(TrackedGhost.Kind, (presence, root) =>
{
    Spatial spatial = root.GetComponent<Spatial>();
    root.GetComponent<PositionModule>().Bind(() =>
    {
        var proxy = presence.Source as SdkProxy;
        return proxy == null ? spatial.Position : YourGeo.Wgs84ToEnu(
            proxy.LatitudeDegrees, proxy.LongitudeDegrees, proxy.AltitudeMeters);
    });
});
realm.RegisterManifestationBlueprint(blueprint);

realm.ReferenceFrame = new ReferenceFrame
{
    FollowedGhost = new Key("items", TrackedGhost.Kind, "origin"),
    UnityPosition = Vector3.zero,
    FollowRotation = false
};
realm.GetOrCreateAnchor("items", detector);
detector.Arrive("origin", originProxy);
detector.Arrive("item-1", itemProxy);
```

The Ghost defines its modules through `RequireComponent` or its prefab. The initializer only binds their inputs, and runs again if a retained Ghost is rediscovered or handed to another detector. No data payload travels through the detector. Each realm update reads the enabled modules, applies both converted positions, and then projects them relative to `origin`. Updating a proxy requires no further detection call. `FollowRotation = false` follows position only; use `true` after binding a rotation module.

`Presence.Source` resolves a weak reference. It can hold a proxy, SDK client or another application source object. It returns null if collected or destroyed as a Unity object; the example preserves the last position in that case. Resolve Source inside the reader, as shown, to avoid a closure retaining the proxy strongly. The application owns its lifetime. Disappearance, handover and removal release the reference. Passing a different source for the same identity reruns initialization; omitting it preserves the current source.

For snapshot SDKs, supply the SDK client or a stable proxy that exposes its current snapshot. The initializer can use that source directly; reusable modules still consume only their own input types.

Call `realm.Update()` each frame and `realm.Dispose()` when done. `Realm.Default` updates automatically. To request a view for one available Presence:

```csharp
if (realm.TryGetPresence(new Key("items", TrackedGhost.Kind, "item-1"), out Presence item)
    && item.IsAvailable)
{
    realm.Manifest(item, DetailLevel.Full);
}
```

`realm.Demanifest(item)` removes only its view.

### 4. Use a prefab instead

```text
World  (RealmSetup: blueprint; Use Reference Frame; Follow Ghost)
  Items  (AnchorSetup: id "items"; SDK provider)
```

Put `RealmSetup` on the root and assign the optional blueprint. On `Items`, put `AnchorSetup` and exactly one enabled component implementing `IDetectorProvider` on the **same** GameObject. Its `CreateDetector()` creates your membership detector. Implement `IRealmConfigurator` on that component to register the initializer and SDK-to-module readers before detectors start. Author the `PositionModule` on the Ghost prefab; `RequireComponent` supplies it when Emas creates a root in code. Leave **Automatic Views** on for prefab-managed manifestations. Realm Setup updates and disposes its realm. The [four samples](Samples~) ship with this setup already authored.

For the moving origin, set **Follow Ghost** to anchor `items`, Kind `tracked.item`, entity `origin`. There are no latitude/longitude fields on `ReferenceFrame` or Realm Setup: their **Position** field is already-converted Cartesian `Double3`. For a fixed reference, convert its latitude/longitude/altitude with the same `YourGeo.Wgs84ToEnu` function and assign that `Double3` to `ReferenceFrame.Position` instead of following a Ghost. `Unity Position` chooses where the reference appears in the scene. If your module writes ordinary local Unity transforms instead of `Spatial`, omit the reference frame. See [Spatial](Documentation~/Spatial.md) for projection and reference loss.

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
- [Validation](Documentation~/Validation.md): Unity Test Runner setup, results and limits.
