# Relative world

Import **Relative world** from the Emas package, open `RelativeWorld.unity`, and press Play. The scene has one **Relative World** component; it creates an isolated realm, camera, ground, and simple car manifestations.

`SimulatedGeoSdk` supplies a complete snapshot with an `origin` and a `target` on each update. Each `GeoPoseReading` contains WGS84 latitude and longitude in degrees, ellipsoidal altitude in metres, and yaw, pitch, and roll. The one-generic `PollingPresenceDetector<GeoPoseReading>` identifies and forwards the readings without changing Ghosts.

The Realm initializes a `RelativeCar` root for each Presence and adds `GeoPositionModule` and `GeoOrientationModule`. The position module converts raw geodetic coordinates relative to the fixed datum **52.520008° N, 13.404954° E, 40 m** into double-precision ENU coordinates: `Double3.X` is east, `Y` is up, and `Z` is north. The orientation module maps SDK yaw clockwise from true north, pitch nose-up, and roll right-wing-down into the root's local **+Z forward, +X right, +Y up** convention. Both modules update `Spatial`; the detector handles identity and presence only.

`ReferenceFrame` follows the `origin` Presence with `FollowRotation = true`. The green origin appears at Unity position zero with identity rotation while the orange target moves and turns relative to it. Projection subtracts the shared reference in doubles before converting to Unity floats.

| File | Role |
| --- | --- |
| `RelativeWorld.cs` | Owns the realm, registers the initializer and blueprint, configures the origin-following `ReferenceFrame`, and updates the SDK and realm. |
| `RelativeCar.cs` | Provides the Ghost root and requires `Spatial`. |
| `GeoPoseReading.cs` | Carries one raw SDK geodetic pose and stable ID. |
| `SimulatedGeoSdk.cs` | Returns complete snapshots containing the origin and target. |
| `GeoPositionModule.cs` | Converts WGS84 latitude, longitude, and altitude into fixed-datum ENU `Double3` positions. |
| `GeoOrientationModule.cs` | Converts SDK yaw, pitch, and roll into a `Spatial` rotation. |

Disable the **Relative World** object to release its realm and generated scene objects. The sample has its own assembly and can be imported without **Emas sample**. For coordinate mapping, reference loss, and range behavior, see `Documentation~/Spatial.md` in the Emas package.
