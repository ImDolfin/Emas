# Relative worlds and large coordinates

Configure a reference frame for a realm when simulation positions should be projected relative to a moving origin. Add `Spatial` to each participating Ghost root. Without a reference frame, ordinary application positioning continues to work.

## Configure a prefab realm

Add **Emas > Realm Setup** to the prefab root. Its **Blueprints** list holds defaults for that realm. Add one **Emas > Anchor Setup** for each source frame, on the root or a child object. Each Anchor Setup needs a unique **Anchor Id** within this realm and exactly one enabled component implementing `ISourceProvider` on the same object. That component returns a `PresenceSource` from `CreateSource()`. Anchor blueprints can override the realm defaults. For example:

```text
Screen (RealmSetup: blueprints and reference frame)
  Vehicles (AnchorSetup: id vehicles; CarSourceProvider)
  Signs (AnchorSetup: id signs; SignSourceProvider)
Environment (another RealmSetup with its own anchors and reference frame)
```

For a car that stays near the Unity origin, enable **Use Reference Frame** and **Follow Ghost** on the screen's Realm Setup. Enter the car's anchor ID (`vehicles`), kind ID and entity ID (`my-car`). Set **Unity Position** to `(0, 0, 0)`, **Unity Rotation** to identity and **Follow Rotation** as needed. To hide distant views, enable **Limit Distance** and enter a positive **Max Distance** in simulation units. The followed ghost must be in this same realm and have an enabled `Spatial` component with a published position.

For a fixed origin, leave **Follow Ghost** off and enter the simulation **Position** as doubles, plus its **Rotation**. Each prefab instance creates its own realm and frame on the first update after enable. Read the live frame through `realmSetup.Realm.ReferenceFrame`. Disabling the setup disposes that realm. [Getting started](GettingStarted.md) shows the complete source-provider wiring.

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

`FollowedGhost` identifies a ghost in this realm. Its enabled `Spatial` component supplies the reference's latest simulation position and, when published, rotation. Your car maps to the configured Unity pose; other spatial ghosts move and rotate relative to it. `FollowRotation = false` follows position only, allowing your car's heading to change in Unity.

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

## Publish spatial channels independently

A concrete ghost can require the spatial component:

```csharp
[RequireComponent(typeof(Spatial))]
public sealed class Car : Ghost
{
}
```

In your source's position handler:

```csharp
Car car = GetOrCreate<Car>(id, CarKind);
car.GetComponent<Spatial>().SetPosition(new Double3(packet.X, packet.Y, packet.Z));
```

In a separate orientation handler:

```csharp
Car car = GetOrCreate<Car>(id, CarKind);
car.GetComponent<Spatial>().SetRotation(packet.Rotation);
```

Articulation continues to update its own data or child transforms. Publishing orientation leaves the cached position intact; position leaves orientation intact. `HasPosition` and `HasRotation` indicate which channels have arrived. Without a rotation publication, Emas leaves that ghost root's rotation under application control.

Sources using a cached owned ghost still call `MarkPublished(ghost)` after writing fresh data if they use inactivity expiry. `Spatial.SetPosition` and `SetRotation` store spatial state; they do not publish tracking membership or emit general data-change notifications.

After source processing, the realm resolves the reference once and projects all participating ghosts using their latest spatial state. A reference movement repositions unchanged entities too. Partial channels do not trigger unrelated application refreshes. For interpolated feeds, evaluate the reference and entity samples at a common presentation time before assigning their spatial state.

## Preserve precision before Unity

Store and transport global positions as `Double3`, which has three `double` components. Convert SDK axes and units into one shared Cartesian coordinate system for the realm. Different sources must agree on that system; source-specific geodetic or geocentric conversion belongs in the adapter.

The relative displacement is calculated in double precision before its final conversion to a Unity position:

```text
Unity position = Unity reference position
               + Unity reference rotation
               * inverse(simulation reference rotation)
               * (entity simulation position - simulation reference position)
```

With position-only following, the inverse simulation-reference rotation is omitted. Absolute positions around one billion metres can therefore yield nearby Unity positions such as `20.25 m` without first rounding the global values into floats. Converting an already-rounded global `Vector3` to `Double3` cannot recover precision.

Projected roots stay beneath their Anchors. Projection sets world position and compensates for parent placement; do not add an Anchor's offset to spatial coordinates a second time. Ordinary ghosts without an enabled `Spatial` retain their existing positioning behavior. Network scenery that should move with the reference should use spatial projection too; a local cockpit can remain fixed in the Unity scene.

Disabling `Spatial` stops projection and restores renderers and colliders that spatial culling had disabled. Setting `Realm.ReferenceFrame` to null releases projection and restores that presentation on the next realm update; requested views resume through the usual refresh phase. Roots keep their last projected world pose in either case. Emas does not restore a previous transform pose; application positioning can take over from the current pose.

Only one system should write a participating root's position and published rotation. Remove old root-position behaviors such as the example's `ApplyPosition` from spatial ghost prefabs. Dynamic Rigidbody simulation or other transform writers require an application-specific integration; spatial projection directly places the root.

## Limit distant presentation

`MaxDistance` is an optional positive distance in simulation units, measured in doubles from the reference. Null disables the configured range limit. Select a range appropriate for your visual scale; relative coordinates far from the reference still have the precision limits of Unity floats.

A spatial ghost without a position, without an initialized reference, or outside the presentation range keeps its tracking identity and data. Its requested view is suppressed; entering range creates the requested view automatically. `Spatial.IsInRange` describes its latest projection result. Root rendering and colliders are also suppressed outside the range while root scripts remain active. This is a presentation limit, not entity removal or a query-availability filter.

`Demanifest` still cancels the view request. Source failure, explicit removal and inactivity expiry still remove the ghost under the ordinary tracking rules.

## Reference loss and conversion helpers

A new frame has no usable position until `Position` is assigned or the followed ghost supplies one. `HasPosition` describes that cached state. If a followed ghost disappears, becomes unavailable, disables its spatial component or lacks position data, `IsReferenceAvailable` becomes false and the last valid reference pose is retained. Existing entities continue to project in that frozen frame. A returning identity resumes following on the next update. With no valid reference yet, spatial presentation stays suppressed.

Application consumers can use the same conversion and distance rules:

| Method | Use |
| --- | --- |
| `TryToUnityPosition(position, out unityPosition)` | Project a double position when the reference is initialized and the position is within its presentation range |
| `ToSimulationPosition(unityPosition)` | Convert a Unity world position back into the frame's simulation coordinates |
| `ToUnityRotation(rotation)` / `ToSimulationRotation(rotation)` | Convert orientations using the frame's active rotation mapping |
| `DistanceTo(position)` | Compute simulation distance from the cached reference in doubles |

Check `HasPosition` before inverse-position, rotation-conversion or reference-distance operations when following a ghost that has not published yet. `TryToUnityPosition` returns false while no reference exists or a result cannot fit in finite Unity floats. Use `Double3.Distance(a, b)` for distances between simulation entities independently of a reference frame. Followed state is resolved during realm updates, so conversion helpers use the latest resolved reference.

## Run the example

Import the separate **Relative world** sample, open `RelativeWorld.unity`, and press Play. Its component creates an isolated realm, camera and simple car views. The green ego car stays at the Unity origin while orange traffic moves inversely. One distant car's view appears as it enters the configured 45-metre range. Network traffic positions are published once, while the ego car keeps publishing independent position and orientation updates.

Read [RelativeWorld.cs](../Samples~/RelativeWorld/RelativeWorld.cs) for ownership and configuration, and [RelativeCarSource.cs](../Samples~/RelativeWorld/RelativeCarSource.cs) for double-precision mapping. Disable the component to release its realm and generated objects.
