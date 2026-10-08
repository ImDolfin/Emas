# Relative worlds and large coordinates

Configure a reference frame for a realm to project shared Cartesian or geographic positions relative to a moving origin. Add `Spatial` to each participating Ghost root. Without a reference frame, enabled `Spatial` components use an identity frame: Cartesian poses map directly to Unity world space, with no distance limit. Geographic and ECEF attitudes require a Geographic reference. Ghosts without an enabled `Spatial` remain application-positioned.

## Choose the input contract

| Spatial input | Meaning | Reference space |
| --- | --- | --- |
| `SetCartesianPosition(Double3)` | Shared source XYZ in the world's units and `Coordinates` axes | Cartesian or no reference |
| `SetGeographicPosition(GeoPosition)` | Absolute WGS84 latitude/longitude degrees and ellipsoidal height metres | Geographic |
| `SetEarthCenteredPosition(Double3)` | Absolute ECEF XYZ metres | Geographic |
| `SetSourceRotation(Quaternion)` | Quaternion in the frame's selected source axes; local tangent attitude in Geographic space | Either |
| `SetGeographicRotation(yawDegrees, pitchDegrees, rollDegrees)` | Heading clockwise from true north, nose-up pitch, right-wing-down roll | Geographic |
| `SetEarthCenteredRotation(Quaternion, bodyAxes)` | Active body-to-ECEF quaternion and right-handed source body axes | Geographic |

Position and attitude arrive independently. Setters store data; the Realm applies it after all readers run. `Position` contains source Cartesian coordinates or ECEF metres, never latitude/longitude. `Rotation` contains a normalized quaternion; `RotationSpace` identifies `Source`, `Geographic` (east/up/north), or `EarthCentered`. Read them after `HasPosition` or `HasRotation` becomes true. Each rotation setter replaces the previous representation.

## Smooth SDK poses

On the Ghost's **Spatial** component, set **Smoothing Time** to a positive time constant in seconds; start around `0.1` and adjust for your SDK. The default `0` keeps direct pose application. Position uses exponential smoothing in double-precision source coordinates and rotation follows the shortest quaternion arc. Larger values reduce jitter and increase lag. Smoothing uses Unity's unscaled clock, advances on realm projection even between SDK packets, and does not extrapolate beyond the latest position.

```csharp
Spatial spatial = ghost.GetRequired<Spatial>();
spatial.SmoothingTime = 0.1f;
```

`Position` and `Rotation` continue to expose the latest raw input. The smoothed pose supplies both the root and a following reference frame, so the followed entity stays at the configured Unity pose. Changes to reference placement and coordinate conversion apply immediately. Attached parts inherit their parent's projected pose without a second smoothing pass. Source range checks still suppress an entity immediately when its latest input is outside `MaxDistance`; returning to range or re-enabling Spatial starts with a fresh pose.

Velocity is optional. Without it, position and rotation still smooth normally. If your SDK supplies velocity, bind it through a separate `Trait`, just like the position channel. Use `SetCartesianVelocity(Double3)` for shared source axes and units per second, or `SetEarthCenteredVelocity(Double3)` for ECEF XYZ metres per second in Geographic space. **Minimum Forward Speed** (default `0.1`) sets the speed at which the guard rejects position corrections opposite the supplied velocity; perpendicular corrections continue to smooth. It follows the velocity direction, independently of the model's heading.

The Relative World sample includes an optional `GeoVelocityTrait : Trait<Double3?>`. Add it to a geographic Ghost prefab when your SDK supports velocity. Its reader returns ECEF metres per second or `null` when velocity is unavailable:

```csharp
GeoVelocityTrait velocity;
if (root.TryGet<GeoVelocityTrait>(out velocity))
{
    velocity.Bind(() => ReadOptionalEcefVelocity(presence)); // Application SDK mapping: Double3?
}
```

`GeoInitializer` demonstrates the optional binding using `GeoPoseReading.EarthCenteredVelocity`; existing sample prefabs and mock readings do not require velocity. Convert body or local tangent SDK velocity into ECEF before supplying it. A Cartesian trait can use the same nullable pattern and call `SetCartesianVelocity` instead.

Publish current velocity whenever the entity stops, turns or reverses. Zero or a speed below `MinimumForwardSpeed` permits corrections in any direction; `ClearVelocity()` releases the guard if the SDK stops supplying velocity. The guard retains its last velocity until updated or cleared and does not predict travel. For a teleport or intentional discontinuity, call `ResetSmoothing()` after supplying the new pose; the next projection snaps to that input while retaining smoothing settings and channels. Changes between source, geographic and ECEF attitude representations restart rotation smoothing rather than blending incompatible quaternions.

## Attach and detach entities

Use `Spatial.Attach` for a part whose presentation should follow another entity instead of independently timed absolute SDK packets. Attach by a complete `Key` in the same Realm; the parent may be discovered after its parts, including on another Anchor or detector:

```csharp
Spatial part = partGhost.GetComponent<Spatial>();
Key vehicle = new Key("vehicles", vehicleKind, vehicleId);
part.Attach(vehicle, new Vector3(1, 0, 2), Quaternion.identity);

// Continue supplying absolute SDK positions and attitudes while attached.
// Once those channels describe the release pose, resume absolute placement:
part.Detach();
```

The position offset uses **Unity local axes and units**: X right, Y up and Z forward. The relative rotation is a Unity quaternion. Source coordinate presets do not reinterpret these offsets. For your SDK's X-forward/Y-right/Z-down position offset, pass `new Vector3(sourceY, -sourceZ, sourceX)`. Parent and Anchor scale do not scale the attachment offset. The overload without a rotation uses identity; call `Attach` again to change the target or local pose.

Keep `Spatial` enabled while attached. Each Realm projection resolves parents before parts, including attachment chains, then combines the parent's projected world pose with the local offset. Ghost roots stay under their Anchors and retain independent identities and lifetimes. This works with Cartesian and Geographic reference frames and full reference-attitude cancellation. The parent can also be application-positioned without an enabled `Spatial`.

`AttachedTo` reports the requested parent even while it is missing. Until that parent is available and presentable, the part remains tracked and queryable but its views, root renderers and colliders are suppressed. A parent without a usable spatial position, an out-of-range parent, or an attachment cycle also suppresses dependent parts. Discovery, rediscovery or breaking the cycle resolves the attachment automatically. An attached part's resulting position must also fit the reference's presentation range.

Attachment does not require an absolute part position or orientation. The absolute setters continue caching their channels; `Position`, `Rotation`, `RotationSpace`, `HasPosition` and `HasRotation` describe that cached input. `Detach` clears the attachment and the next projection uses those channels. It is safe before parent discovery or when already detached. A missing absolute position keeps presentation suppressed; a stale absolute pose can cause a jump, so publish a current release pose before detaching. Reference following continues to consume the followed Ghost's absolute input; follow the vehicle supplying that input when attaching its parts.

## Configure a prefab realm

Add **Emas > Realm Setup** to the prefab root. Its **Manifestation Blueprints** list maps each Kind to its Ghost and views for every anchor in that realm. Add one **Emas > Anchor Setup** for each anchor frame, on the root or a child object. Each Anchor Setup needs a unique **Anchor ID**, a `PresenceDetectorComponent` subclass and optionally a `GhostInitializer` on the same object. The initializer binds the spatial traits authored on the Ghost prefab. Each blueprint covers one Kind and stores a table of named variants, each selecting one view prefab. For example:

```text
Screen (RealmSetup: manifestation blueprints and reference frame)
  Vehicles (AnchorSetup: id vehicles; CarDetector; CarInitializer)
  Signs (AnchorSetup: id signs; SignDetector; SignInitializer)
Environment (another RealmSetup with its own anchors and reference frame)
```

For a car that stays near the Unity origin, enable **Use Reference Frame**, choose the **Coordinate System** matching the incoming poses, and enable **Follow Ghost** on the screen's Realm Setup. If its identity is known in advance, enter the car's entity ID (`my-car`), anchor ID (`vehicles`) and kind ID. Leave **Entity ID** empty when the target is chosen at runtime; the Anchor and Kind fields are then unused. Set **Unity Position** to `(0, 0, 0)`, **Unity Rotation** to identity and **Follow Rotation** as needed. To hide distant views, enable **Limit Distance** and enter a positive **Max Distance** in the shared coordinate units. The followed ghost must be in this same realm and have an enabled `Spatial` component with a published position.

For a fixed origin, leave **Follow Ghost** off. In Cartesian space, enter its shared **Position** as doubles and its **Rotation**. In Geographic space, enter WGS84 latitude, longitude and ellipsoidal height, plus local attitude. Each prefab instance creates its own realm and frame on the first update after enable. Read the live frame through `realmSetup.Realm.ReferenceFrame`. Disabling the setup disposes that realm. [Getting started](GettingStarted.md) shows the complete component wiring.

## Assign a reference at runtime

Enable **Use Reference Frame** and **Follow a Ghost**, and leave **Entity ID (optional)** empty. Realm Setup starts with an uninitialized reference while retaining your coordinate system, Unity placement, rotation-following and distance settings. Ghosts remain tracked and queryable, but spatial views stay suppressed until a reference position is available.

After Realm Setup starts, assign the selected Ghost's key:

```csharp
realmSetup.Realm.ReferenceFrame.FollowedGhost = ghost.Key;
```

If your SDK supplies only the identity, construct the full key instead:

```csharp
realmSetup.Realm.ReferenceFrame.FollowedGhost =
    new Key(anchorId, kind, runtimeEntityId);
```

The key includes the Anchor and Kind as well as the entity ID. The Ghost can arrive later; the frame begins following on the update that resolves its first valid `Spatial` position. Assign a different key to switch targets without replacing the frame or its settings. If a previous reference position exists, it is retained while waiting for the new target.

`RealmSetup` normally starts on its first update after enable. Call `StartRealm()` explicitly if you need to assign the key earlier. Stopping and restarting the setup creates a fresh frame, so assign the runtime target for each new Realm. A nonempty Entity ID in the Inspector still requires an Anchor ID and Kind.

## Follow a geographic reference directly

Choose **Reference space > Geographic** in Realm Setup, or set `ReferenceFrame.Space = ReferenceSpace.Geographic`. Feed WGS84 positions directly:

```csharp
spatial.SetGeographicPosition(new GeoPosition(latitudeDegrees, longitudeDegrees, heightMeters));

realm.ReferenceFrame = new ReferenceFrame
{
    Space = ReferenceSpace.Geographic,
    FollowedGhost = referenceGhost.Key,
    FollowRotation = false,
    MaxDistance = 5000
};
```

`GeoPosition` uses ellipsoidal height in metres. Convert mean-sea-level height with your application's geoid model when needed. Latitude must be within [-90, 90] degrees and longitude within [-180, 180]. `SetGeographicPosition` converts to Earth-centered, Earth-fixed (ECEF) metres, retained in `Spatial.Position`. Sources already providing ECEF use `SetEarthCenteredPosition`. All spatial positions in a Geographic realm are stored as ECEF metres.

To present one entity relative to another, detect both in the same Realm and give both Ghost roots an enabled `Spatial`. Their anchors and detectors can differ. The reference frame belongs to the Realm; `FollowedGhost` selects the entity supplying its origin, and every participating entity projects through that frame. Each entity still supplies its absolute WGS84 reading. Do not subtract latitude, longitude or altitude in the SDK reader.

Bind an `Trait<GeoPosition>` on each Ghost to its SDK reading; its `Apply` calls `SetGeographicPosition`. The [README integration](../README.md#2-define-reusable-traits-on-the-ghost) shows the complete detector, trait and initializer setup. After the traits run, the Realm resolves the followed entity and projects both roots in the same update. Updating either SDK reading needs no new detection call. Isolated Realms need explicit `Update` calls; `Realm.Default` and `RealmSetup` update automatically. Request the target's view with `Manifest`, or use Anchor Setup's automatic views.

For example, with reference `(52.520008, 13.404954, 40.125)` and target `(52.520108, 13.405154, 50.125)` in latitude degrees, longitude degrees and ellipsoidal metres, identity Unity placement and `FollowRotation = false` put the reference at `(0, 0, 0)` and the target at approximately `(13.576, 10.000, 11.128)` Unity metres. X is east, Y is up and Z is north at the reference. Moving the reference recomputes the target's Unity position while its stored ECEF position stays unchanged.

On each update, the reference position defines the local east/north/up tangent axes. The projection subtracts ECEF positions in doubles, then rotates that displacement into the reference's tangent frame and applies Unity placement. Stationary entities retain their global positions while their projected positions change with the reference. No static ENU origin is configured. This uses the WGS84 [geodetic conversion](https://gssc.esa.int/navipedia/index.php/Ellipsoidal_and_Cartesian_Coordinates_Conversion) and [local tangent transformation](https://gssc.esa.int/navipedia/index.php/Transformations_between_ECEF_and_ENU_coordinates).

For SDK angles matching the sample, feed attitude directly:

```csharp
spatial.SetGeographicRotation(yawDegrees, pitchDegrees, rollDegrees);
```

All three angles use degrees. Zero faces true north with the model's +Z forward and +Y up. The intrinsic sequence is heading about local up, nose-up pitch about the resulting body right, then right-wing-down bank about the resulting body forward. Angles wrap modulo 360 before float conversion; nonfinite input throws without replacing the last attitude. This is equivalent to `yaw * pitch * roll` in Unity east/up/north axes, with positive yaw and negative pitch/roll `AngleAxis` angles. It follows the entity's own tangent plane as its position changes.

These named angles have the same physical meaning with Unity, ENU, NED or Custom selected. Emas stores their converted quaternion as `RotationSpace.Geographic`; changing `Coordinates` does not reinterpret it. For a manual geographic reference, use `frame.SetGeographicRotation(yawDegrees, pitchDegrees, rollDegrees)` with the same convention. Convert SDK radians or different angle signs/order into this documented convention before calling it.

`Coordinates`, shown as **Source quaternion axes** in Geographic mode, describes raw quaternions supplied to `Spatial.SetSourceRotation`: Unity means local east/up/north, ENU means east/north/up, and NED means north/east/down. Emas accounts for each entity's own tangent plane before applying the reference alignment. SDK quaternion ordering, active/passive representation and body-axis conventions remain application responsibilities. An already converted Unity quaternion uses Unity source axes.

`FollowRotation = false` keeps local north/up aligned to the scene. True cancels the reference's full heading, pitch and roll for both positions and attitudes, preserving 3D separation while changing the arrangement relative to the viewer. For a cockpit view using this cancellation, keep the camera fixed relative to the reference's desired Unity pose; assigning the SDK attitude to that camera applies another rotation. Alternatively, follow position only and orient the camera yourself.

For a fixed geographic reference, assign `frame.GeographicPosition = new GeoPosition(...)` after selecting Geographic space. `frame.Position` exposes the same point in ECEF metres. A followed reference can also be assigned later, using the runtime-selection workflow above. Loss freezes both the last reference position and its tangent axes.

Use `TryToUnityPosition(GeoPosition, out Vector3)` and `ToGeographicPosition(Vector3)` for geographic point conversion. For an entity's attitude, use `ToUnityRotation(rotation, geoPosition)` or `ToSimulationRotation(unityRotation, geoPosition)`; the overloads without a position use the reference location. `ToSimulationPosition` continues to return the stored Cartesian coordinates, which are ECEF in this mode. `GeoPosition.ToEarthCentered()` and `GeoPosition.FromEarthCentered()` convert independently of a Realm.

`MaxDistance` and `DistanceTo` measure straight-line ECEF distance in metres, not surface travel distance. Projection retains Earth curvature; equal-height objects far away need not have the same Unity Y. At an exact ECEF pole (X = Y = 0), longitude is taken as zero to define the tangent axes. Geographic projection requires a reference; removing it restores ordinary identity projection of the stored ECEF numbers.

## Feed ECEF positions and attitudes

Use the same **Geographic** reference space for an SDK that already supplies Earth-centered, Earth-fixed coordinates. Positions bypass WGS84 conversion, and ECEF attitudes have an explicit input method:

```csharp
spatial.SetEarthCenteredPosition(new Double3(ecefX, ecefY, ecefZ));
spatial.SetEarthCenteredRotation(bodyToEcefRotation);
```

The quaternion must actively rotate source body XYZ vectors into ECEF XYZ. The default body convention is **X forward, Y right, Z down**. For other right-handed body axes, pass their mapping to Unity model right/up/forward:

```csharp
// SDK body axes: X forward, Y left, Z up.
CoordinateSystem bodyAxes = new CoordinateSystem(
    Axis.NegativeY, Axis.PositiveZ, Axis.PositiveX);
spatial.SetEarthCenteredRotation(bodyToEcefRotation, bodyAxes);
```

`CoordinateSystem.NorthEastDown` describes the default body mapping; `EastNorthUp` describes X right, Y forward, Z up. This parameter concerns the body's axes, not geographic directions at its current location. Decode SDK quaternion ordering and invert passive ECEF-to-body attitudes in the application as needed. ECEF attitudes require right-handed body axes; a mapping that cannot represent a proper body-to-ECEF rotation is rejected.

Position and attitude can arrive independently, in either order. Emas retains the global quaternion, so moving an entity without another attitude update does not silently change its global orientation. Source, named geographic and ECEF attitudes can coexist in the same Realm. `RotationSpace` identifies the stored quaternion's basis. Calling `SetSourceRotation` switches that Ghost back to the local/source convention selected by the frame's **Source quaternion axes** setting.

Follow the Ghost's key normally: the frame inherits its attitude representation. For a manual reference, assign its ECEF `Position` and call `frame.SetEarthCenteredRotation(...)`. `ToUnityEarthCenteredRotation(...)` and `ToEarthCenteredRotation(...)` convert attitudes in both directions and take the same optional body-axis mapping. These inputs still display around the reference's moving tangent frame.

## Choose source coordinates

Each reference frame has a `Coordinates` value. Choose its preset in Realm Setup or assign it in code. Unity is the default; no extra asset is needed.

| Preset | Source X | Source Y | Source Z | Unity right / up / forward |
| --- | --- | --- | --- | --- |
| `CoordinateSystem.Unity` | Right | Up | Forward | +X / +Y / +Z |
| `CoordinateSystem.EastNorthUp` | East | North | Up | +X / +Z / +Y |
| `CoordinateSystem.NorthEastDown` | North | East | Down | +Y / -Z / +X |

ENU and NED both place east along Unity right and north along Unity forward. For another convention, select **Custom** and choose the signed source axis mapped to each Unity direction. Each of X, Y and Z must appear exactly once. The mapping determines handedness automatically.

```csharp
frame.Coordinates = CoordinateSystem.NorthEastDown;

// Custom source: X left, Y forward, Z up.
frame.Coordinates = new CoordinateSystem(
    right: Axis.NegativeX, up: Axis.PositiveZ, forward: Axis.PositiveY);
```

In Cartesian space, the setting applies to `Spatial` positions and source quaternion rotations, the reference pose, and both directions of the conversion helpers. In Geographic space it applies to source quaternions; WGS84/ECEF positions and named yaw/pitch/roll have fixed geographic meanings. Source quaternion inputs in a realm must use the selected convention. Explicit ECEF attitudes use their own body-axis mapping. Rotation conversion helpers take source quaternions unless explicitly named EarthCentered. `UnityPosition` and `UnityRotation` remain in Unity coordinates. Reference following and the desired Unity pose are configured independently of the source axes.

Rotations are converted by changing both the world and local basis of their rotation matrix, including handedness. Feed quaternions expressed in that source basis; decoding an SDK's angle order, angular units or quaternion representation belongs in the application. Prefab geometry uses Unity local axes.

Changing `Coordinates` at runtime reinterprets stored source poses on the next realm update, retaining Ghosts and views. Named geographic angles and native ECEF attitudes retain their physical meaning. Conversion helpers use the new setting immediately. It does not rewrite the stored source data.

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

## Apply spatial channels through Ghost traits

Configure independent position and rotation traits on the Ghost prefab, or require them on its class:

```csharp
[RequireComponent(typeof(Spatial), typeof(PositionTrait), typeof(RotationTrait))]
public sealed class Car : Ghost
{
}

public sealed class PositionTrait : Trait<Double3>
{
    public override void Apply(Double3 position)
    {
        Ghost.GetRequired<Spatial>().SetCartesianPosition(position);
    }
}

public sealed class RotationTrait : Trait<Quaternion>
{
    public override void Apply(Quaternion rotation)
    {
        Ghost.GetRequired<Spatial>().SetSourceRotation(rotation);
    }
}
```

The initializer adapts your SDK to those reusable traits. Here the SDK's axes already match the shared frame:

```csharp
realm.RegisterPresenceInitializer<Car>(CarKind, (presence, car) =>
{
    Spatial spatial = car.GetComponent<Spatial>();
    car.GetRequired<PositionTrait>().Bind(() => presence.Source is SdkProxy proxy
        ? new Double3(proxy.X, proxy.Y, proxy.Z) : spatial.Position);
    car.GetRequired<RotationTrait>().Bind(() =>
        (presence.Source as SdkProxy)?.Rotation ?? spatial.Rotation);
});
```

`SdkProxy` is your SDK's concrete proxy type, supplied once with `Detect(id, CarKind, source: proxy)`. `Presence.Source` resolves a weak reference; these readers preserve the last spatial state if it is collected or destroyed. Use `ReferenceFrame.Coordinates` for the common axis and handedness conversion. For WGS84 feeds, use Geographic space and `SetGeographicPosition` as described above. Other datum conversions, unit conversion and SDK rotation decoding belong in these readers or an application helper. If sources use different conventions, first normalize them to the convention selected for this realm. Each trait only knows its input type. Bind only channels supplied by the SDK; unbound or disabled traits leave their channel unchanged. `Spatial.HasPosition` and `HasRotation` indicate whether each channel has been supplied. Missing rotation leaves root rotation under application control.

The detector calls `Detect(id, CarKind, source: proxy)` on arrival and `Disappear(CarKind, id)` on departure. Data updates require neither another detection nor a report. Each realm update reads and applies the enabled traits, resolves the reference once, and projects all roots using the resulting spatial state. Reference movement also repositions entities whose shared positions stayed unchanged. Sample reference and target data at a common presentation time for interpolated feeds.

Trait reads do not refresh `InactivityTimeout`. Leave it disabled for feeds that only announce arrivals and departures. A presence feed using expiry must independently confirm continued presence with Detect.

## Preserve precision before Unity

Store and transport global positions as `Double3`, which has three `double` components. Use one shared Cartesian coordinate system and unit for the realm, with its axes described by `ReferenceFrame.Coordinates`. Different detectors must feed the same system. Geographic space standardizes on ECEF metres and handles WGS84 conversion; other coordinate datums must be normalized by the application.

For Cartesian space, the relative displacement is calculated in double precision before its final conversion to a Unity position:

```text
Unity position = Unity reference position
               + Unity reference rotation
               * inverse(converted reference rotation)
               * coordinate basis
               * (entity position - reference position)
```

The coordinate basis maps source axes to Unity axes. A source rotation matrix `R` becomes `B * R * inverse(B)`, where `B` is that basis. With position-only following, the inverse converted reference rotation is omitted. Absolute positions around one billion metres can therefore yield nearby Unity positions such as `20.25 m` without first rounding the global values into floats. Converting an already-rounded global `Vector3` to `Double3` cannot recover precision.

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
| `ToUnityRotation(rotation)` / `ToSimulationRotation(rotation)` | Convert orientations using the selected coordinates and reference alignment |
| `DistanceTo(position)` | Compute distance from the cached reference in doubles |

Check `HasPosition` before inverse-position, rotation-conversion or reference-distance operations when following a ghost that has not published yet. `TryToUnityPosition` returns false while no reference exists or a result cannot fit in finite Unity floats. Use `Double3.Distance(a, b)` for distances between positions in the shared coordinate system independently of a reference frame. Followed state is resolved during realm updates, so conversion helpers use the latest resolved reference.

## Run the example

Import **Relative world**, open `RelativeWorld.unity`, and press Play. Its authored tracking prefab follows the green origin car driving north at 8 m/s. Stationary orange parked cars appear ahead on alternating sides, pass the origin and leave the SDK snapshot behind it. The road markings scroll using the reference frame's actual northward displacement. A white bird circles above the origin, updating both its relative position and heading through the same spatial traits. Its orange feet are separately tracked entities driven by `GeoAttachmentTrait`, converting SDK body offsets and resolving the bird by key. In the Geo Source component's context menu, choose **Detach bird feet** to hold their absolute release poses, then **Attach bird feet** to restore their local offsets; code can call `SetBirdFeetAttached(bool)`.

`GeoSource` detects the current snapshot and explicitly removes missing IDs. `GeoInitializer` supplies `GeoPosition` readings and local attitude to the authored traits. Realm Setup uses Geographic space: the driving car defines the tangent frame directly. A parked car's ECEF position remains constant even though its Unity position moves past the origin. The mock SDK has a starting road location, which is unrelated to reference configuration.

Inspect Realm Setup, the road's `RoadMotion` component and the saved blueprints and variants to change the scene configuration. See the [sample guide](../Samples~/RelativeWorld/README.md) for timings, units and the SDK mapping. Disable and re-enable Tracking to restart.
