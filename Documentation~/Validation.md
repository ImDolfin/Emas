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
| Samples | Four runnable sample scenes and required consistency between package samples and their imported copies. |

Keep a test when it protects a distinct behavior that an application depends on. Failure-path tests must demonstrate a meaningful recovery or cleanup contract; reproducing a past bug alone is not a reason to add or retain a case. Avoid duplicate permutations, assertions about private state, tests of test helpers, and checks of trivial constants. Cover related inputs in one focused scenario where that makes the contract clearer.

## Results

Realm-only blueprint mappings passed in Unity **2022.3.62f3** on **2026-09-29**, package **0.1.0**. All **145 tests** passed with no failures or skips. PlayMode verifies shared mappings across anchors, replacement views without replacing existing roots, mappings surviving anchor disposal, and independent snapshots across realms. Obsolete Anchor override tests were replaced with the current contracts. EditMode verifies that authored anchors share their Realm Setup blueprint while retaining independent automatic-view settings, and checks all imported sample assets against the package. The existing Ghost update, spatial and sample lifecycle tests also pass.

| Editor | Test Framework | EditMode | PlayMode |
| --- | --- | --- | --- |
| 2022.3.62f3 (`96770f904ca7`) | 1.1.33 | 13 passed | 132 passed |

The ignored XML reports and logs are in `Tests/Unity~/TestResults/RealmBlueprints/`. The preceding Ghost-update revision passed **13 EditMode** and **133 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/GhostUpdates/`. The preceding Ghost-composition revision passed **13 EditMode** and **128 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/GhostComposition/`. The preceding identity-frame revision passed **13 EditMode** and **127 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/IdentityFrame/`. The preceding weak-source revision passed **13 EditMode** and **124 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/PresenceSource/`. The preceding Ghost-module migration passed **13 EditMode** and **121 PlayMode** tests; those reports remain in `Tests/Unity~/TestResults/GhostModules/`. The shipped Ghost prefabs contain saved module components and require no asset-generation helper.

The preceding authored-sample revision passed **13 EditMode** and **117 PlayMode** tests on **2026-09-26**. Those results and its Direct3D 11 camera previews remain in `Tests/Unity~/TestResults/AuthoredSamples/`. This module migration did not change sample geometry or presentation assets beyond adding components to Ghost prefabs; camera previews were not recaptured.

The previous internal identity-map and dispatch extraction passed **9 EditMode** and **114 PlayMode** tests on **2026-09-26**; those reports remain in `Tests/Unity~/TestResults/IdentityAndCommands/`. The earlier adapter removal passed **9 EditMode** and **112 PlayMode** tests in `Tests/Unity~/TestResults/DetectorSimplification/`. Earlier **2026-09-22** checks passed on Unity 6.3 LTS (6000.3.24f1) and Windows Mono players; those runs predate later API changes and do not validate the current suite. Unity 6 and player runs have not been repeated for this revision. IL2CPP, other platforms, performance and alternative render pipelines have not been validated.
