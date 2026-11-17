# Relative world

Open `RelativeWorld.unity` and press Play. The green reference car drives north at **100 m/s (360 km/h)** while remaining fixed in Unity. Orange parked cars pass it, road markings scroll, and a white bird circles overhead. Its orange feet are separate tracked entities attached to the bird.

## Inspect the setup

| Asset/component | Purpose |
| --- | --- |
| **Tracking / Realm Setup** | Geographic projection following `relative-world / relative.car / origin`; orientation following and a 45 m range. |
| **Geodetic Feed / Geo Source** | Simulated SDK membership and immutable observations. Geo Initializer binds the authored traits. |
| `Prefabs/RelativeCar.prefab` | Spatial input/motion readers, Extrapolation and Smoothing; shared by cars and bird. |
| `Manifestations/RelativeCar.asset` | `origin` / `target` variants select green / orange views. Bird and foot blueprints select their own views. |
| `RelativeBirdFoot.prefab` | Adds attachment handling while retaining absolute pose/motion readers. |
| **Environment / Road Motion** | Scrolls the saved five-metre marking pattern using actual reference travel. |

The scene, tracking, roots and views are authored assets. Disable/re-enable Tracking to restart. Restarting Geo Source also resets this sample's shared SDK clock.

## Try noisy and delayed packets

Select **Tracking / Geodetic Feed / Geo Source**. Defaults:

| Setting | Value |
| --- | --- |
| Speed Kilometers Per Hour | 360 km/h; zero stops forward travel. |
| Source Packets Per Second | 60 Hz. |
| Packet Delay / Packet Delay Jitter | 80 ms / +/-40 ms. |
| Position Jitter | Up to 2 m horizontal error per observation. |
| Yaw Jitter | Up to +/-1 degree. |
| Realm Interpolation Delay | 0.1 s, shared by reference and peers. |
| Ghost extrapolation horizon | 0.05 s beyond recorded observations. |
| Position / rotation half-lives | 0.08 s / 0.04 s. |

Disable **Simulate Jitter And Delay** for clean immediate input. To isolate transport effects, leave it enabled and set measurement jitter to zero. Compare shared buffering with `realm.InterpolationDelay = 0`, then tune Extrapolation and Smoothing under **Ghost > Traits** on live roots. A buffer adds latency; half-lives filter error; the horizon limits prediction. Each has a separate role.

At 360 km/h, 60 Hz observations are about 1.67 m apart. Delivery delay represents 4-12 m of travel, while independently noisy observations can differ by up to 4 m. Supplied analytical velocity helps smooth noise without treating those position differences as motion. Prediction can still overshoot an unseen stop or turn.

Change speed from 360 to zero and back to compare abrupt maneuvers. Travel already accumulated is preserved; delayed packets retain their original pose/velocity. Live tuning blends setting differences over 0.25 unscaled seconds. `Spatial.ResetPresentation()` handles intentional teleports.

Automatic Advance uses real elapsed time independently of Unity time scale. For deterministic stepping, set `geoSource.AutomaticAdvance = false`, then call `Advance(seconds)` and `realm.Update()`. Clock-mode changes preserve travel and rebase this sample-owned Realm clock. Large manual jumps bound capture work rather than replaying an unlimited backlog.

## SDK and reference coordinates

The mock SDK starts at **52.520008 degrees N, 13.404954 degrees E, 40 m ellipsoidal height**. This generates the road; it is not a separately configured projection origin. The followed car defines the current geographic tangent frame.

Readings contain WGS84 position, named heading/pitch/roll degrees, original capture time and optional motion. Linear ENU uses X east, Y north, Z up in metres/second; acceleration is gravity-free metres/second squared. The bird supplies world ENU angular velocity in radians/second. The initializer maps these into the traits independently of reader order; absent channels are cleared.

The SDK captures noise once per observation and retains its timestamp through delayed delivery. Reordered older snapshots are rejected as a whole so they cannot resurrect departed entities or attachment state. Held packets do not acquire new timestamps. The Realm estimates relative sample age; it does not know absolute transport latency.

The camera stays fixed. Follow Rotation cancels the reference's full attitude; assigning the same SDK rotation to the camera would add another rotation. Source quaternion presets do not reinterpret the named geographic angles. The installed package's `Documentation~/Spatial.md` covers input conventions and clock/reset contracts.

## Query parked cars and the bird

The Tracking prefab includes [GeoQueryExample](GeoQueryExample.cs). Its overlay shows available cars, observed parked-car membership and raw SDK bird separation. Use **Log queried parked cars** in its component context menu to enumerate positions.

```csharp
Query cars = realm.Query().InAnchor("relative-world").OfKind(GeoSource.Kind).With<Spatial>();
Query parkedCars = cars.WithVariant(new Variant("target"));
Query birds = realm.Query().InAnchor("relative-world").OfKind(GeoSource.BirdKind).With<Spatial>();
IGhost bird = birds.FirstOrDefault();
```

Derived filters leave `cars` unchanged. The consumer retains Keys through paired Observe callbacks, disposes/clears on disable, and rebinds after Realm restart. Bird separation uses raw ECEF positions and Double3.Distance, independently of views, filtering and range. The installed package's `Documentation~/Queries.md` contains the complete contracts.

## Attach and detach the bird's feet

While playing, open **Geo Source**'s context menu and choose **Detach bird feet** or **Attach bird feet**. Code can call `SetBirdFeetAttached(false)` / `true`. Detach publishes coherent absolute release poses and zero release motion; reattach retains the roots/views. Both commands clear pending packets and advance the mock clock by one microsecond so their state is accepted during manual stepping too.

The feet are `relative.bird-foot / bird-left-foot` and `bird-right-foot`, reported before the bird at startup. `GeoAttachmentTrait` attaches by complete Key, so discovery order is irrelevant. It converts SDK forward/right/down offsets to Unity `(Y, -Z, X)`. Parts inherit the bird's processed pose while absolute readers keep a release pose cached. Attached feet bypass their own filters until detached.

The bird's four-metre orbit takes eight seconds at 3.2 m height with a 20-degree bank. Parked bays are fixed 80 m apart, alternating sides; the SDK reports them from 38 m ahead to 22 m behind the origin. These deterministic paths make membership and presentation changes repeatable.
