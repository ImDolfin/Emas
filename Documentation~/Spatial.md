# Relative worlds and large coordinates

Configure a reference frame for a realm when shared Cartesian positions should be projected relative to a moving origin. Add `Spatial` to each participating Ghost root. Without a reference frame, enabled `Spatial` components use an identity frame: stored positions and rotations map directly to Unity world space, with no distance limit. Ghosts without an enabled `Spatial` remain application-positioned.

## Configure a prefab realm

Add **Emas > Realm Setup** to the prefab root. Its **Manifestation Blueprints** list holds visual defaults for that realm. Add one **Emas > Anchor Setup** for each anchor frame, on the root or a child object. Each Anchor Setup needs a unique **Anchor Id** within this realm and exactly one enabled component implementing `IDetectorProvider` on the same object. That component returns a `PresenceDetector` from `CreateDetector()`. An enabled `IRealmConfigurator` beneath the Realm Setup can register the Ghost root type and spatial modules for each Kind before detectors start. Anchor manifestation blueprints can override the realm defaults. Each blueprint covers one Kind and may reference separate Manifestation Variant assets for different appearances, each with its own detail-level views. For example:

```text
Screen (RealmSetup: manifestation blueprints and reference frame)
  Vehicles (AnchorSetup: id vehicles; CarDetectorProvider)
  Signs (AnchorSetup: id signs; SignDetectorProvider)
Environment (another RealmSetup with its own anchors and reference frame)
```

For a car that stays near the Unity origin, enable **Use Reference Frame** and **Follow Ghost** on the screen's Realm Setup. Enter the car's anchor ID (`vehicles`), kind ID and entity ID (`my-car`). Set **Unity Position** to `(0, 0, 0)`, **Unity Rotation** to identity and **Follow Rotation** as needed. To hide distant views, enable **Limit Distance** and enter a positive **Max Distance** in the shared coordinate units. The followed ghost must be in this same realm and have an enabled `Spatial` component with a published position.

For a fixed origin, leave **Follow Ghost** off and enter the frame's **Position** as doubles in the same coordinate system as the Ghosts, plus its **Rotation**. Each prefab instance creates its own realm and frame on the first update after enable. Read the live frame through `realmSetup.Realm.ReferenceFrame`. Disabling the setup disposes that realm. [Getting started](GettingStarted.md) shows the complete detector-provider wiring.

## Keep your car fixed by code

```csharp
realm.ReferenceFrame = new ReferenceFrame
{
    FollowedGhost = new Key("vehicles", CarKind, "my-car"),
    UnityPosition = Vector3.zero,
    UnityRotation = Quaternion.identity,
    FollowRotation = true,
    MaxDistance = 5000.0
};
```

`FollowedGhost` identifies a ghost in this realm. Its enabled `Spatial` component supplies the reference's latest shared Cartesian position and, when published, rotation. Your car maps to the configured Unity pose; other spatial ghosts move and rotate relative to it. `FollowRotation = false` follows position only, allowing your car's heading to change in Unity.

You can also drive the frame manually, without a reference ghost:

```csharp
ReferenceFrame frame = new ReferenceFrame
{
    Position = new Double3(1000000000.125, 0.0, 1000000000.375),
    Rotation = Quaternion.identity,
    UnityPosition = Vector3.zero,
    UnityRotation = Quaternion.identity,
    MaxDistance = 5000.0
};
realm.ReferenceFrame = frame;

// Assign fresh reference data before the realm update.
frame.Position = new Double3(reference.X, reference.Y, reference.Z);
```

`Realm.Default` updates automatically. An isolated `new Realm()` needs an application-owned `Update()` call after its incoming data is processed and must be disposed when its owner stops. A `RealmSetup` advances its own isolated realm automatically. Choose a small Unity reference position near the scene origin.

## Apply spatial channels through Ghost modules

Configure independent position and rotation modules on the Ghost prefab, or require them on its class:

```csharp
[RequireComponent(typeof(Spatial), typeof(PositionModule), typeof(RotationModule))]
public sealed class Car : Ghost
{
}

public sealed class PositionModule : EntityModule<Double3>
{
    public override void Apply(Double3 position)
    {
        GetComponent<Spatial>().SetPosition(position);
    }
}

public sealed class RotationModule : EntityModule<Quaternion>
{
    public override void Apply(Quaternion rotation)
    {
        GetComponent<Spatial>().SetRotation(rotation);
    }
}
```

The initializer adapts your SDK to those reusable modules. Here the SDK's axes already match the shared frame:

```csharp
realm.RegisterPresenceInitializer<Car>(CarKind, (presence, car) =>
{
    Spatial spatial = car.GetComponent<Spatial>();
    car.GetComponent<PositionModule>().Bind(() => presence.Source is SdkProxy proxy
        ? new Double3(proxy.X, proxy.Y, proxy.Z) : spatial.Position);
    car.GetComponent<RotationModule>().Bind(() =>
        (presence.Source as SdkProxy)?.Rotation ?? spatial.Rotation);
});
```

`SdkProxy` is your SDK's concrete proxy type, supplied once with `Detect(id, CarKind, source: proxy)`. `Presence.Source` resolves a weak reference; these readers preserve the last spatial state if it is collected or destroyed. Put coordinate, axis or unit conversions in these readers or an application helper. Each module only knows its input type. Bind only channels supplied by the SDK; unbound or disabled modules leave their channel unchanged. `Spatial.HasPosition` and `HasRotation` indicate whether each channel has been supplied. Missing rotation leaves root rotation under application control.

The detector calls `Detect(id, CarKind, source: proxy)` on arrival and `Disappear(CarKind, id)` on departure. Data updates require neither another detection nor a report. Each realm update reads and applies the enabled modules, resolves the reference once, and projects all roots using the resulting spatial state. Reference movement also repositions entities whose shared positions stayed unchanged. Sample reference and target data at a common presentation time for interpolated feeds.

Module reads do not refresh `InactivityTimeout`. Leave it disabled for feeds that only announce arrivals and departures. A presence feed using expiry must independently confirm continued presence with Detect.

## Preserve precision before Unity

Store and transport global positions as `Double3`, which has three `double` components. Convert SDK axes and units into one shared Cartesian coordinate system for the realm. Different detectors must feed the same system; SDK-specific geodetic or geocentric conversion belongs in the initializer-bound readers or an application helper, keeping the modules independent of the SDK.

The relative displacement is calculated in double precision before its final conversion to a Unity position:

```text
Unity position = Unity reference position
               + Unity reference rotation
               * inverse(reference rotation)
               * (entity position - reference position)
```

With position-only following, the inverse reference rotation is omitted. Absolute positions around one billion metres can therefore yield nearby Unity positions such as `20.25 m` without first rounding the global values into floats. Converting an already-rounded global `Vector3` to `Double3` cannot recover precision.

Projected roots stay beneath their Anchors. Projection sets world position and compensates for parent placement; do not add an Anchor's offset to spatial coordinates a second time. Ordinary ghosts without an enabled `Spatial` retain their existing positioning behavior. Network scenery that should move with the reference should use spatial projection too; a local cockpit can remain fixed in the Unity scene.

Setting `Realm.ReferenceFrame` to null returns to identity projection on the next realm update: roots use their stored world coordinates and rotations, and the previous distance limit no longer applies. Valid positions restore suppressed presentation through the usual refresh phase. Missing positions or coordinates that cannot fit in finite Unity floats remain suppressed.

Disabling `Spatial` stops projection and restores renderers and colliders that spatial culling had disabled. The root keeps its last projected world pose; application positioning can take over from that pose.

Only one system should write a participating root's position and published rotation. Remove old root-position behaviors such as the example's `ApplyPosition` from spatial ghost prefabs. Dynamic Rigidbody motion or other transform writers require an application-specific integration; spatial projection directly places the root.

## Limit distant presentation

`MaxDistance` is an optional positive distance in the shared coordinate units, measured in doubles from the reference. Null disables the configured range limit. Select a range appropriate for your visual scale; relative coordinates far from the reference still have the precision limits of Unity floats.

A spatial ghost without a position, with an explicitly assigned but uninitialized reference, or outside the presentation range keeps its tracking identity and data. Its requested view is suppressed; entering range creates the requested view automatically. `Spatial.IsInRange` describes its latest projection result. Root rendering and colliders are also suppressed outside the range while root scripts remain active. This is a presentation limit, not entity removal or a query-availability filter.

`Demanifest` still cancels the view request. Detector failure removes the population; explicit disappearance and inactivity expiry make a Presence unavailable immediately and remove its root after any configured disappearance grace period.

## Reference loss and conversion helpers

A new frame has no usable position until `Position` is assigned or the followed ghost supplies one. `HasPosition` describes that cached state. If a followed ghost disappears, becomes unavailable, disables its spatial component or lacks position data, `IsReferenceAvailable` becomes false and the last valid reference pose is retained. Existing entities continue to project in that frozen frame. A returning identity resumes following on the next update. With no valid reference yet, spatial presentation stays suppressed.

Application consumers can use the same conversion and distance rules:

| Method | Use |
| --- | --- |
| `TryToUnityPosition(position, out unityPosition)` | Project a double position when the reference is initialized and the position is within its presentation range |
| `ToSimulationPosition(unityPosition)` | Convert a Unity world position back into the frame's shared Cartesian coordinates |
| `ToUnityRotation(rotation)` / `ToSimulationRotation(rotation)` | Convert orientations using the frame's active rotation mapping |
| `DistanceTo(position)` | Compute distance from the cached reference in doubles |

Check `HasPosition` before inverse-position, rotation-conversion or reference-distance operations when following a ghost that has not published yet. `TryToUnityPosition` returns false while no reference exists or a result cannot fit in finite Unity floats. Use `Double3.Distance(a, b)` for distances between positions in the shared coordinate system independently of a reference frame. Followed state is resolved during realm updates, so conversion helpers use the latest resolved reference.

## Run the example

Import the separate **Relative world** sample, open `RelativeWorld.unity`, and press Play. The scene contains an authored tracking prefab, camera, ground and lighting. Realm Setup owns an isolated realm and its reference-frame settings; the assigned blueprint and variant assets select the authored Ghost root and car view prefabs. `GeoSource` supplies the SDK detector and binds the configured spatial modules before tracking starts. `SimulatedGeoSdk` returns one complete snapshot containing continuously updated `origin` and `target` readings. The application-specific `GeoDetector` subclasses `PresenceDetector`, detects both entities in `OnStart`; the GeoSource component advances and caches SDK snapshots independently. The simulated population always contains these two entities; a changing SDK population also needs explicit `Disappear` calls for missing identities.

`GeoProjection` converts WGS84 latitude and longitude in degrees and ellipsoidal altitude in metres relative to the fixed datum **52.520008° N, 13.404954° E, 40 m**. The position reader returns double-precision ENU values to `GeoPositionModule`, which writes them to `Spatial`, with `Double3.X` east, `Y` up and `Z` north. The orientation reader converts yaw clockwise from true north, pitch nose-up and roll right-wing-down for a root whose local axes are +Z forward, +X right and +Y up. The `RelativeCar` Ghost prefab defines both modules. Its initializer binds the converted values before the realm applies them.

The prefab's Realm Setup follows `origin` with **Follow Rotation** enabled, so that car stays at Unity position zero and identity rotation. The target moves and turns relative to it. The fixed datum and double-precision subtraction keep global coordinate magnitudes out of Unity float transforms.

Inspect Realm Setup, Anchor Setup and the assigned blueprint and variant assets to change the scene configuration. Read [GeoSource.cs](../Samples~/RelativeWorld/GeoSource.cs) for provider and module registration, [GeoDetector.cs](../Samples~/RelativeWorld/GeoDetector.cs) for detection, [SimulatedGeoSdk.cs](../Samples~/RelativeWorld/SimulatedGeoSdk.cs) for raw readings, and [GeoPositionModule.cs](../Samples~/RelativeWorld/GeoPositionModule.cs) and [GeoOrientationModule.cs](../Samples~/RelativeWorld/GeoOrientationModule.cs) for applying converted values. [GeoProjection.cs](../Samples~/RelativeWorld/GeoProjection.cs) contains the SDK coordinate conversion. Disable the tracking prefab to dispose its realm and tracked instances; enable it again to restart.
