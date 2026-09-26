# Emas

A Unity 2022.3+ package that tracks SDK entities as stable Presences, creates invisible Ghost roots, and manifests optional views. Applications supply detectors, data modules, kinds, and view prefabs.

## Setup and Usage

Add `package.json` through **Package Manager > Add package from disk**. This example uses a generic tracked item. Put each C# class in its own file; an application asmdef must reference `Emas.Runtime`.

The integration uses five roles:

| Role | Responsibility |
| --- | --- |
| `PresenceDetector` | Reports SDK entity identities, data updates, and disappearances. |
| `EntityModule<TData>` | Applies SDK data to the Ghost root, such as updating its position. |
| `Ghost` | The entity's Unity root, carrying its components and behavior independently of its view. |
| `View` | An optional visual child of the Ghost, created when manifestation is requested. |
| `Realm` | Owns tracked entities and coordinates detector updates, data application, and views. |

### 1. Choose a Kind and its views

`Kind("tracked.item")` identifies the category. A `Variant("standard")` identifies an appearance within that Kind. The detector reports both IDs; asset filenames do not select them.

Create **Assets > Create > Emas > Manifestation Blueprint** with **Kind Id** `tracked.item`. It holds an optional **Ghost Prefab**, an optional **Fallback View Prefab**, and a list of **Manifestation Variant** assets. Leave Ghost Prefab empty to let the Realm create the registered `TrackedGhost` root. Create a variant asset with ID `standard` and map **Full (3)** to a detailed view prefab and **Reduced (2)** to a simpler one. A `ManifestationVariant` represents one appearance; `DetailLevel` selects its view. A missing mapping uses the closest lower level, then the blueprint fallback. With no blueprint, tracking still works but has no view.

### 2. Receive raw SDK data and apply it through modules

The SDK reading keeps its raw WGS84 fields. The detector forwards it unchanged; the module converts it. Here both the moving origin (`origin`) and another item (`item-1`) use the same reading type and module:

```csharp
using Emas;
using UnityEngine;

public sealed class TrackedReading
{
    public string Id;
    public string VariantId;
    public double LatitudeDegrees;
    public double LongitudeDegrees;
    public double AltitudeMeters;
}

[RequireComponent(typeof(Spatial))]
public sealed class TrackedGhost : Ghost
{
    public static readonly Kind Kind = new Kind("tracked.item");
}

public sealed class GeoPositionModule : EntityModule<TrackedReading>
{
    public override void Apply(TrackedReading reading)
    {
        Double3 enuMetres = YourGeo.Wgs84ToEnu(
            reading.LatitudeDegrees,
            reading.LongitudeDegrees,
            reading.AltitudeMeters);
        Presence.Root.GetComponent<Spatial>().SetPosition(enuMetres);
    }
}
```

`YourGeo.Wgs84ToEnu` is **application code**, not an Emas API. Give it one fixed WGS84 latitude, longitude, and height as the local ENU conversion point. Convert each reading to Earth-centered XYZ, subtract that fixed point's XYZ, then rotate into east/up/north metres. This fixed conversion point is separate from the moving `origin` Ghost chosen below; **both** Ghosts must use the same conversion point and axes. `AltitudeMeters` here means WGS84 ellipsoidal height; convert mean-sea-level SDK altitude before passing it to the converter. The [working WGS84 conversion](Samples~/RelativeWorld/GeoPositionModule.cs) shows the full calculation. If the SDK also supplies orientation, a separate module converts it to a `Quaternion` and calls `Spatial.SetRotation`; see the [orientation example](Samples~/RelativeWorld/GeoOrientationModule.cs).

### 3. Connect the detector and realm

Copy the SDK's current coordinates into `TrackedReading` in the component that owns your SDK client. Replace `sdk.ReadAll()` and the property names with your SDK's API. It must return every tracked item, including the one whose stable ID is `origin`, in the same complete snapshot:

```csharp
using System.Collections.Generic;

IEnumerable<TrackedReading> ReadSnapshot()
{
    foreach (var sdkItem in sdk.ReadAll())
    {
        yield return new TrackedReading
        {
            Id = sdkItem.Id,
            VariantId = sdkItem.AppearanceId,
            LatitudeDegrees = sdkItem.LatitudeDegrees,
            LongitudeDegrees = sdkItem.LongitudeDegrees,
            AltitudeMeters = sdkItem.Wgs84HeightMeters
        };
    }
}
```

Implement a detector for this feed. It reports the complete snapshot at startup and on each realm update, then explicitly marks omitted IDs as disappeared. Scheduling and snapshot comparison belong to this application class:

```csharp
using System;
using System.Collections.Generic;
using Emas;

internal sealed class TrackedDetector : PresenceDetector
{
    private readonly Func<IEnumerable<TrackedReading>> _readSnapshot;

    internal TrackedDetector(Func<IEnumerable<TrackedReading>> readSnapshot)
    {
        _readSnapshot = readSnapshot;
    }

    protected override void OnStart()
    {
        PublishSnapshot();
    }

    protected override void OnUpdate()
    {
        PublishSnapshot();
    }

    private void PublishSnapshot()
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TrackedReading reading in _readSnapshot())
        {
            if (!seen.Add(reading.Id))
            {
                throw new InvalidOperationException("The SDK returned a duplicate entity ID.");
            }

            Variant variant = string.IsNullOrWhiteSpace(reading.VariantId)
                ? Variant.None : new Variant(reading.VariantId);
            Report(reading.Id, TrackedGhost.Kind, reading, variant: variant);
        }

        foreach (Presence presence in OwnedPresences)
        {
            if (!seen.Contains(presence.Key.EntityId))
            {
                Disappear(presence.Key.Kind, presence.Key.EntityId);
            }
        }
    }
}
```

For an SDK that supplies change events, subscribe in `OnStart`, capture a dispatcher with `CaptureDispatcher()`, and unsubscribe in `OnStop`. Queue reports and explicit disappearances through that captured dispatcher so callbacks retained from an older attachment are ignored. All detector operations and callbacks run on Unity's main thread; the application handles any thread transfer. See the [callback sample](Samples~/Callbacks/FeedDetector.cs).

Here `blueprint` is the asset from section 1 and `ReadSnapshot` is the SDK-reading method above:

```csharp
var detector = new TrackedDetector(ReadSnapshot);
Realm realm = new Realm();
realm.RegisterPresenceInitializer<TrackedGhost>(TrackedGhost.Kind, (presence, root) =>
{
    GeoPositionModule module;
    if (!presence.TryGetModule(out module))
    {
        presence.AddModule(new GeoPositionModule());
    }
});
realm.RegisterManifestationBlueprint(blueprint);

realm.ReferenceFrame = new ReferenceFrame
{
    FollowedGhost = new Key("items", TrackedGhost.Kind, "origin"),
    UnityPosition = Vector3.zero,
    FollowRotation = false
};
realm.GetOrCreateAnchor("items", detector);
```

The detector identifies each SDK item by `Id` and forwards its `TrackedReading` without changing its coordinates. The Realm creates a Presence and Ghost, then calls `GeoPositionModule.Apply` with that reading. The module converts **both** the origin and `item-1` into the same east/up/north `Double3` system and writes each position to its Ghost's `Spatial`. `FollowedGhost` selects the origin's converted position as the frame center; Emas subtracts it from the other converted positions before placing their roots in Unity. As the origin moves, the relative positions update on the same realm update. `FollowRotation = false` follows position only; use `true` after an orientation module writes `Spatial.SetRotation`.

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

Put `RealmSetup` on the root and assign the blueprint. On `Items`, put `AnchorSetup` and exactly one enabled component implementing `IDetectorProvider` on the **same** GameObject. Put `ReadSnapshot` from section 3 in that component. Its `CreateDetector()` returns `new TrackedDetector(ReadSnapshot)` from section 3. Have the same component implement `IRealmConfigurator`: its `ConfigureRealm(Realm realm)` registers the `TrackedGhost` initializer and `GeoPositionModule` shown above. Leave **Automatic Views** on for prefab-managed manifestations. Realm Setup updates and disposes its realm.

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
