# Changelog

## 0.3.0

- Reuse live spatial and processing component lookups, and prepare shared signed-axis mappings for Mono updates. Preserve runtime component additions and replacements, serialized axes and handedness.
- Add brief README examples for custom extrapolation strategies and filters, including registration, execution order and state reset guidance.
- Remove the redundant Extrapolation prediction-timing mode row. Rename its Inspector field to Prediction Interval and explain automatic calculation at zero in the tooltip.
- Add drag grips and insertion markers to Ghost Inspector trait cards. Reordering moves real Unity components, preserves settings and references, supports Undo/Redo and persists filter order in prefabs.
- Move observation processing before extrapolation. Add the extensible Filter trait with timestamped pose/motion state, ordered per-Ghost filtering and separate filtered history. Make Smoothing a Filter and move packet correction out of turn strategies into PacketCorrection. Preserve SDK input, shared timing, batch observations and attached/distant bypasses; include filter age without spending the packet-gap prediction budget.

- Replace separate strategy displacement/rotation calls with one Predict(PredictionRequest) call returning a complete Prediction. Share the result and its velocities across packet correction and smoothing; use cached presentation motion for live tuning instead of evaluating strategies again. Preserve independent position/rotation times and attached/distant bypasses.
- Make Spatial the sole SDK input API for pose, velocity, acceleration and angular velocity. Move raw motion/history ownership off Extrapolation, retain inputs across prediction removal, and let Smoothing use supplied motion without Extrapolation. Update sample data traits and public contract coverage to the consolidated API.
- Bypass prediction, packet-correction previews and smoothing for attached Ghosts. Reset processing only at attachment transitions, ignore unused attached timestamps when choosing the Realm clock, and resume from current Spatial inputs on Detach.
- Group automatic views, view distance and processing distance under Realm Setup > Presentation. Apply them across all Anchors with live editing; use one automatic-view subscription per Realm. Move the processing-radius API to Realm.PoseProcessingDistance and simplify Anchor Setup to identity and detector connections. Update the authored/imported samples to the new scope.
- Make the followed Ghost's fallback control Realm timing: zero restores automatic clock compensation; a positive value replaces it with a shared manual receipt-based interval. Allow smaller manual targets, sample newer peers from history, retain packet-age diagnostics and keep per-entity prediction caps.
- Show the shared automatic packet-age estimate in milliseconds beside the projected time in the Ghost Inspector. Expose the cached calibrated receipt age through Realm.EstimatedPacketAge. Keep fallback prediction editable during Play Mode, including when the reference timeline overrides it.
- Update the package and runtime metadata version to 0.3.0.

## 0.2.0

- Set the minimum prediction limit to 0.007 s (7 ms, rounded up from one 144 Hz frame) in the API, Inspector and saved settings. Smaller nonnegative limits clamp to the minimum; disable the Extrapolation trait to stop prediction.
- Replace prediction explanation boxes with a compact timing row and label tooltips. Show buffered or over-limit intervals beside their values instead of separate warning boxes. Clarify that zero fallback lead preserves automatic prediction.
- Hide Ghost identity, spatial pose, pose-debugger controls and live timing readouts outside Play Mode. Defer trait component-card visibility changes until after Inspector callbacks to avoid rebuilding Unity's editor list during trait insertion or drawing; retain serialized settings and Undo.
- Explain automatic reference prediction in each Ghost's Extrapolation Inspector. Show the shared projected time, independent sample-to-target intervals, inactive processing states and prediction-limit warnings. Hide the unused individual forward control during shared timing and label it as a fallback otherwise. Keep reference clock settings code-only.
- Reduce many-entity update overhead with direct identity membership checks, reader-only trait guards, shared geographic input conversions and cached tangent bases. Reuse built-in motion preparation across prediction previews and unchanged input, retaining independent channel times, live history/settings changes, callback safety and zero warmed allocations.
- Add Constant Turn, Climb and Acceleration (CTCA): horizontal turns with tangential acceleration and independent constant climb rate. Infer missing course/motion from timestamped history, retain full attitude prediction, and prevent pure roll/pitch or vertical acceleration from bending the climb trajectory. Reuse shared timing, caps, packet correction and smoothing; keep Gravity hidden.
- Add Constant Turn and Acceleration (CTA), extending rotating-body turns with history-derived body acceleration and endpoint velocity. Preserve supplied inertial acceleration and SDK velocity overrides, reuse coordinated-turn geometry and packet correction, and keep Gravity hidden in the Inspector.
- Add SplineExtrapolation with timestamp-fitted cubic Bézier continuation for position and quaternion rotation. Use supplied velocity histories when available, preserve SDK derivative overrides, reduce curve order for sparse input, and retain shared timing, caps, smoothing and reset behavior without new Inspector settings.
- Hide the Constant Turn gravity setting in the Inspector while retaining its code API and serialized value.
- Fix Constant Turn predicting from a lagging position chord. Reconstruct endpoint velocity from bounded timestamped history using the rotating-body model, including supplied inertial acceleration. Support cold packet batches and reset windows while preserving supplied velocity and zero overrides.
- Make Constant Acceleration estimate missing linear and angular acceleration from timestamped position, orientation and SDK rate histories. Fit irregular samples in a bounded window, preserve supplied motion and zero-rate overrides, integrate body/world angular changes, and expose read-only strategy history access without new UI or SDK setters.
- Keep shared clock policy in the code-only ReferenceFrame.TimestampClock, ClockOffset and PredictionTime APIs. Ghosts supply timestamped data; the reference applies calibration once across the Realm and selects the shared target independently of Ghost processing traits.
- Automatically compensate Unix creation timestamps on the followed Ghost against UTC receipt time using rolling medians. Expose the shared Realm.ProjectedTime while retaining individual models and caps. Support automatic Unix recognition, explicit UTC and arbitrary simulation epochs; reset alignment on reference changes and clock restarts.
- Compensate angular smoothing with known model motion so sustained turns do not require extra prediction lead. Keep packet correction movement separate from motion assistance so half-life filters still reduce correction error when cubic correction is enabled.
- Reduce steady-state update allocations by reusing population snapshots, Anchor/source tick buffers and detector callbacks. Avoid duplicate query component checks, repeated motion sampling and geographic tangent-axis calculations while retaining live component and input changes.
- Add Unity Profiler markers for the Realm update, trait readers, Ghost behavior, projection, views and query notifications.
- Add per-Anchor PoseProcessingDistance with live authoring controls. Distant entities project their latest raw pose without buffering, prediction, packet correction or smoothing; reference processing and visibility remain independent, and filters restart cleanly on return.
- Add Extrapolation.PredictionTime as actual forward prediction shared by all existing strategies; distinguish it from the prediction limit and count known packet age once. Cover sustained constant-rate flight with uniform transport delay and the 0.2-to-0.4 prediction contract.
- Show shared playback delay in pose diagnostics and observed/reference geographic bank in the Ghost Inspector; clarify immediate bank/pitch cancellation, view frames, prediction limits and input attitude direction.
- Preserve angular prediction age when readers repeat an unchanged untimed attitude; recognize small attitude and representation changes as fresh input.
- Reuse the captured reference presentation when placing its root, avoiding a second prediction/filter preparation in the same projection pass.
- Clarify that target pose comparisons exclude followed-reference processing; guide reference-angle and extrapolation checks for distant cockpit targets.
- Turn Window/Emas into a pose debugger with a dedicated Scene tool, shared overlay controls, pinned/followed targets, framing and observed/presented pose readouts.
- Add observed/presented pose comparison gizmos with source timestamps and position/angular differences; expose passive observed-pose projection through Spatial.TryGetObservedUnityPose.
- Fix coordinated-turn packet correction underestimating motion at fresh observation boundaries, preventing angular tracking lag during rapid maneuvers.
- Fix coordinated-turn packet correction retaining stale linear/angular motion when observations arrive every presentation frame; advance interrupted correction before rebasing so continuous streams converge.
- Separate source-space playback, prediction, packet correction and filtering into an internal PosePresenter; Spatial retains input validation, attachments and transform projection.
- Prevent duplicate Ghost markers on one GameObject, keeping root identity and trait ownership unambiguous.
- Streamline the README, API, spatial, architecture and sample guides. Keep concise caller contracts in XML comments and detailed examples in the guides.
- Add missing coordinate/sample tooltips, explicit motion units in the Ghost Inspector, grouped aircraft controls and clearer diagnostics labels.
- Sort and shorten strategy choices, indent model settings, and keep compatible settings editable during multi-object selection.
- Dispose temporary blueprint serialized state; strengthen authoring coverage and remove timing/measurement ambiguity from live-tuning tests.
- Refresh diagram source references and use package-specific architecture legend labels.

## 0.1.0

- Add Unity entity tracking with Realm, Anchor, stable Presence handles, Ghost roots and optional views. Realm.Default updates automatically; isolated Realms support explicit ownership and updates.
- Separate SDK membership detection from source-independent Trait readers. Support scene-local and Realm-wide initializers, weak application Source references and capability metadata.
- Provide callback-safe startup, cleanup, bounded dispatch, captured attachment dispatchers, explicit restart/replacement, rollback and compatible identity handover.
- Support inactivity expiry and disappearance grace, retaining unavailable handles for recovery without treating trait reads as source activity.
- Add immutable queries for Kind, Anchor, display name, Variant and unique root contracts; exact identity lookup; paired observations; and global queries with Realm-aware identity.
- Add per-Realm blueprint snapshots with authored Ghost prefabs, inline named view variants and optional fallbacks. Keep tracking available when a view fails, with explicit retry.
- Add double-precision Cartesian, WGS84 and ECEF Spatial input, signed coordinate mappings, independent position/attitude channels and fixed/followed reference frames.
- Support full reference-attitude cancellation, presentation range suppression and same-Realm entity attachments with independent root lifetimes.
- Add shared timestamped buffering, SDK clock alignment/reset, bounded linear/angular prediction and independent position/rotation smoothing half-lives.
- Add constant-acceleration, constant-velocity, Singer, coordinated-turn and hold strategies, with IExtrapolationStrategy and IPacketSmoothingStrategy extension contracts.
- Accept independently timed linear motion and body/world angular velocity. Preserve supplied-zero overrides, rate provenance, raw observations and per-root prediction/correction state.
- Compensate position filtering with SDK motion, including after earlier packets reach the prediction cap. Blend live setting changes without discarding observations or freezing normal movement.
- Add root-only component/trait access with descriptive missing/ambiguous diagnostics, immutable membership snapshots and direct Anchor/Realm context.
- Add grouped scene/blueprint inspectors, exclusive Ghost trait authoring, Undo/prefab editing, passive diagnostics and a Scene view overlay.
- Ship Quick start, Example, Relative world and Aircraft formation samples with authored scenes/prefabs, SDK mappings, query consumers and motion comparisons.
- Provide an included Unity test project, command-line/CI validation and focused lifecycle, spatial, failure/recovery, authoring and sample-consistency coverage.
- Fix delayed strategy-menu selection retaining disposed inspector state. Apply selections through fresh serialized state so selection, Undo and prefab editing remain usable.
- Exclude destroyed roots from queries, preserve shared projection during view requests and contain presentation failures independently of detection.
- Keep runtime independent of Editor/SDK/sample assemblies, retain Unity asset GUIDs through API cleanup, and target C# 8 on Unity 2022.3+.

Licensed under MIT.
