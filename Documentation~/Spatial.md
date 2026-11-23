# Spatial input and presentation

Add `Spatial` to each Ghost that uses shared coordinates. Its raw position/orientation remain separate from the Unity transform. An optional `Realm.ReferenceFrame` projects every participating root around a fixed or followed reference; without one, Cartesian poses map directly to Unity world space.

Examples assume `using Emas;` and `using UnityEngine;`. `realm`, `ghost` and SDK readings belong to the calling integration. All calls require Unity's main thread.

## Choose the input contract

| Setter | Input | Reference space |
| --- | --- | --- |
| `SetCartesianPosition(Double3)` | Shared XYZ in application distance units and source axes. | Cartesian or no reference. |
| `SetGeographicPosition(GeoPosition)` | WGS84 latitude/longitude degrees and ellipsoidal height metres. | Geographic. |
| `SetEarthCenteredPosition(Double3)` | ECEF XYZ metres. | Geographic. |
| `SetSourceRotation(Quaternion)` | Quaternion in configured source axes; local tangent attitude for Geographic. | Either. |
| `SetGeographicRotation(yaw, pitch, roll)` | Degrees: heading clockwise from true north, nose-up pitch, then right-wing-down roll. | Geographic. |
| `SetEarthCenteredRotation(rotation, bodyAxes)` | Active body-to-ECEF quaternion; default body axes are X forward, Y right, Z down. | Geographic. |

Position and attitude arrive independently. Setters retain accepted input for the next Realm projection. `Position` is Cartesian XYZ or ECEF metres, never latitude/longitude. `RotationSpace` identifies the stored quaternion basis. Check `HasPosition` / `HasRotation` before reading channels. Convert mean-sea-level altitude to ellipsoidal height in the application.

For native ECEF attitude, decode SDK quaternion ordering and invert passive ECEF-to-body attitudes before supplying them. A custom right-handed body mapping can be passed explicitly:

```csharp
// SDK body axes: X forward, Y left, Z up.
CoordinateSystem bodyAxes = new CoordinateSystem(
    Axis.NegativeY, Axis.PositiveZ, Axis.PositiveX);
spatial.SetEarthCenteredRotation(bodyToEcef, bodyAxes);
```

Named geographic angles and ECEF attitudes retain their physical meaning independently of the source quaternion preset. ECEF attitude remains global when position changes without another attitude observation.

## Choose spatial behavior per Ghost

Author components on the same Ghost root:

| Components | Result |
| --- | --- |
| `Spatial` | Direct placement, attachments and range suppression. |
| `Spatial` + `Smoothing` | Independently filter position and rotation correction error. |
| `Spatial` + `Extrapolation` | Predict motion during bounded gaps after observations. |
| All three | Predict motion and filter remaining correction error. |

Smoothing and Extrapolation are behavior Traits with no reader binding. The Realm evaluates them after data traits and Ghost hooks. Attached parts inherit their parent's processed pose and bypass their own filters. Disabling Spatial releases transform control and restores presentation components it suppressed; the last world pose remains for application control.

Use one transform writer per root. Keep articulation on children; integrate dynamic physics explicitly. Root parenting stays under its Anchor, but projection compensates for that parent's placement rather than applying its offset twice.

## Configure a prefab realm

Assign Kind blueprints on `RealmSetup`, with Spatial and any motion traits already on each Ghost prefab. Add an `AnchorSetup`, detector and optional initializer per feed. Under **Reference**, enable **Use reference frame** and choose Cartesian or Geographic.

For a moving reference, enable **Follow a Ghost** and enter the complete Anchor/Kind/entity identity. Leave Entity ID empty for runtime selection, then assign `realmSetup.Realm.ReferenceFrame.FollowedGhost` after startup. Startup fields are read-only while that Realm runs; code can change its live ReferenceFrame.

Equivalent geographic code setup:

```csharp
realm.ReferenceFrame = new ReferenceFrame
{
    Space = ReferenceSpace.Geographic,
    FollowedGhost = new Key("vehicles", new Kind("vehicle"), "ego"),
    UnityPosition = Vector3.zero,
    UnityRotation = Quaternion.identity,
    FollowRotation = true,
    MaxDistance = 5000.0 // Metres in Geographic space.
};
```

For fixed references, assign `Position` in Cartesian space or `GeographicPosition` in Geographic space instead of FollowedGhost. A new ReferenceFrame has no usable position until assigned or resolved from its followed Ghost.

`FollowRotation = true` cancels the reference's full heading, pitch and roll for positions and attitudes. For a cockpit, keep the camera fixed relative to the desired Unity reference pose; applying SDK attitude to that camera adds another rotation. With false, only position is followed. A followed reference uses the same processed presentation time as its peers.

## Choose source coordinates

`ReferenceFrame.Coordinates` maps signed source axes to Unity right/up/forward:

| Preset | Source XYZ | Unity right / up / forward |
| --- | --- | --- |
| `Unity` | Right, up, forward. | +X / +Y / +Z |
| `EastNorthUp` | East, north, up. | +X / +Z / +Y |
| `NorthEastDown` | North, east, down. | +Y / -Z / +X |

Custom mappings use each source axis exactly once. Quaternion conversion changes both world and local bases, including handedness. Decode SDK Euler order and quaternion conventions in the application. Prefab geometry and Unity placement always use Unity axes.

In Cartesian space this setting covers positions and source quaternions. In Geographic space it covers source quaternions only: WGS84 positions, named geographic angles and explicit ECEF inputs have fixed meanings. Changing Coordinates reinterprets stored source poses on the next update; it does not rewrite raw input.

## Supply SDK sample times

Pass original observation times, preferably as separate SDK seconds/nanoseconds:

```csharp
Timestamp time = new Timestamp(sdkSeconds, sdkNanoseconds);
Spatial spatial = ghost.GetRequired<Spatial>();
spatial.SetGeographicPosition(location, time);
spatial.SetGeographicRotation(heading, pitch, roll, time);
```

Use one shared SDK clock per Realm, advancing in real-time seconds. `Timestamp` assumes no epoch; `ElapsedSince` subtracts whole seconds before converting to doubles. `FromSeconds(double)` is available when the SDK already supplies floating-point time.

Channels independently reject older/duplicate timed samples. A newer stationary observation refreshes age. Untimed input clears that channel's ordering guard; repeated identical untimed positions and rotations do not refresh age. This lets prediction continue while readers reread an SDK cache between packets. Even small quaternion component changes refresh untimed rotation age; changing the attitude representation or ECEF body axes also counts as fresh input. `PositionTime` and `RotationTime` expose the accepted times.

The Realm aligns SDK time from the least-delayed observed arrival and advances with Unity's unscaled clock. Every root uses one captured presentation time. This estimates relative packet age; absolute transport latency remains unknown. For unrelated SDK clocks, use separate Realms or convert clocks in the application.

Call `realm.ResetSpatialTime()` before publishing after an SDK clock restart. It clears alignment, ordering and presentation histories while retaining raw pose, supplied motion and settings.

## Buffer packet timing before filtering noise

```csharp
realm.InterpolationDelay = 0.1f; // Shared playback delay in seconds; default is zero.
```

Timed channels play at estimated SDK time minus this delay. Positions interpolate in doubles; rotations use the shortest quaternion arc. Multiple observations between updates are retained. Choose a delay covering delivery variation; it adds latency to the unknown transport delay and cannot reconstruct unseen motion.

Each channel retains at most 128 observations. Playback before retained history holds the oldest sample; playback beyond the newest uses enabled Extrapolation up to its horizon, then holds. Untimed channels remain immediate. Motion is sampled on the same playback timeline. Attachments inherit their parent's result.

Start with a short gap horizon such as 0.05 seconds and compare buffered/immediate playback using the samples. Buffers reduce packet stepping; smoothing separately reduces measurement noise. Neither guarantees exact alignment during missing data or unobserved maneuvers.

## Tune smoothing in half-lives

A half-life is seconds to halve remaining correction error: 50% remains after one, 25% after two. Position defaults to 0.08 seconds; rotation defaults to zero.

| Half-life | Effect |
| --- | --- |
| `0` | Immediate response after any live-edit transition. |
| `0.02-0.05 s` | Light filtering and responsive correction. |
| `0.05-0.15 s` | Moderate filtering. |
| `0.15-0.5 s` | Stronger filtering with more response lag. |

With timed SDK velocity supplied through Extrapolation, position filtering compensates observed motion and filters measurement error. This works with Extrapolation disabled or a zero horizon. With prediction enabled, changes in predicted lead apply separately from position noise. New observations retain motion assistance after earlier packets reach the horizon; a held sample at its cap adds no motion. Recorded motion integrates velocity segments rather than applying later acceleration retroactively.

Without SDK velocity and timed observations, moving inputs have ordinary low-pass lag, approximately `speed * halfLife / ln(2)`. Position-derived velocity is not used to compensate this filter because that would pass position noise through. Angular velocity supplies gap prediction but does not provide rotation-filter motion assistance.

Following a Ghost's orientation also follows its angular filtering. Use zero/small, comparable rotation half-lives when reference/peer alignment matters. Other roots' position filters do not add lag to reference changes.

Live edits to horizons, half-lives, enabled states, buffering or strategy instances retain history and blend their presentation difference over 0.25 unscaled seconds. Repeated edits rebase the correction while normal motion continues. Raw input is unaffected. For a teleport, supply the new pose and motion, then call `Spatial.ResetPresentation()` to reset playback, estimates, filters and transitions. `Smoothing.Reset()` resets only its filter and tuning; supplied motion remains separate.

## Extrapolate motion independently

Choose **Ghost > Traits > Extrapolation > Strategy**, or assign a model in code:

```csharp
Extrapolation extrapolation = ghost.Trait<Extrapolation>();
extrapolation.MaximumExtrapolation = 0.1f;
// Choose one assignment:
extrapolation.Strategy = new ConstantAccelerationExtrapolation();
extrapolation.Strategy = new ConstantVelocityExtrapolation();
extrapolation.Strategy = new SingerExtrapolation { ManeuverTimeConstant = 0.5f };
extrapolation.Strategy = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0.1f };
extrapolation.Strategy = new NoExtrapolation();
```

| Model | Gap behavior |
| --- | --- |
| Constant acceleration (default) | `velocity*t + acceleration*t*t/2`; constant angular velocity. |
| Constant velocity | Continue linear/angular rates, ignoring linear acceleration. |
| Singer | Integrate velocity with exponentially decaying acceleration; constant angular velocity. |
| Coordinated turn | Rotating-body second-order motion and cubic packet correction. |
| No extrapolation | Hold observations after recorded playback; retain interpolation and SDK motion assistance. |

`MaximumExtrapolation` caps age beyond the newest observation; it defaults to 0.15 seconds. Larger horizons bridge longer gaps but risk overshooting stops and turns. Zero or a disabled trait bypasses all strategies, including packet correction.

Built-in position models need supplied or estimated velocity; acceleration alone does not invent velocity. Consecutive timed positions can estimate velocity. Supplied angular rates can extend one attitude; otherwise the last two timed rotations estimate a shortest-arc rate. A supplied zero overrides estimates. Coupled models can use angular motion for translation as well.

### Singer acceleration

`ManeuverTimeConstant` is a positive duration in seconds, default 1. Acceleration follows `a(t) = a(0)*exp(-t/tau)`; after tau, about 37% remains. Smaller tau approaches constant velocity; larger tau approaches constant acceleration. Velocity is integrated, not damped toward zero. Missing acceleration gives constant velocity.

This is a deterministic mean trajectory, with no estimator or process noise. It does not enable packet correction. Assign a new instance to blend a setting change; editing an existing instance takes effect directly. [Implementation](../Runtime/Entities/SingerExtrapolation.cs).

### Aircraft body-frame dead reckoning and coordinated turns

CoordinatedTurnExtrapolation integrates quaternion body rotation and `R(t)*(vBody + aBody*t)` with closed-form rotating-vector integrals. It uses attitude sampled at the position time; future-only attitude cannot turn older motion. Missing attitude falls back to world second-order motion.

Acceleration must be **gravity-free inertial acceleration**. The model derives `aBody = inverse(R)*aWorld - cross(omegaBody,vBody)`, avoiding duplicate centripetal acceleration. Missing acceleration means constant body velocity; supplied inertial zero is different.

Supplied/estimated rates, including zero, override the optional bank fallback. Otherwise it estimates steady coordinated turn rate `Gravity*tan(bank)/horizontalSpeed`. This assumes negligible sideslip/wind and uses ground speed. It declines near-zero speed, nearly vertical heading and bank magnitudes of 85 degrees or more. Gravity must match the distance unit; the default 9.80665 assumes metres. [Implementation](../Runtime/Entities/CoordinatedTurnExtrapolation.cs).

`PacketSmoothingDuration` defaults to 0.1 unscaled seconds. Position uses a cubic Hermite error spline; rotation uses a spherical cubic quaternion correction. Accepted pose/motion updates advance the prior correction to the arrival time before rebasing its pose and tangent, so correction still progresses when a packet arrives every presentation frame. Held/rejected samples do not restart it. Each root owns its state, so immutable strategies can be shared. The correction reaches the capped trajectory with zero remaining correction/tangent at the end; it can finish after the extrapolation horizon.

Zero duration bypasses packet correction. Omit Smoothing to evaluate cubic correction alone. With Smoothing present, its position assistance follows the corrected trajectory. Presentation/clock resets discard packet state. Custom models can request this pipeline through `IPacketSmoothingStrategy`.

## Supply SDK motion

Linear vectors use source units/second and units/second squared. Cartesian setters use configured shared axes; ECEF setters use XYZ metres; geographic setters accept ENU (X east, Y north, Z up) at an explicit SDK tangent origin:

```csharp
extrapolation.SetGeographicVelocity(enuVelocity, sdkTangentOrigin, velocityTime);
extrapolation.SetGeographicAcceleration(enuAcceleration, sdkTangentOrigin, accelerationTime);
```

The origin is the entity location for local ENU or the SDK's fixed origin for shared ENU; it is independent of the presentation reference. Acceleration must have gravity removed. Convert body-frame linear vectors using raw SDK attitude at the motion timestamp, never the projected Ghost transform.

Angular vectors always use **radians/second**:

| SDK vector | Setter |
| --- | --- |
| World ENU | `SetGeographicAngularVelocity(rate, origin, time)` |
| World ECEF XYZ | `SetEarthCenteredAngularVelocity(rate, time)` |
| Aircraft body axes | `SetBodyAngularVelocity(rate, bodyAxes, time)` |

```csharp
// Default body axes: X forward, Y right, Z down.
extrapolation.SetBodyAngularVelocity(new Double3(rollRate, pitchRate, yawRate),
    CoordinateSystem.NorthEastDown, angularTime);
```

Body rates are true angular-vector components, not Euler-angle derivatives. A body-axis mapping does not turn world ENU into body rates. World ENU uses the right-hand rule: positive up turns north toward west, opposite to positive geographic heading. World rates compose before attitude; body rates compose after it. Handedness conversion preserves rotation sense. `AngularVelocity` exposes model right/up/forward rates for body input (`-pitch, yaw, -roll` for the default) or ECEF for world input.

Motion is sampled at each pose channel's own observation time; future-only samples are excluded. Accepted angular body/world frame changes clear incompatible history. Call `ClearVelocity`, `ClearAcceleration` or `ClearAngularVelocity` when a supplied channel disappears; clearing angular velocity restores history estimation. Resetting estimates retains supplied channels.

## Implement a custom strategy

Implement `IExtrapolationStrategy` with deterministic, non-mutating methods. Both receive `ExtrapolationContext` and bounded seconds; preview/tangent evaluation may call them repeatedly. Return zero displacement or identity rotation at zero seconds. Return finite source-space displacement and a finite nonzero quaternion offset.

The context contains raw accepted poses with their own timestamps, time-aligned observation pose/motion, supplied/estimated provenance, world/body angular convention and coordinate metadata. Newest motion timestamps may be later than effective sampled motion. Emas owns interpolation, composition, coordinate conversion and per-root histories.

A `[Serializable]` concrete class with a public parameterless constructor appears in the selector with its serialized settings. Runtime-only implementations need no serialization attribute. Null assignment is rejected; missing serialized references use the default strategy. Replace instances for a blended live transition; in-place setting edits are immediate. Finite-result overflows reported as `ArgumentOutOfRangeException` fall back to observed pose; other application exceptions propagate.

## Attach and detach entities

```csharp
Spatial part = partGhost.GetRequired<Spatial>();
part.Attach(new Key("vehicles", vehicleKind, vehicleId),
    new Vector3(1, 0, 2), Quaternion.identity);
// Publish coherent absolute release pose/motion before detaching.
part.Detach();
```

Parents use complete same-Realm Keys and may arrive later or belong to another Anchor. Offsets use Unity local axes/units, independently of source presets and parent scale. An SDK forward/right/down offset becomes `(Y, -Z, X)`. Keep Spatial enabled. Parent-first projection resolves chains without reparenting roots or sharing their lifetimes.

Missing/unavailable/out-of-range parents and cycles suppress dependent presentation while keeping entities tracked/queryable. Breaking the condition resumes projection. Application-positioned parents without enabled Spatial also work. Attached parts still cache absolute input; Detach resumes it on the next projection. Missing absolute position stays hidden; stale input can jump. Follow the vehicle's absolute input rather than an attached part as the reference.

## Preserve precision before Unity

Subtract reference positions in doubles before casting to Unity floats. Converting an already-rounded global Vector3 cannot recover precision. Cartesian projection applies source-axis mapping, optional inverse reference orientation, then desired Unity alignment. Geographic projection derives local east/up/north at the moving reference and retains Earth curvature.

Geographic distance/range is straight-line ECEF distance in metres, not surface distance. At exact poles, longitude zero defines the tangent axes. Keep the desired Unity reference near the scene origin. Network scenery should share projection; a local cockpit may remain fixed.

## Limit distant presentation

`MaxDistance` is a positive optional radius in source units (metres for Geographic); null disables the limit. Both the buffered observation and processed pose must fit the reference/range and finite Unity floats. Missing raw input invalidates presentation history; an otherwise valid filtered pose outside range keeps settling rather than repeatedly resetting.

Suppression hides views, root renderers and colliders while tracking/query membership and scripts continue. `Spatial.IsInRange` describes the latest projection. Requested views return automatically when presentable; `Demanifest` cancels the request. Disappearance and detector failure have separate tracking lifecycles.

## Reference loss and conversion helpers

A missing followed reference sets `IsReferenceAvailable` false and freezes its last valid pose/tangent axes. Following resumes on return. Before the first valid reference, spatial presentation is suppressed. Removing the ReferenceFrame restores identity Cartesian projection, including stored ECEF numbers if geographic input remains.

| Helper | Use |
| --- | --- |
| `TryToUnityPosition(position, out result)` | Project Cartesian/ECEF or GeoPosition input; false if uninitialized, outside range or unrepresentable. |
| `ToSimulationPosition(unityPosition)` | Inverse world-position conversion; returns ECEF in Geographic space. |
| `ToGeographicPosition(unityPosition)` | Inverse WGS84 conversion. |
| `ToUnityRotation` / `ToSimulationRotation` | Source attitude conversion; geographic overloads accept an entity location, otherwise use the reference location. |
| `ToUnityEarthCenteredRotation` / `ToEarthCenteredRotation` | Body-to-ECEF attitude conversion with explicit body axes. |
| `DistanceTo` / `Double3.Distance` | Reference distance or independent shared-coordinate distance. |

Check `HasPosition` before inverse/reference-dependent operations. Helpers use the latest reference resolved by a Realm update. `GeoPosition.ToEarthCentered` / `FromEarthCentered` and `ToEarthCenteredVector` also work independently of a Realm.

## Compare observed and presented poses

Open **Window > Emas > Pose debugger** and click **Activate pose tool**, or choose **Emas pose comparison** from the Scene tools menu. The Emas Scene overlay shares the same target and drawing controls. The Ghost Inspector's **Inspect poses in Emas** shortcut opens the debugger, activates the tool and pins that Ghost.

Cyan marks the latest accepted absolute observation; magenta marks the presented root after extrapolation, packet correction and smoothing. Both use the same cached presentation reference, so the followed Ghost's raw marker can differ from its origin. A yellow line connects their positions. Enable each marker, labels and the connection independently and adjust the marker size; axes use red X, green Y and blue Z. Gizmos draw while the pose tool is active.

With no pinned target, selecting a Ghost or one of its view objects compares that root; multiple selected view objects on one Ghost produce one comparison. **Pin** retains the first target while you select other objects. **Selection** resumes following selection, and **Followed Ghost** pins the selected Realm's reference. **Frame comparison** fits both markers in the Scene view. The window shows the active strategy, maximum prediction horizon, source timestamps, position/angular differences and observed/presented Unity poses. Tracking health remains under its **Tracking** tab.

Observed position and attitude retain their independent sample times. The gizmo represents accepted input at those times, while the presented root includes the configured processing. Missing attitude draws only a position marker. Attached roots compare their cached absolute observation with their attached presentation.

Target differences exclude prediction and filtering of the followed reference, because both markers use that same reference. A stationary target can show zero difference even while reference prediction rotates it across the scene. Choose **Followed Ghost** to inspect the reference's own position and angular differences. These differences measure processing, not error against the true aircraft pose. Position smoothing can also offset prediction, so a small net difference does not prove prediction is inactive.

For a cockpit with a fixed camera, a small reference-angle error grows with target range and offset. A one-degree pitch error moves a target about 17.5 metres vertically per kilometre forward; a one-degree bank error moves it about 17.5 metres vertically per kilometre sideways. This can make distant targets jitter while their own observed/presented markers overlap.

To isolate maneuver drift, disable Extrapolation on the reference only and wait at least 0.25 unscaled seconds for the live-edit transition. If the wrong-direction movement remains, inspect the raw reference attitude, source-axis mapping and sample times.

If it disappears, check supplied angular-rate signs, units and body/world axes. `ClearAngularVelocity()` selects estimation from timed attitudes instead of supplied rates; noisy attitudes can make this estimate noisy too.

When using Constant Acceleration with Smoothing disabled, no packet correction or rotation filter is active. Prediction still uses estimated relative packet age, which can be close to zero for the freshest stream. The maximum horizon is a cap, not a fixed lead or a correction for unknown absolute latency.

The Editor controls only read existing state. For application diagnostics, call `spatial.TryGetObservedUnityPose(out Pose pose)` on the main thread after `Realm.Update()`. It projects Cartesian, geographic and ECEF observations, including custom body axes, through the cached reference and its range limit. False means the observation cannot currently be projected. Reads preserve observations, filters and transforms.

## Run the examples

[Relative world](../Samples~/RelativeWorld/README.md) demonstrates WGS84 input, noisy/delayed observations, a moving reference and attachable bird feet. [Aircraft formation](../Samples~/AircraftFormation/README.md) demonstrates independently timed aircraft and angular presentation. Their guides describe controls and defaults; initializers show SDK-to-trait mappings.
