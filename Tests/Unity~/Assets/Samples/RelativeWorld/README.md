# Relative world

Open `RelativeWorld.unity` and press Play. The green origin drives north at **8 m/s** (28.8 km/h), while the reference frame keeps it fixed in Unity. Orange parked cars appear ahead on alternating sides, pass the origin, then disappear behind it. Road markings scroll at the same speed to make the travel visible. A little white bird circles the origin at a height of 3.2 m, turning with its orbit; its orange feet are separate entities attached to its moving root.

## Inspect the setup

- **Tracking / Realm Setup** uses **Geographic** reference space and follows `relative-world / relative.car / origin`, with orientation following and a 45 m presentation range.
- **Geodetic Feed / Geo Source** simulates the SDK and detects arrivals and departures. **Geo Initializer** maps geographic readings to the traits saved on `Prefabs/RelativeCar.prefab`.
- **Environment / Road Motion** references that Realm Setup and the authored road markings. It scrolls them using the reference frame's actual northward travel, wrapping the repeating five-metre pattern.
- `Manifestations/RelativeBird.asset` maps `relative.bird` to the saved `BirdView.prefab` through its inline `bird` variant. It reuses the same spatial Ghost root and traits as the cars.
- `Manifestations/RelativeBirdFoot.asset` maps `relative.bird-foot` to `BirdFootView.prefab`. Its `RelativeBirdFoot.prefab` root adds `GeoAttachmentTrait` beside the absolute position and orientation traits.
- `Manifestations/RelativeCar.asset` selects the plain Ghost root, `Spatial`, position and orientation traits. Its inline `origin` and `target` variant rows select the green driving and orange parked views.

The road, markings, camera, light, tracking prefab and vehicle prefabs are saved assets or scene objects. No bootstrap creates the scene. Disable and re-enable Tracking to restart the drive.

## SDK and reference coordinates

For jittery SDK data, configure **Spatial > Position Smoothing Time** and **Rotation Smoothing Time** independently on the Ghost prefab, for example `0.1` seconds for position and `0` for rotation. Both default to zero. On the followed origin, leaving rotation smoothing at zero lets heading changes reposition other Ghosts immediately while position still smooths. An optional `GeoVelocityTrait` can be added beside `GeoPositionTrait`; `GeoInitializer` binds it only when present. Supply `GeoPoseReading.EarthCenteredVelocity` in ECEF XYZ metres per second, or leave it null. Velocity enables the position backward-motion guard; both smoothing channels also work without it. Update velocity when stopping or reversing, and call `Spatial.ResetSmoothing()` for teleports. See [spatial smoothing](../../Documentation~/Spatial.md#smooth-sdk-poses).

`SimulatedGeoSdk` supplies complete WGS84 snapshots: latitude/longitude in degrees, ellipsoidal altitude in metres and attitude in degrees. The origin advances along a level road. Parking bays are fixed 80 m apart, alternating left and right; the first is 24 m ahead. The SDK includes parked cars from 38 m ahead to 22 m behind the origin, leaving gaps between encounters. Each bay has a stable `parked-N` identity and a fixed geographic pose.

The bird is always present as `relative.bird / bird`. Its four-metre-radius orbit moves with the driving origin and takes eight seconds per lap. Its heading follows the relative orbit, with a constant 20-degree bank. Both position and orientation arrive as SDK readings and pass through the initializer and traits; the bird view contains only authored geometry.

## Attach and detach the bird's feet

While playing, select **Tracking / Geodetic Feed**, open the **Geo Source** component's context menu, and choose **Detach bird feet**. Both feet hold their release poses in the SDK's absolute world coordinates while the bird and driving reference continue moving. Choose **Attach bird feet** to return them to the bird. Code can call `geoSource.SetBirdFeetAttached(false)` or `true`; `Advance(seconds)` supports deterministic stepping of the same demonstration.

The two foot identities are `relative.bird-foot / bird-left-foot` and `bird-right-foot`. The SDK reports them before the bird on startup. Each foot's `GeoAttachmentTrait` binds to the current immutable SDK reading and calls `Spatial.Attach` with the bird's complete key, so the parent does not need to be discovered yet. The parts remain separate Ghost roots under the same Anchor.

The traits access their root through `Trait.Ghost`: the attachment trait reads `Ghost.Key.AnchorId` and resolves its `Spatial` with `Ghost.GetRequired<Spatial>()`. No trait needs to retrieve its Ghost with `GetComponent` or wait for an activation callback.

The SDK's `BodyOffset` uses metres in **X forward, Y right, Z down** axes. The trait converts it to Unity local axes with `(Y, -Z, X)`. Attached feet follow the bird's projected position and bank; absolute position and orientation traits continue caching a coherent release pose. When the SDK clears `ParentId`, the attachment trait calls `Detach` and those absolute channels take over. Reattaching retains the existing Ghosts and views. No part script needs its own transform update.

## Geographic projection

`GeoSource` refreshes membership from each snapshot and calls `Disappear` for missing IDs. Its `Advance(seconds)` method also supports deterministic stepping. The initializer binds readers through weak `Presence.Source`; the position trait consumes `GeoPosition`, while the orientation trait passes the SDK's yaw/pitch/roll degrees directly to `Spatial.SetGeographicRotation`. Position is stored as double-precision ECEF metres.

The attitude API accepts heading clockwise from true north, nose-up pitch, then right-wing-down roll. It performs the conversion into local east/up/north and sets `RotationSpace.Geographic`; no quaternion construction is needed in the initializer. **Source quaternion axes** only applies when using `SetSourceRotation`, so changing that setting does not reinterpret these named angles.

The sample's camera is fixed in the scene. With **Follow Rotation** enabled, the reference frame cancels the origin's full heading, pitch and roll for the surrounding positions and attitudes. For an aircraft cockpit using this setup, keep the camera fixed relative to the reference's desired Unity pose. Applying the SDK attitude to the camera as well adds another rotation. Following changes viewing direction while preserving the entities' 3D separation.

The followed car defines the local tangent frame directly. Emas subtracts Earth-centered positions in doubles and maps the displacement into the reference's current east/up/north axes. Each entity's local attitude is oriented using its own tangent frame. Parked cars retain fixed geographic positions while the reference moves.

Only the simulated SDK needs a road starting location: **52.520008 degrees N, 13.404954 degrees E, 40 m ellipsoidal height**. It generates nearby WGS84 readings for a level road and bird orbit using local curvature radii. This starting location is not a projection datum and is never configured on Realm Setup. Road Motion accumulates displacement between reference positions to scroll the repeating markings.

See `Documentation~/Spatial.md` for the coordinate and reference-frame contracts.
