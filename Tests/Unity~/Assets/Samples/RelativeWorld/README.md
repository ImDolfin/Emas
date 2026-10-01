# Relative world

Open `RelativeWorld.unity` and press Play. The green origin drives north at **8 m/s** (28.8 km/h), while the reference frame keeps it fixed in Unity. Orange parked cars appear ahead on alternating sides, pass the origin, then disappear behind it. Road markings scroll at the same speed to make the travel visible. A little white bird circles the origin at a height of 3.2 m, turning with its orbit.

## Inspect the setup

- **Tracking / Realm Setup** uses **Geographic** reference space and follows `relative-world / relative.car / origin`, with orientation following and a 45 m presentation range.
- **Geodetic Feed / Geo Source** simulates the SDK and detects arrivals and departures. **Geo Initializer** maps geographic readings to the modules saved on `Prefabs/RelativeCar.prefab`.
- **Environment / Road Motion** references that Realm Setup and the authored road markings. It scrolls them using the reference frame's actual northward travel, wrapping the repeating five-metre pattern.
- `Manifestations/RelativeBird.asset` maps `relative.bird` to the saved `BirdView.prefab` through its inline `bird` variant. It reuses the same spatial Ghost root and modules as the cars.
- `Manifestations/RelativeCar.asset` selects the plain Ghost root, `Spatial`, position and orientation modules. Its inline `origin` and `target` variant rows select the green driving and orange parked views.

The road, markings, camera, light, tracking prefab and vehicle prefabs are saved assets or scene objects. No bootstrap creates the scene. Disable and re-enable Tracking to restart the drive.

## SDK and reference coordinates

`SimulatedGeoSdk` supplies complete WGS84 snapshots: latitude/longitude in degrees, ellipsoidal altitude in metres and attitude in degrees. The origin advances along a level road. Parking bays are fixed 80 m apart, alternating left and right; the first is 24 m ahead. The SDK includes parked cars from 38 m ahead to 22 m behind the origin, leaving gaps between encounters. Each bay has a stable `parked-N` identity and a fixed geographic pose.

The bird is always present as `relative.bird / bird`. Its four-metre-radius orbit moves with the driving origin and takes eight seconds per lap. Its heading follows the relative orbit, with a constant 20-degree bank. Both position and orientation arrive as SDK readings and pass through the initializer and modules; the bird view contains only authored geometry.

`GeoSource` refreshes membership from each snapshot and calls `Disappear` for missing IDs. Its `Advance(seconds)` method also supports deterministic stepping. The initializer binds readers through weak `Presence.Source`; the position module consumes `GeoPosition`, while the orientation module passes the SDK's yaw/pitch/roll degrees directly to `Spatial.SetGeographicRotation`. Position is stored as double-precision ECEF metres.

The attitude API accepts heading clockwise from true north, nose-up pitch, then right-wing-down roll. It performs the conversion into local east/up/north and sets `RotationSpace.Geographic`; no quaternion construction is needed in the initializer. **Source quaternion axes** only applies when using `SetSourceRotation`, so changing that setting does not reinterpret these named angles.

The sample's camera is fixed in the scene. With **Follow Rotation** enabled, the reference frame cancels the origin's full heading, pitch and roll for the surrounding positions and attitudes. For an aircraft cockpit using this setup, keep the camera fixed relative to the reference's desired Unity pose. Applying the SDK attitude to the camera as well adds another rotation. Following changes viewing direction while preserving the entities' 3D separation.

The followed car defines the local tangent frame directly. Emas subtracts Earth-centered positions in doubles and maps the displacement into the reference's current east/up/north axes. Each entity's local attitude is oriented using its own tangent frame. Parked cars retain fixed geographic positions while the reference moves.

Only the simulated SDK needs a road starting location: **52.520008 degrees N, 13.404954 degrees E, 40 m ellipsoidal height**. It generates nearby WGS84 readings for a level road and bird orbit using local curvature radii. This starting location is not a projection datum and is never configured on Realm Setup. Road Motion accumulates displacement between reference positions to scroll the repeating markings.

See `Documentation~/Spatial.md` for the coordinate and reference-frame contracts.
