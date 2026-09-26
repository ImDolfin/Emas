# Relative world

Import **Relative world** from the Emas package, open `RelativeWorld.unity`, and press Play. The green origin car stays at Unity position zero while the orange target moves and turns relative to it. The scene, camera, light, materials, and prefab configuration are already authored and can be inspected before Play.

## Explore the setup

1. Select **Tracking** in the scene. Its **Realm Setup** owns the realm and assigns `Manifestations/RelativeCar.asset`. The reference frame follows anchor `relative-world`, kind `relative.car`, entity `origin`, with **Follow Rotation** enabled and a **Max Distance** of 45 metres.
2. Expand **Tracking / Geodetic Feed**. **Anchor Setup** has ID `relative-world` and **Automatic Views** enabled. **Geo Source** connects the simulated SDK and registers the two data-mapping modules.
3. Open `Manifestations/RelativeCar.asset`. Its Ghost Prefab is `Prefabs/RelativeCar.prefab`, containing **Relative Car** and **Spatial**. Its two variants point to `Origin.asset` and `Target.asset`.
4. Open either variant asset to inspect its **Full** detail mapping, then open `Prefabs/OriginView.prefab` or `Prefabs/TargetView.prefab` to edit the car geometry and assigned materials. Appearance is configured in assets, so changing a view requires no source-code changes.

`Prefabs/Tracking.prefab` contains the complete reusable tracking configuration. Place it in another scene with a camera and light to use the same detector, blueprint, and reference frame. Disable **Tracking** to release its realm and generated entity/view instances; the authored environment stays in place. Re-enable it to start a fresh simulation.

## SDK mapping

`SimulatedGeoSdk` supplies a complete snapshot containing `origin` and `target`. Each `GeoPoseReading` carries WGS84 latitude and longitude in degrees, ellipsoidal altitude in metres, and yaw, pitch, and roll. `GeoDetector` reports both readings in `OnStart` and advances the SDK before reporting each `OnUpdate` snapshot. These two sample entities are always present; an integration with departing entities should call `Disappear` for their IDs.

`GeoSource` implements `IDetectorProvider` and `IRealmConfigurator`. Its only setup code creates the detector and registers `GeoPositionModule` and `GeoOrientationModule` for the authored `RelativeCar` root. `RealmSetup` owns startup, updates, views, and cleanup. `GeoSource.Advance(seconds)` can advance the simulation explicitly; the next realm update applies the resulting snapshot.

The position module converts geodetic coordinates relative to the fixed datum **52.520008 degrees N, 13.404954 degrees E, 40 m** into double-precision ENU coordinates: `Double3.X` is east, `Y` is up, and `Z` is north. The orientation module maps SDK yaw clockwise from true north, pitch nose-up, and roll right-wing-down into the root's local **+Z forward, +X right, +Y up** convention. Both modules update `Spatial`; the detector handles identity and presence.

The reference frame subtracts the moving origin in doubles before converting to Unity floats. Following rotation keeps the origin's Unity heading fixed as well as its position, while the target inherits the corresponding relative position and orientation.

| Asset or file | Role |
| --- | --- |
| `Prefabs/Tracking.prefab` | Inspector-configured realm, followed origin, anchor, and source. |
| `Manifestations/RelativeCar.asset` | Connects the car kind, Ghost prefab, and appearance variants. |
| `Manifestations/Origin.asset`, `Target.asset` | Map each appearance to its car view prefab. |
| `Prefabs/RelativeCar.prefab` | Ghost root with `RelativeCar` and `Spatial`. |
| `Prefabs/OriginView.prefab`, `TargetView.prefab` | Editable car visuals with shared material assets. |
| `GeoSource.cs` | Provides the detector and registers data-mapping modules. |
| `GeoDetector.cs` | Reports the two SDK entities, names, and visual variants. |
| `SimulatedGeoSdk.cs`, `GeoPoseReading.cs` | Simulated geodetic snapshots and their payload. |
| `GeoPositionModule.cs`, `GeoOrientationModule.cs` | Map SDK pose data into `Spatial`. |

The sample has its own assembly and can be imported independently. For coordinate mapping, reference loss, and range behavior, see `Documentation~/Spatial.md` in the Emas package.