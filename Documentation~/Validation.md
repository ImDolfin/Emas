# Validation

## Open the included test project

1. In **Unity Hub**, use **Add project from disk** and select **`Tests/Unity~`** inside this repository.
2. Open it with **Unity 2022.3.62f3** and wait for package import and compilation.
3. Open **Window > General > Test Runner**; choose **Run All** in **EditMode**, then **PlayMode**.

Failure-path tests capture and verify their declared exception messages. Their Test Runner output and exported XML contain short `Verified expected failure` entries. Unexpected errors still reach the Console and fail the test.

The saved project already includes Test Framework, package test registration and imported samples. No manifest editing is needed. Sample scenes are under `Assets/Samples/`. The repository root contains the UPM package; opening that root as a Unity project does not load this configuration.

For standalone validation, select Windows in Build Settings and choose **Run all in player** in Test Runner (Windows Mono build support required). Close other Unity editors during player runs so they cannot intercept the test connection. The prepared project enables full scene and domain reload.

Required project files are Git-tracked; generated output is ignored. The `~` suffix excludes the nested project from package import. After changing `Samples~/`, update the corresponding folder in `Tests/Unity~/Assets/Samples/`; EditMode tests detect differences.

## Test in an existing Unity project

Importing a UPM sample copies sample assets into that project. It does not import the included test project's settings. To run the package tests there, install Test Framework and add `com.emas.core` to the top-level `testables` array in that project's `Packages/manifest.json`, preserving existing entries:

```json
"testables": ["com.emas.core"]
```

Reopen Unity if the tests remain hidden. [Unity requires this opt-in for installed package tests](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-tests.html). Use the included test project above to avoid this manual setup.

## What the tests cover

Each test has a consumer-facing purpose documented in its XML summary. Tests use public Emas APIs, supported protected detector extension points, and Unity's public serialization APIs for Inspector authoring. Runtime and Editor assemblies grant no friend access to test assemblies. Timing tests wait for observable changes using Unity's unscaled clock, including checks while `Time.timeScale` is zero.

| Area | Purpose |
| --- | --- |
| Entities | Typed identity and appearance values, root contracts, and double-precision coordinate behavior. |
| Tracking | Detector ownership and status, SDK data application, identity reuse, cleanup, explicit dispatch, restart, inactivity and disappearance grace. |
| Queries | Filtering and lookups, paired arrival/departure notifications, and subscriptions across live realms. |
| Views | Blueprint selection, reference-frame projection, spatial channels, and presentation availability. |
| Unity setup | Isolated/nested realm ownership, configuration timing, reparenting and automatic lifecycle updates. |
| Editor authoring | Serialized configuration errors, shared realm blueprint mappings, reference selection and visible Ghost fields. |
| Samples | Three runnable sample scenes and required consistency between package samples and their imported copies. |

Keep a test when it protects a distinct behavior that an application depends on. Failure-path tests must demonstrate a meaningful recovery or cleanup contract; reproducing a past bug alone is not a reason to add or retain a case. Avoid duplicate permutations, assertions about private state, tests of test helpers, and checks of trivial constants. Cover related inputs in one focused scenario where that makes the contract clearer.

## Results

Geographic reference projection and native ECEF attitudes passed **18 EditMode** and **157 PlayMode** tests in Unity **2022.3.62f3** on **2026-10-01**, with no failures or skips. Coverage includes WGS84/ECEF conversion, moving tangent axes, date-line and polar positions, double-precision displacement, local and ECEF attitude conversion in both directions, configurable right-handed body axes, independently updated position/attitude channels, mixed attitude inputs, manual and followed references, range limits, reference loss and recovery, and invalid authored coordinates. The Relative World sample now uses direct geographic readings; package and imported sample assets match.

The suites used `-batchmode -nographics`. Separate Direct3D 11 captures of the actual Realm Setup Inspector were visually reviewed: manual Geographic mode shows latitude, longitude and ellipsoidal height; followed mode hides the manual coordinates and supports an empty runtime-assigned Entity ID. The temporary preview helper was removed. Reports, logs, `ManualGeographic.png` and `FollowedGeographic.png` are in `Tests/Unity~/TestResults/GeographicReference/`. Sample camera previews and player/platform runs were not repeated for this change.

Runtime reference selection passed **17 EditMode** and **146 PlayMode** tests in Unity **2022.3.62f3** on **2026-10-01**, with no failures or skips. A blank authored Entity ID now allows Realm startup while spatial presentation waits. Tests cover runtime target assignment and switching, retained coordinate/placement/range settings and views, fresh waiting state after restart, and validation of incomplete nonempty identities. Imported samples remain consistent.

The suites used `-batchmode -nographics`. Direct3D 11 captures of Realm Setup were visually reviewed: the Entity ID is marked optional, Anchor and Kind are disabled while it is empty, and the live frame reports **Waiting for reference**. The temporary preview helper was removed. Reports, logs and captures are in `Tests/Unity~/TestResults/RuntimeReference/`.

Configurable reference coordinates passed **15 EditMode** and **146 PlayMode** tests in Unity **2022.3.62f3** on **2026-10-01**, with no failures or skips. Coverage includes ENU/NED positions and handed rotations, custom signed axes, inverse conversions, large-origin precision, distance limits, position-only following, reference loss, runtime convention changes retaining Ghosts/views, serialization and invalid authored mappings. Imported-sample consistency checks also pass.

The suites ran with `-batchmode -nographics`. A separate Direct3D 11 capture of Realm Setup was visually reviewed for the NED preset and Custom axis selectors. Both layouts render without overlapping fields; custom axes appear only when needed. The temporary preview helper was removed. Reports, logs, `PresetInspector.png` and `CustomInspector.png` are in `Tests/Unity~/TestResults/CoordinateSystems/`.

Inspector cleanup passed **14 EditMode tests** in Unity **2022.3.62f3** on **2026-10-01**, with no failures or skips. Removed Setup Help foldouts and inspector diagnostics buttons, including their unused shared helpers. The diagnostics overlay owns its own window shortcut. This check used `-batchmode -nographics`; reports are in `Tests/Unity~/TestResults/InspectorCleanup/`.

Inline blueprint variants passed **14 EditMode** and **139 PlayMode** tests in Unity **2022.3.62f3** on **2026-10-01**, with no failures or skips. Coverage includes exact named-prefab selection, fallback behavior, independent registration snapshots, switching named LOD variants while retaining Ghost roots, cancellation through Demanifest, invalid row validation, serialized row reordering/removal and undo/redo. All three imported samples match the package after seven separate variant assets were folded into their blueprints.

The test suites ran with `-batchmode -nographics`. A separate Direct3D 11 capture of the actual blueprint Inspector was visually reviewed: Name and View Prefab columns, row handles and add/remove controls render correctly. This capture did not reproduce the earlier GUI shader issue. The temporary preview helper was removed. Reports, logs and `BlueprintTable.png` are in `Tests/Unity~/TestResults/InlineVariants/`.

The architecture and lifecycle diagrams were regenerated with the current variant API wording. Both pass **9/9 showcase checks**, with zero errors or warnings. Browser containment checks pass at 1440?900, 1600?1000, 1920?1080 and 2048?1320; the final large screenshots were visually reviewed in light and dark themes. Delivery receipts, browser receipts, screenshots and the artifact hashes are recorded alongside the test results in `InlineVariants/`.

The reduced sample set and Relative World driving scene passed **140 PlayMode** and **13 EditMode** tests in Unity **2022.3.62f3** on **2026-09-29**, with no failures or skips. Callback-sample cases were removed; the Emas example now validates three cars on one Anchor and SDK replacement. Relative World checks stationary parking encounters on both sides, explicit departure, reference-driven road motion, bounded populations after large time steps, and a bird's complete orbit with position and heading updates while retaining its Ghost and view. All three imported samples match the package assets.

Direct3D-rendered frames at 0, 3, 5, 9 and 14 simulated seconds were inspected, showing the parked cars passing on alternating sides and the bird above the origin. The bird mesh, material, prefab, variant and blueprint are saved assets. The temporary authoring/capture helper was removed. Test reports and preview images are in `Tests/Unity~/TestResults/DrivingSamples/`. These camera previews validate the sample presentation, not the previously reported Inspector GUI shader issue.

Component authoring and inspector simplification passed **139 PlayMode** and **15 EditMode** tests in Unity **2022.3.62f3** on **2026-09-29**, with no failures or skips. The added scenarios cover Anchor-local initialization and Realm fallback, module reads and source rebinding, restart dispatch invalidation, disabled-detector startup, component removal, disappearance grace and failure cleanup. Existing plain C# setup and sample lifecycle tests remain passing. Editor checks confirm all four tracking prefabs contain detector and initializer components and the imported samples match the package. Reports are in `Tests/Unity~/TestResults/ComponentSetup/`.

These runs used `-batchmode -nographics`; they do not validate rendered Inspector appearance. The previous GUI shader/include problem described below has not been retested or fixed by this change.

Inspector and diagnostics changes passed **15 EditMode tests** in Unity **2022.3.62f3** on **2026-09-29**. This includes authored positive/finite visibility-range validation, passive construction of diagnostics/overlay UI without advancing a code-managed Realm, prefab configuration and sample consistency. The headless run validates UI construction but cannot render the live window; the test also opens the window when run with a graphics device. Logs are in `Tests/Unity~/TestResults/InspectorUX/`. A temporary review window exercised followed-reference, manual-reference and identity settings without changing their serialized values. Visual approval remains pending: this Unity installation failed to compile its built-in GUI shaders (`HLSLSupport.cginc` include errors), so native and Unity screenshot captures were magenta. The temporary helper and review process were removed. PlayMode was not rerun for that earlier Editor-only revision; the component-authoring results above are now the latest runtime validation.

Realm-only blueprint mappings passed in Unity **2022.3.62f3** on **2026-09-29**, package **0.1.0**. All **145 tests** passed with no failures or skips. PlayMode verifies shared mappings across anchors, replacement views without replacing existing roots, mappings surviving anchor disposal, and independent snapshots across realms. Obsolete Anchor override tests were replaced with the current contracts. EditMode verifies that authored anchors share their Realm Setup blueprint while retaining independent automatic-view settings, and checks all imported sample assets against the package. The existing Ghost update, spatial and sample lifecycle tests also pass.

| Editor | Test Framework | EditMode | PlayMode |
| --- | --- | --- | --- |
| 2022.3.62f3 (`96770f904ca7`) | 1.1.33 | 13 passed | 132 passed |

The ignored XML reports and logs are in `Tests/Unity~/TestResults/RealmBlueprints/`. The preceding Ghost-update revision passed **13 EditMode** and **133 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/GhostUpdates/`. The preceding Ghost-composition revision passed **13 EditMode** and **128 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/GhostComposition/`. The preceding identity-frame revision passed **13 EditMode** and **127 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/IdentityFrame/`. The preceding weak-source revision passed **13 EditMode** and **124 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/PresenceSource/`. The preceding Ghost-module migration passed **13 EditMode** and **121 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/GhostModules/`. The shipped Ghost prefabs contain saved module components and require no asset-generation helper.

The preceding authored-sample revision passed **13 EditMode** and **117 PlayMode** tests on **2026-09-26**. Those results and its Direct3D 11 camera previews remain in `Tests/Unity~/TestResults/AuthoredSamples/`. This module migration did not change sample geometry or presentation assets beyond adding components to Ghost prefabs; camera previews were not recaptured.

The previous internal identity-map and dispatch extraction passed **9 EditMode** and **114 PlayMode** tests on **2026-09-26**; those reports remain in `Tests/Unity~/TestResults/IdentityAndCommands/`. The earlier adapter removal passed **9 EditMode** and **112 PlayMode** tests in `Tests/Unity~/TestResults/DetectorSimplification/`. Earlier **2026-09-22** checks passed on Unity 6.3 LTS (6000.3.24f1) and Windows Mono players; those runs predate later API changes and do not validate the current suite. Unity 6 and player runs have not been repeated for this revision. IL2CPP, other platforms, performance and alternative render pipelines have not been validated.
