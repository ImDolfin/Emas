# Relative worlds and large coordinates

Configure a reference frame for a realm when shared Cartesian positions should be projected relative to a moving origin. Add `Spatial` to each participating Ghost root. Without a reference frame, ordinary application positioning continues to work.

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

## Apply spatial channels through entity modules

A root that uses shared Cartesian coordinates needs `Spatial`. For example:

```csharp
[RequireComponent(typeof(Spatial))]
public sealed class Car : Ghost
{
}
```

Assume `PositionPacket` carries an ID and raw double coordinates, and `RotationPacket` carries the same ID and a `Quaternion`. The detector forwards these SDK packets unchanged. Separate modules convert coordinates into the realm's shared Cartesian frame and update the independent spatial channels. The example's X/Y/Z axes already match that frame; replace the conversion in `Apply` for geodetic or other SDK coordinates:

```csharp
public sealed class PositionModule : EntityModule<PositionPacket>
{
    public override void Apply(PositionPacket packet)
    {
        Presence.Root.GetComponent<Spatial>().SetPosition(
            new Double3(packet.X, packet.Y, packet.Z));
    }
}

public sealed class RotationModule : EntityModule<RotationPacket>
{
    public override void Apply(RotationPacket packet)
    {
        Presence.Root.GetComponent<Spatial>().SetRotation(packet.Rotation);
    }
}
```

Register the root type and modules for this Kind before attaching the detector. Use `IRealmConfigurator.ConfigureRealm` for a prefab realm, or call the same registration on a realm built by code:

```csharp
realm.RegisterPresenceInitializer<Car>(CarKind, (presence, car) =>
{
    PositionModule position;
    if (!presence.TryGetModule(out position))
    {
        presence.AddModule(new PositionModule());
    }

    RotationModule rotation;
    if (!presence.TryGetModule(out rotation))
    {
        presence.AddModule(new RotationModule());
    }
});
```

Inside a custom `PresenceDetector`, each SDK event reports its data to the Realm:

```csharp
Report(positionPacket.Id, CarKind, positionPacket);
Report(rotationPacket.Id, CarKind, rotationPacket);
```

The detector identifies the Presence and forwards the packet. The Realm creates its root and modules when first reported, then invokes the module matching the packet type. A new position packet leaves cached rotation intact; a rotation packet leaves position intact. `Spatial.HasPosition` and `HasRotation` indicate which channels have arrived. Without rotation data, Emas leaves the Ghost root's rotation under application control.

Every `Report` records activity for `InactivityTimeout`. `Spatial.SetPosition` and `SetRotation` store spatial state; neither reports tracking membership on its own. Direct code integrations using cached Ghost roots can still call `MarkPublished(ghost)` after fresh data.

After detector processing, the Realm resolves the reference once and projects all participating roots using their latest spatial state. A reference movement repositions unchanged entities too. Partial channels do not trigger unrelated application refreshes. For interpolated feeds, evaluate the reference and entity samples at a common presentation time before reporting them.

## Preserve precision before Unity

Store and transport global positions as `Double3`, which has three `double` components. Convert SDK axes and units into one shared Cartesian coordinate system for the realm. Different detectors must feed the same system; SDK-specific geodetic or geocentric conversion belongs in an `EntityModule<TData>` after the detector forwards raw SDK coordinates.

The relative displacement is calculated in double precision before its final conversion to a Unity position:

```text
Unity position = Unity reference position
               + Unity reference rotation
               * inverse(reference rotation)
               * (entity position - reference position)
```

With position-only following, the inverse reference rotation is omitted. Absolute positions around one billion metres can therefore yield nearby Unity positions such as `20.25 m` without first rounding the global values into floats. Converting an already-rounded global `Vector3` to `Double3` cannot recover precision.

Projected roots stay beneath their Anchors. Projection sets world position and compensates for parent placement; do not add an Anchor's offset to spatial coordinates a second time. Ordinary ghosts without an enabled `Spatial` retain their existing positioning behavior. Network scenery that should move with the reference should use spatial projection too; a local cockpit can remain fixed in the Unity scene.

Disabling `Spatial` stops projection and restores renderers and colliders that spatial culling had disabled. Setting `Realm.ReferenceFrame` to null releases projection and restores that presentation on the next realm update; requested views resume through the usual refresh phase. Roots keep their last projected world pose in either case. Emas does not restore a previous transform pose; application positioning can take over from the current pose.

Only one system should write a participating root's position and published rotation. Remove old root-position behaviors such as the example's `ApplyPosition` from spatial ghost prefabs. Dynamic Rigidbody motion or other transform writers require an application-specific integration; spatial projection directly places the root.

## Limit distant presentation

`MaxDistance` is an optional positive distance in the shared coordinate units, measured in doubles from the reference. Null disables the configured range limit. Select a range appropriate for your visual scale; relative coordinates far from the reference still have the precision limits of Unity floats.

A spatial ghost without a position, without an initialized reference, or outside the presentation range keeps its tracking identity and data. Its requested view is suppressed; entering range creates the requested view automatically. `Spatial.IsInRange` describes its latest projection result. Root rendering and colliders are also suppressed outside the range while root scripts remain active. This is a presentation limit, not entity removal or a query-availability filter.

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

Import the separate **Relative world** sample, open `RelativeWorld.unity`, and press Play. The scene creates an isolated realm, camera and two simple car manifestations. `SimulatedGeoSdk` returns one complete snapshot containing continuously updated `origin` and `target` readings. The application-specific `GeoDetector` subclasses `PresenceDetector`, reads both entities in `OnStart` and `OnUpdate`, and forwards their raw SDK data with `Report`. The simulated population always contains these two entities; a changing SDK population also needs explicit `Disappear` calls for missing identities.

`GeoPositionModule` converts WGS84 latitude and longitude in degrees and ellipsoidal altitude in metres relative to the fixed datum **52.520008° N, 13.404954° E, 40 m**. It writes double-precision ENU positions to `Spatial`, with `Double3.X` east, `Y` up and `Z` north. `GeoOrientationModule` converts yaw clockwise from true north, pitch nose-up and roll right-wing-down for a root whose local axes are +Z forward, +X right and +Y up. The Realm installs both modules on each `RelativeCar` Presence before applying readings.

`ReferenceFrame` follows `origin` with `FollowRotation = true`, so that car stays at Unity position zero and identity rotation. The target moves and turns relative to it. The fixed datum and double-precision subtraction keep global coordinate magnitudes out of Unity float transforms.

Read [RelativeWorld.cs](../Samples~/RelativeWorld/RelativeWorld.cs) for realm ownership and configuration, [GeoDetector.cs](../Samples~/RelativeWorld/GeoDetector.cs) for detection, [SimulatedGeoSdk.cs](../Samples~/RelativeWorld/SimulatedGeoSdk.cs) for raw readings, and [GeoPositionModule.cs](../Samples~/RelativeWorld/GeoPositionModule.cs) and [GeoOrientationModule.cs](../Samples~/RelativeWorld/GeoOrientationModule.cs) for coordinate mapping. Disable the component to release its realm and generated objects.
