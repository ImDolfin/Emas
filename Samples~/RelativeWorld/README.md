# Relative world

Open `RelativeWorld.unity` and press Play. The green origin drives north at **8 m/s** (28.8 km/h), while the reference frame keeps it fixed in Unity. Orange parked cars appear ahead on alternating sides, pass the origin, then disappear behind it. Road markings scroll at the same speed to make the travel visible. A little white bird circles the origin at a height of 3.2 m, turning with its orbit.

## Inspect the setup

- **Tracking / Realm Setup** follows `relative-world / relative.car / origin`, with orientation following and a 45 m presentation range.
- **Geodetic Feed / Geo Source** simulates the SDK and detects arrivals and departures. **Geo Initializer** maps geographic readings to the modules saved on `Prefabs/RelativeCar.prefab`.
- **Environment / Road Motion** references that Realm Setup and the authored road markings. It scrolls them using the reference frame's actual northward travel, wrapping the repeating five-metre pattern.
- `Manifestations/RelativeBird.asset` maps `relative.bird` to the saved `BirdView.prefab` through its inline `bird` variant. It reuses the same spatial Ghost root and modules as the cars.
- `Manifestations/RelativeCar.asset` selects the plain Ghost root, `Spatial`, position and orientation modules. Its inline `origin` and `target` variant rows select the green driving and orange parked views.

The road, markings, camera, light, tracking prefab and vehicle prefabs are saved assets or scene objects. No bootstrap creates the scene. Disable and re-enable Tracking to restart the drive.

## SDK and reference coordinates

`SimulatedGeoSdk` supplies complete WGS84 snapshots: latitude/longitude in degrees, ellipsoidal altitude in metres and attitude in degrees. The origin advances along a level road. Parking bays are fixed 80 m apart, alternating left and right; the first is 24 m ahead. The SDK includes parked cars from 38 m ahead to 22 m behind the origin, leaving gaps between encounters. Each bay has a stable `parked-N` identity and a fixed geographic pose.

The bird is always present as `relative.bird / bird`. Its four-metre-radius orbit moves with the driving origin and takes eight seconds per lap. Its heading follows the relative orbit, with a constant 20-degree bank. Both position and orientation arrive as SDK readings and pass through the initializer and modules; the bird view contains only authored geometry.

`GeoSource` refreshes membership from each snapshot and calls `Disappear` for missing IDs. Its `Advance(seconds)` method also supports deterministic stepping. The initializer binds readers through weak `Presence.Source`; modules consume only `Double3` positions and `Quaternion` rotations.

`GeoProjection` maps all observations to one fixed east/up/north tangent frame at **52.520008 degrees N, 13.404954 degrees E, 40 m**. The simulator uses the inverse conversion to generate geographic observations of a flat road. Parked cars remain stationary in that shared frame. Realm subtracts the moving reference in double precision and applies the relative Unity pose; the parked cars never orbit or bob around the origin.

See `Documentation~/Spatial.md` for the coordinate and reference-frame contracts.
