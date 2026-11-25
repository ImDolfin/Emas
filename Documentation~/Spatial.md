# Spatial input and presentation

Add `Spatial` to each Ghost that uses shared coordinates. Its raw position/orientation remain separate from the Unity transform. An optional `Realm.ReferenceFrame` projects every participating root around a fixed or followed reference; without one, Cartesian poses map directly to Unity world space.

`Spatial` is the single input API for position, orientation, velocity, acceleration and angular velocity. Data traits only write to Spatial in the SDK's coordinate space. Extrapolation and Smoothing read those cached observations; they do not own SDK inputs. Inputs can arrive before either processing component exists, and remain available when prediction is disabled or removed.

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

In an aircraft-fixed view, a right-wing-down bank moves a world-stationary target on the right upward and a target on the left downward. At 30 degrees of bank, an initially level target 100 metres to the right has a body-relative height of +50 metres. A nose-up pitch moves a target straight ahead downward. This is the inverse reference rotation, not extra target movement. A world-level view instead uses `FollowRotation = false` and shows the reference aircraft model banking while world-stationary targets stay level.

Accepted attitude changes apply their full angle, including sudden large changes. Extrapolation predicts gaps; it does not set the strength of reference rotation cancellation. For an immediate-input comparison, disable Extrapolation and Smoothing, set `realm.InterpolationDelay = 0`, and allow 0.25 unscaled seconds for live-setting transitions. Disabling those components alone does not disable buffered playback. With processing bypassed, an opposite bank/pitch response calls for checking the input attitude direction and coordinate contract: named geographic angles use positive nose-up pitch and right-wing-down bank; source and ECEF quaternions must map body vectors into the declared world axes, rather than the inverse.

For Geographic input, the Ghost Inspector shows **Observed bank** from the latest accepted attitude and **Reference bank** from the followed reference's processed attitude. Compare these during a near-level maneuver to see how much reference processing leads or trails the accepted input. They show principal bank angles and become undefined at vertical pitch. Neither reading measures the unknown age of the SDK data against the actual aircraft. The pose debugger also shows the shared **Playback delay**, including while Extrapolation is disabled. Constant Acceleration estimates angular acceleration from eligible rate or quaternion history when enough timed samples exist. A sudden rate change cannot be predicted before an observation or rate sample reports it.

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

With Unix creation timestamps on the followed Ghost, that aircraft drives the Realm's timeline. Emas compares creation time with UTC at receipt and takes rolling medians over its latest 31 distinct packets. Duplicate channel timestamps and repeated cached reads count once. This removes measured delivery age from clock alignment and selects current UTC as the shared target, advancing with Unity's unscaled clock between arrivals. Other entities cannot advance that clock. Packet jitter cannot move the target backward. Replacing the reference or resetting spatial time clears its offset history.

`ReferenceFrame.TimestampClock` defaults to `Automatic`. It recognizes reference timestamps within one day of calibrated host UTC as Unix seconds/nanoseconds. No extra SDK clock calls are needed. Set it to `Unix` for timestamps outside that detection window, or `Simulation` for an arbitrary epoch, replay or a simulation clock that happens to resemble UTC. Source and host UTC clocks must agree after calibration for measured packet ages to be accurate. Arbitrary simulation clocks retain least-delayed-arrival alignment across roots; their absolute delivery delay remains unknown.

Reference clock configuration is code-only. Set `ReferenceFrame.ClockOffset` to `simulation UTC - host UTC`. For a simulation clock 80 ms ahead, use `0.080`; for 80 ms behind, use `-0.080`:

```csharp
realm.ReferenceFrame.ClockOffset = 0.080;
```

The clock uses `simulation now = host UTC now + ClockOffset`. Calibrate this difference independently of packet delivery delay. The reference applies it once to the entire Realm. Ghosts supply timestamped observations and retain their own motion processing; they do not store clock configuration. Keep raw creation timestamps unchanged and leave `ReferenceFrame.PredictionTime` at zero for automatic age compensation. Changes reset alignment on the next projection pass, including corrections toward an earlier time. `Simulation` mode ignores this UTC correction because relative alignment already absorbs clock origins. The default offset is zero.

`Realm.ProjectedTime` exposes the timestamp selected by the last projection pass, including shared forward prediction and playback delay. It stays fixed between passes. Each entity samples or predicts from its own creation time toward that shared target using its selected strategy and prediction limit. Attachments inherit their parent; distant direct-pose entities still bypass prediction.

Unix creation timestamps align independently aged observations automatically without a manual forward interval. Different delays require different extrapolation durations toward the same target. For unrelated SDK clocks, use separate Realms or convert clocks in the application.

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

With timed SDK velocity supplied through Spatial, position filtering compensates observed motion and filters measurement error. This works with Extrapolation disabled or absent. With prediction enabled, changes in predicted lead apply separately from position noise. New observations retain motion assistance after earlier packets reach the horizon; a held sample at its cap adds no motion. Recorded motion integrates velocity segments rather than applying later acceleration retroactively.

With timed angular motion, rotation filtering also advances by the selected model's known turn before smoothing the remaining attitude error. Body and world angular rates retain their composition convention. Pose-derived angular rates are filtered before motion assistance so differentiated measurement noise does not bypass correction filtering. Supplied rates are used directly. Constant-rate turns therefore converge without requiring extra forward prediction to compensate the rotation half-life. At the prediction cap, motion assistance stops with the target. Without timed motion, moving inputs retain ordinary low-pass lag, approximately `speed * halfLife / ln(2)`. Position-derived velocity is not used to compensate this filter because that would pass position noise through.

Following a Ghost's orientation also follows its filtered correction error. Strong filtering still takes longer to accept corrections or unobserved changes in motion. Other roots' position filters do not add lag to reference changes.

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
| Constant acceleration (default) | Uses supplied linear acceleration or fits missing linear/angular acceleration from timestamped history. |
| Constant velocity | Continue linear/angular rates, ignoring linear acceleration. |
| Singer | Integrate velocity with exponentially decaying acceleration; constant angular velocity. |
| Coordinated turn | Rotating-body second-order motion and cubic packet correction. |
| Constant Turn and Acceleration (CTA) | Coordinated turns with missing body acceleration estimated from velocity or position history. |
| Constant Turn, Climb and Acceleration (CTCA) | Horizontal turning and tangential acceleration with constant vertical climb rate. |
| Spline | Cubic Bézier continuation fitted to timestamped poses or SDK velocities; quaternion-log rotation. |
| No extrapolation | Hold observations after recorded playback; retain interpolation and SDK motion assistance. |

`MaximumExtrapolation` caps age beyond the newest observation; it defaults to 0.15 seconds. Larger horizons bridge longer gaps but risk overshooting stops and turns. Zero or a disabled trait bypasses all strategies, including packet correction.

### History-based constant acceleration

Constant Acceleration owns its motion estimation. SDK linear velocity and acceleration remain authoritative, including supplied zero acceleration. If acceleration is missing, the strategy fits a line through retained SDK velocities. Without enough velocity observations, it fits a quadratic through positions and obtains both acceleration and velocity at the latest observation. This avoids treating the last interval's average velocity as the current velocity while accelerating.

Angular acceleration comes from SDK angular-rate history or from consecutive quaternion arcs. Arc rates are fitted at their interval midpoints, recovering the rate at the attitude timestamp without Euler-angle wrapping. Supplied zero angular velocity holds orientation. A fixed-axis accelerating bank has an exact quaternion exponential; changing axes use bounded midpoint integration with the proper body/world multiplication order.

Fits use actual SDK timestamp differences and at most eight vector observations or nine orientations. The preferred window is 0.25 seconds; slower feeds retain the minimum two rates or three poses needed for estimation. Least-squares fitting reduces sensitivity to individual noisy samples. Degenerate or unrepresentable fits fall back to available motion. A single pose cannot provide velocity, and fewer than three poses cannot determine position-derived acceleration. Untimed observations do not invent an acceleration estimate.

The strategy reads only history eligible at its observation time, including packets received between render updates. It does not accumulate estimates from rendered transforms or keep shared mutable estimator state. Clock/presentation resets and channel/frame changes restart the relevant fitting windows. Older SDK samples needed for playback or interpolation remain available to that pipeline, but do not re-enter a reset derivative fit. Other strategies retain their own estimation rules; no new SDK setter calls or authoring controls are required.

### Request actual forward prediction

The Ghost Inspector's Extrapolation section shows a compact **Prediction timing** row, including on prefabs. Hover its label for the clock explanation. With a live timed reference, it shows **Automatic (Realm reference)**, followed by the cached **Projected time**, **Packet age (ms)** and independent position/rotation sample-to-target intervals. Packet age is the shared clock's calibrated median creation-to-receipt offset, exposed in seconds by `Realm.EstimatedPacketAge`. It excludes playback delay, manual lead, caps and smoothing, and stays fixed between projection passes. It is unavailable without Unix clock alignment; a negative age means reference observations are ahead of calibrated host UTC. Negative sample-to-target intervals show **(buffered)**; intervals beyond the entity's prediction limit show **(over limit)** beside the value. These intervals describe the requested target, not the final smoothed pose or measured display latency. Disabled, attached and distant direct-pose entities report their processing status. Inspection never advances the Realm or reads the SDK.

**Fallback prediction (s)** stays visible and editable in Edit and Play Mode. Zero selects automatic clock timing. A positive value on the followed Ghost's enabled Extrapolation trait selects manual timing for the whole Realm: reference creation time plus the manual interval plus elapsed time since receipt. It replaces automatic packet-age compensation and any code-only `ReferenceFrame.PredictionTime` request. Lowering the interval can move the shared target earlier. Returning it to zero, disabling or removing that trait restores automatic reference timing. Peer fallback values do not split the shared timeline. Without a timed followed Ghost, the override is individual. **Prediction limit (s)** remains the cap, independent of the requested interval.

The timing row changes to **Manual (Realm reference)** while the followed fallback is positive. Inspecting any peer shows that same shared mode. Packet-age measurements remain visible for comparison while manual timing is active.

Zero **Fallback prediction** restores automatic age-based prediction. **Prediction limit** has a minimum of **0.007 s (7 ms)**, rounded up from one 144 Hz frame. Smaller nonnegative API assignments, including zero, clamp to that minimum. Saved smaller values also use the minimum. Negative or nonfinite API assignments are rejected. Disable the Extrapolation trait to stop prediction while retaining its supplied motion for smoothing. The minimum bounds the cap; it does not force a prediction interval.

```csharp
extrapolation.Strategy = new ConstantAccelerationExtrapolation();
extrapolation.PredictionTime = 0.2f; // On the followed Ghost: manual 200 ms for the whole Realm.
extrapolation.MaximumExtrapolation = 0.5f; // Allow up to 500 ms of total prediction.
```

A pose sampled at SDK time 0.2 targets time 0.4 at receipt with this manual configuration, whether delivery age is 0.1 or 0.3 seconds. Measured packet age is not added or used as a minimum. Time after receipt continues advancing the target until each entity reaches its own prediction limit. Position and rotation retain independent sample times and rates. A peer with a newer observation samples available history to reach the earlier shared target.

This works with Constant Velocity, Constant Acceleration, Singer and Coordinated Turn. No Extrapolation still holds recorded poses. The interval defaults to zero for ordinary age-based prediction. The reference selects the timeline and each entity's motion processing applies its strategy; no SDK adapter, packet or timestamp setter changes are needed.

Every entity retains its selected strategy and `MaximumExtrapolation`; a capped entity can stop short of the common `Realm.ProjectedTime`. Manual selection belongs to the followed Ghost alone. Automatic reference timing remains available without a followed Extrapolation trait. Clock calibration stays code-only on `ReferenceFrame`.

With Unix creation timestamps, leave the followed Ghost's fallback and `ReferenceFrame.PredictionTime` at zero for automatic compensation to calibrated UTC. In automatic mode, the code-only reference interval can still request a minimum duration from the reference observation, counting measured age once. A positive followed fallback takes precedence and switches to manual timing instead. The packet-age diagnostic continues measuring the automatic estimate in either mode. Smoothing does not require more lead during known steady turns.

An arbitrary simulation epoch cannot reveal constant transport latency. A positive followed fallback can supply a manual interval when relative alignment hides that delay. It remains one shared setting for the Realm.

Timed playback buffering subtracts `Realm.InterpolationDelay` before prediction. With 50 ms of buffered playback, request 150 ms to retain a net 100 ms interval at receipt. Untimed input ignores that buffer. The total prediction beyond a recorded pose always remains limited by `MaximumExtrapolation`; disabling Extrapolation also disables forward prediction.

Built-in position models need supplied or estimated velocity; acceleration alone does not invent velocity. Consecutive timed positions can estimate velocity. Supplied angular rates can extend one attitude; otherwise the last two timed rotations estimate a shortest-arc rate. A supplied zero overrides estimates. Coupled models can use angular motion for translation as well.

### Singer acceleration

`ManeuverTimeConstant` is a positive duration in seconds, default 1. Acceleration follows `a(t) = a(0)*exp(-t/tau)`; after tau, about 37% remains. Smaller tau approaches constant velocity; larger tau approaches constant acceleration. Velocity is integrated, not damped toward zero. Missing acceleration gives constant velocity.

This is a deterministic mean trajectory, with no estimator or process noise. It does not enable packet correction. Assign a new instance to blend a setting change; editing an existing instance takes effect directly. [Implementation](../Runtime/Entities/SingerExtrapolation.cs).

### Spline prediction

```csharp
extrapolation.Strategy = new SplineExtrapolation();
```

Spline uses cubic Bézier continuation. It fits recent samples using their creation timestamps, anchors the curve at the newest observation, and evaluates beyond that observation. It uses up to eight samples within 0.25 seconds, retaining the minimum needed at low source rates. Two pose samples give a line, three give a quadratic, and four or more allow a cubic least-squares fit. Samples closer than 0.1 ms are skipped to avoid unstable derivatives. Resets discard the previous fitting window, and future-only samples cannot contribute.

The curve is stored as `c1*u + c2*u² + c3*u³`, with time scaled by the history span. Its equivalent Bézier controls are `B0 = 0`, `B1 = c1/3`, `B2 = (2*c1 + c2)/3`, and `B3 = c1 + c2 + c3`. Power-form evaluation avoids cancellation near the observation and preserves double-precision source offsets. See [PBRT's cubic Bézier definition](https://www.pbr-book.org/4ed/Utilities/Mathematical_Infrastructure) for the curve basis.

Supplied SDK velocities take priority over pose-derived motion. Their own timestamped histories fit quadratic velocity curves, which integrate to cubic displacement. One supplied velocity gives constant-rate motion until more samples arrive. When a fitted SDK velocity predates the pose, its curve advances that velocity to the pose timestamp. Supplied linear acceleration, including zero, sets the current curvature; linear velocity already aligned by supplied acceleration is not advanced twice. Its absence allows curvature from history. Without enough history or a supplied velocity, position holds; acceleration alone does not invent velocity. Untimed input uses supplied velocity and acceleration.

Pose-derived rotation fits quaternion-log offsets around the newest orientation, then maps the predicted offset back to a unit quaternion. Source, geographic and ECEF orientations retain their body-axis conventions. The fit stops at a shortest-arc branch jump instead of treating angle wrapping as acceleration. Supplied angular rates use their own histories and the selected body/world composition. Changing axes use bounded quaternion integration; collinear rates integrate directly. Supplied zero angular velocity holds orientation.

Select **Spline** in the strategy selector, or assign it through code. There are no extra settings or built-in packet corrections. The existing projected time, prediction cap and optional Smoothing trait still apply. Cubic continuation suits short gaps with smoothly changing motion; noisy samples or abrupt stops can produce overshoot over longer gaps. [Implementation](../Runtime/Entities/SplineExtrapolation.cs).

### Aircraft body-frame dead reckoning and coordinated turns

CoordinatedTurnExtrapolation integrates quaternion body rotation and `R(t)*(vBody + aBody*t)` with closed-form rotating-vector integrals. It uses attitude sampled at the position time; future-only attitude cannot turn older motion. Missing attitude falls back to world second-order motion.

The usual [planar constant-turn model](https://www.mathworks.com/help/fusion/ug/motion-model-state-and-process-noise.html) holds horizontal turn rate and speed, with constant vertical velocity. This strategy extends turning motion into three dimensions through body rotation, as used in [rotating-body dead reckoning](https://github.com/open-dis/dis-tutorial/blob/master/DeadReckoningStateUpdate.md). Constant body velocity gives an exact circular or helical trajectory. Constant angular rate predicts attitude directly; changing angular rate requires new observations, unlike Constant Acceleration's angular-acceleration model.

When SDK velocity is missing, the strategy reconstructs the current velocity from up to eight timed positions within 0.25 seconds. It always allows the closest older sample for slower feeds. Each position chord is inverted through the same rotating-body integral before averaging, weighted by the squared time span. This recovers the endpoint tangent instead of treating the interval's average velocity as current. Supplied inertial acceleration participates in that inversion; supplied SDK velocity, including zero, overrides it. All samples must be at or before the channel's observation time and within its current estimation window. A batch received before the first update works too. Insufficient history retains the prepared velocity estimate; singular full-revolution chords are skipped. No strategy state is shared between entities.

Acceleration must be **gravity-free inertial acceleration**. The model derives `aBody = inverse(R)*aWorld - cross(omegaBody,vBody)`, avoiding duplicate centripetal acceleration. Missing acceleration means constant body velocity; supplied inertial zero is different.

Supplied/estimated rates, including zero, override the optional bank fallback. Otherwise it estimates steady coordinated turn rate `Gravity*tan(bank)/horizontalSpeed`. This assumes negligible sideslip/wind and uses ground speed. It declines near-zero speed, nearly vertical heading and bank magnitudes of 85 degrees or more. Gravity must match the distance unit; the default 9.80665 assumes metres. [Implementation](../Runtime/Entities/CoordinatedTurnExtrapolation.cs).

`PacketSmoothingDuration` defaults to 0.1 unscaled seconds. Position uses a cubic Hermite error spline; rotation uses a spherical cubic quaternion correction. Accepted pose/motion updates advance the prior correction to the arrival time before rebasing its pose and tangent, so correction still progresses when a packet arrives every presentation frame. Held/rejected samples do not restart it. Each root owns its state, so immutable strategies can be shared. The correction reaches the capped trajectory with zero remaining correction/tangent at the end; it can finish after the extrapolation horizon.

Zero duration bypasses packet correction. Omit Smoothing to evaluate cubic correction alone. With Smoothing present, known target velocity and angular rate advance its filters; movement from the packet correction curve remains error for the half-life filters to smooth. Smoothing therefore still reduces correction steps when cubic packet correction is enabled. Presentation/clock resets discard packet state. Custom models can request this pipeline through `IPacketSmoothingStrategy`.

### Constant Turn and Acceleration (CTA)

```csharp
extrapolation.Strategy = new ConstantTurnAccelerationExtrapolation();
```

Select **Constant Turn and Acceleration (CTA)** in the strategy selector. CTA extends the same three-dimensional rotating-body model as Coordinated Turn. It holds angular rate and body-frame acceleration constant over each prediction gap, integrating `R(t)*(vBody + aBody*t)` with the shared exact turn integrals. For forward motion in a horizontal plane, this reduces to the usual [constant acceleration and turn-rate model](https://autowarefoundation.gitlab.io/autoware.auto/AutowareAuto/motion-model-design.html). Acceleration changes speed along the turning path; the general 3D form also supports body-sideways and vertical acceleration. Angular rate itself remains constant during prediction.

Coordinated Turn already uses supplied inertial acceleration. CTA adds estimation when it is missing. It first fits the change in timestamped SDK velocity after removing the body's rotation at each sample time. With a current SDK velocity but insufficient velocity history, positions can determine the missing acceleration. Without supplied velocity, at least three positions jointly determine endpoint velocity and body acceleration. The position fit uses integrated rotating motion, preserving turn curvature instead of adding a world-space acceleration fit on top of it. A steady turn therefore estimates zero body acceleration without counting centripetal motion twice.

Fits use up to eight samples in 0.25 seconds, retaining the minimum needed for slower streams. All observations must belong to the current estimation window and be at or before the channel's observation time. Current/interpolated SDK velocity stays authoritative. Older held SDK velocity advances to the pose timestamp through the fit. Supplied gravity-free inertial acceleration, including zero, overrides inferred acceleration and keeps the existing `aBody = inverse(R)*aWorld - cross(omegaBody,vBody)` convention. No new SDK setters are needed.

Sparse or ill-conditioned history falls back to Coordinated Turn. Missing attitude uses Constant Acceleration's world-space prediction. Acceleration alone cannot invent velocity. Presentation and clock resets discard old fits, while immutable strategy instances may be shared. The existing projected time, prediction cap and optional Smoothing trait apply.

CTA has the same packet-correction duration and optional bank-derived turn fallback as Coordinated Turn. Packet correction defaults to 0.1 unscaled seconds; zero applies corrections immediately. Gravity remains available through code and serialization, hidden in the Inspector. [Implementation](../Runtime/Entities/ConstantTurnAccelerationExtrapolation.cs).

### Constant Turn, Climb and Acceleration (CTCA)

```csharp
extrapolation.Strategy = new ConstantTurnClimbAccelerationExtrapolation();
```

Select **Constant Turn, Climb and Acceleration (CTCA)** in the strategy selector. The model holds horizontal turn rate, tangential acceleration and vertical climb rate constant over each prediction gap. Horizontal speed follows `speed(t) = speed(0) + acceleration*t`; course follows `course(t) = course(0) + turnRate*t`; height follows `height(t) = height(0) + climbRate*t`. Cartesian motion uses the configured up axis. Geographic motion uses local east/up/north at the observation and returns ECEF displacement.

Climb rate comes from supplied vertical velocity, or a linear height fit over retained positions. Vertical acceleration is excluded from forward motion and from aligning an older SDK climb rate to the pose timestamp. Horizontal velocity still aligns through the turning model. Supplied inertial acceleration contributes its along-course component to horizontal acceleration; normal acceleration is already represented by turning. Without supplied acceleration, the shared rotating-motion fit estimates horizontal velocity and acceleration. No new SDK setters are needed.

Horizontal turn rate uses the heading change implied by available angular motion and attitude. It differentiates the projected forward direction instead of taking the world-up component of angular velocity alone. This prevents pure roll at a pitched attitude from becoming a false horizontal turn. Missing attitude can use world angular rates or supplied normal acceleration. Missing angular motion can use course changes from SDK velocity or position history, then the optional coordinated bank fallback. Course-only fits refine the turn rate to remove residual lateral acceleration. Supplied zero angular rate overrides turn inference.

Attitude retains full body/world angular prediction. When angular motion is unavailable, inferred course motion can advance yaw while retaining the observed bank and pitch. Roll and pitch do not rotate the separate climb velocity into lateral motion. Fits use at most eight eligible observations from a short window, with bounded course refinement. Sparse input lowers the estimate order; acceleration alone cannot invent velocity. Current SDK motion, estimator resets, shared projected time and prediction caps retain their existing contracts.

Packet correction defaults to 0.1 unscaled seconds and uses the existing per-root pipeline. Optional Smoothing applies separately. Gravity stays available through code and serialization, hidden in the Inspector. [Implementation](../Runtime/Entities/ConstantTurnClimbAccelerationExtrapolation.cs).

## Supply SDK motion

Linear vectors use source units/second and units/second squared. Cartesian setters use configured shared axes; ECEF setters use XYZ metres; geographic setters accept ENU (X east, Y north, Z up) at an explicit SDK tangent origin:

```csharp
spatial.SetGeographicVelocity(enuVelocity, sdkTangentOrigin, velocityTime);
spatial.SetGeographicAcceleration(enuAcceleration, sdkTangentOrigin, accelerationTime);
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
spatial.SetBodyAngularVelocity(new Double3(rollRate, pitchRate, yawRate),
    CoordinateSystem.NorthEastDown, angularTime);
```

Body rates are true angular-vector components, not Euler-angle derivatives. A body-axis mapping does not turn world ENU into body rates. World ENU uses the right-hand rule: positive up turns north toward west, opposite to positive geographic heading. World rates compose before attitude; body rates compose after it. Handedness conversion preserves rotation sense. `AngularVelocity` exposes model right/up/forward rates for body input (`-pitch, yaw, -roll` for the default) or ECEF for world input.

Motion is sampled at each pose channel's own observation time; future-only samples are excluded. Accepted angular body/world frame changes clear incompatible history. Call `ClearVelocity`, `ClearAcceleration` or `ClearAngularVelocity` when a supplied channel disappears; clearing angular velocity restores history estimation. Resetting estimates retains supplied channels.

## Input and prediction work

Separate physical-value traits all use Spatial setters. Each root shares geographic tangent calculations across position, velocity, acceleration and angular input. Unchanged geographic input reuses its conversion. Height-only changes preserve vector axes. Explicit SDK tangent origins stay independent for each motion channel, and stale packets still validate their input. Motion setter names, units, timestamp ordering and zero overrides retain their meaning; replace the former Extrapolation receiver with Spatial. Prediction strategy, interval and limit remain Extrapolation settings.

Bound trait readers continue running each update. Unbound and presentation-only traits skip the reader pass. Ownership and source-generation checks remain immediately before each application reader and Ghost callback, including removal, restart and replacement during callbacks.

Built-in strategies reuse prepared motion on their owning Spatial component. Preparation depends on observations, eligible history, source axes and model settings. Position and orientation keep independent sampling times. New observations, history resets and live model-setting changes invalidate the relevant preparation. Shared strategy instances retain no mutable entity state.

## Implement a custom strategy

Implement `IExtrapolationStrategy.Predict(PredictionRequest)` with a deterministic, non-mutating method. Emas invokes it once per eligible Ghost per Realm projection. The result supplies both pose channels and their velocities to packet correction and smoothing. Live settings edits reuse the previous presentation and its motion instead of evaluating the old strategy again. Separate explicit Realm projections can run within the same Unity frame and each receives its own prediction.

The request supplies an interpolated base `Position` and `Rotation`, independent `PositionSeconds` and `RotationSeconds`, and a time-aligned `ExtrapolationContext` for each channel. Intervals are nonnegative and already capped. Return an absolute `Prediction` in the same position coordinates and rotation representation. Its velocity uses source units per second; its angular velocity uses the world axes of the returned quaternion, in radians per second with Quaternion.AngleAxis signs. At zero duration, retain that channel's base pose. Retain missing channels as well.

`request.CreatePrediction(displacement, rotationOffset, velocity, angularVelocity)` converts model angular axes and composes offsets for you. Its angular inputs use the rotation context's model body/world convention. For example:

```csharp
[Serializable]
public sealed class CustomVelocity : IExtrapolationStrategy
{
    public Prediction Predict(PredictionRequest request)
    {
        Double3 velocity = request.PositionContext.Velocity.GetValueOrDefault();
        return request.CreatePrediction(velocity * request.PositionSeconds,
            Quaternion.identity, velocity);
    }
}
```

Each context contains raw accepted poses with their own timestamps, time-aligned observation pose/motion, supplied/estimated provenance, world/body angular convention and coordinate metadata. Both contexts are captured before the call. TryGetPositionSample, TryGetVelocitySample, TryGetRotationSample and TryGetAngularVelocitySample expose retained raw observations at or before ObservationTime for strategy-specific estimation. Read these bounded live views only during the strategy call; they allocate no memory and do not mutate or interpolate observations. SDK world angular samples remain ECEF even when the prepared AngularVelocity was converted to tangent axes. Newest motion timestamps may be later than effective sampled motion. Emas owns interpolation, timing limits and per-root histories.

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

While attached, a Ghost does not evaluate prediction, packet-correction previews or smoothing. It inherits its parent's presented pose directly. Attach and Detach reset processing once; attached updates do not repeatedly reset filters or histories. Spatial still accepts absolute pose and motion inputs for later Detach. Those unused absolute timestamps do not drive the Realm clock, and an attached root is not used as a followed reference.

Missing/unavailable/out-of-range parents and cycles suppress dependent presentation while keeping entities tracked/queryable. Breaking the condition resumes projection. Application-positioned parents without enabled Spatial also work. Attached parts still cache absolute input; Detach resumes it on the next projection. Missing absolute position stays hidden; stale input can jump. Follow the vehicle's absolute input rather than an attached part as the reference.

## Preserve precision before Unity

Subtract reference positions in doubles before casting to Unity floats. Converting an already-rounded global Vector3 cannot recover precision. Cartesian projection applies source-axis mapping, optional inverse reference orientation, then desired Unity alignment. Geographic projection derives local east/up/north at the moving reference and retains Earth curvature.

Geographic distance/range is straight-line ECEF distance in metres, not surface distance. At exact poles, longitude zero defines the tangent axes. Keep the desired Unity reference near the scene origin. Network scenery should share projection; a local cockpit may remain fixed.

## Realm presentation policy

**Realm Setup > Presentation** groups **Automatic views**, **View distance** and **Processing distance**. These controls apply to every Anchor and can be edited during Play Mode. Automatic views requests configured views for available entities, including existing entities when enabled. Turning it off stops automatic requests and retains existing views. Manual view requests remain available.

Enable **Limit processing distance** and set **Processing distance** to bound motion processing without hiding distant entities. Code-owned Realms use the same setting:

```csharp
realm.PoseProcessingDistance = 2000.0; // Source units; metres in Geographic space.
// realm.PoseProcessingDistance = null; // Restore processing at every distance.
```

Inside and exactly on the radius, poses use their configured buffering, extrapolation, packet correction and smoothing. Outside it, the latest accepted position and rotation are applied directly through the captured reference. No prediction strategy or presentation filter runs there. Source reads, tracking and view requests continue normally, and trait settings remain intact. The Ghost Inspector reports **Pose processing: Direct (Realm distance)** while bypassed. The default is unlimited processing.

Distance uses the raw source position and the captured reference position in doubles, independently of the Anchor scene transform or Unity scene alignment. Geographic space measures the ECEF chord in metres. With no reference, source zero is the distance origin. The radius must be positive and finite.

The followed reference always keeps its normal pose processing. Attached roots continue inheriting their parent's processed or direct pose. Crossing the processing boundary clears old filters, packet corrections and estimated-motion state; returning starts from current input rather than an old nearby pose. Raw inputs and supplied motion are retained. The separate view-distance limit uses `ReferenceFrame.MaxDistance` and requires an enabled reference frame.

Presentation settings previously stored on Anchor Setup now belong to Realm Setup. Re-enter any custom processing radius or automatic-view choice there, and replace code calls to `Anchor.PoseProcessingDistance` with `Realm.PoseProcessingDistance`.

This setting controls processing cost and pose detail, independently for each Anchor. It does not hide entities. `ReferenceFrame.MaxDistance` remains the separate visibility limit described below.

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

Open **Window > Emas > Pose debugger** and click **Activate pose tool**, or choose **Emas pose comparison** from the Scene tools menu. The Emas Scene overlay shares the same target and drawing controls. In Play Mode, the Ghost Inspector's **Inspect poses in Emas** shortcut opens the debugger, activates the tool and pins that Ghost. The Inspector hides **Identity**, **Spatial pose**, the **Pose debugger** shortcut and live prediction readouts outside Play Mode. Trait authoring and the automatic-prediction explanation stay visible on prefabs. Adding or removing traits updates component-card visibility after Unity's current Inspector pass, retaining Undo and prefab settings.

Cyan marks the latest accepted absolute observation; magenta marks the presented root after extrapolation, packet correction and smoothing. Both use the same cached presentation reference, so the followed Ghost's raw marker can differ from its origin. A yellow line connects their positions. Enable each marker, labels and the connection independently and adjust the marker size; axes use red X, green Y and blue Z. Gizmos draw while the pose tool is active.

With no pinned target, selecting a Ghost or one of its view objects compares that root; multiple selected view objects on one Ghost produce one comparison. **Pin** retains the first target while you select other objects. **Selection** resumes following selection, and **Followed Ghost** pins the selected Realm's reference. **Frame comparison** fits both markers in the Scene view. The window shows the active strategy, maximum prediction horizon, shared playback delay, source timestamps, position/angular differences and observed/presented Unity poses. Tracking health remains under its **Tracking** tab.

Observed position and attitude retain their independent sample times. The gizmo represents accepted input at those times, while the presented root includes the configured processing. Missing attitude draws only a position marker. Attached roots compare their cached absolute observation with their attached presentation.

Target differences exclude prediction and filtering of the followed reference, because both markers use that same reference. A stationary target can show zero difference even while reference prediction rotates it across the scene. Choose **Followed Ghost** to inspect the reference's own position and angular differences. These differences measure processing, not error against the true aircraft pose. Position smoothing can also offset prediction, so a small net difference does not prove prediction is inactive.

For a cockpit with a fixed camera, a small reference-angle error grows with target range and offset. A one-degree pitch error moves a target about 17.5 metres vertically per kilometre forward; a one-degree bank error moves it about 17.5 metres vertically per kilometre sideways. This can make distant targets jitter while their own observed/presented markers overlap.

To isolate maneuver drift, disable Extrapolation on the reference only and wait at least 0.25 unscaled seconds for the live-edit transition. If the wrong-direction movement remains, inspect the raw reference attitude, source-axis mapping and sample times.

If it disappears, check supplied angular-rate signs, units and body/world axes. `ClearAngularVelocity()` selects estimation from timed attitudes instead of supplied rates; noisy attitudes can make this estimate noisy too.

When using Constant Acceleration with Smoothing disabled, no packet correction or rotation filter is active. Prediction still uses estimated relative packet age, which can be close to zero for the freshest stream. The maximum horizon is a cap, not a fixed lead or a correction for unknown absolute latency.

The Editor controls only read existing state. For application diagnostics, call `spatial.TryGetObservedUnityPose(out Pose pose)` on the main thread after `Realm.Update()`. It projects Cartesian, geographic and ECEF observations, including custom body axes, through the cached reference and its range limit. False means the observation cannot currently be projected. Reads preserve observations, filters and transforms.

## Run the examples

[Relative world](../Samples~/RelativeWorld/README.md) demonstrates WGS84 input, noisy/delayed observations, a moving reference and attachable bird feet. [Aircraft formation](../Samples~/AircraftFormation/README.md) demonstrates independently timed aircraft and angular presentation. Their guides describe controls and defaults; initializers show SDK-to-trait mappings.
