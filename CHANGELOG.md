# Changelog

## 0.2.0

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
