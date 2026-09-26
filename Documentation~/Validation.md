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
| Editor authoring | Serialized configuration errors, blueprint overrides, reference selection and visible Ghost fields. |
| Samples | Four runnable sample scenes and required consistency between package samples and their imported copies. |

Keep a test when it protects a distinct behavior that an application depends on. Failure-path tests must demonstrate a meaningful recovery or cleanup contract; reproducing a past bug alone is not a reason to add or retain a case. Avoid duplicate permutations, assertions about private state, tests of test helpers, and checks of trivial constants. Cover related inputs in one focused scenario where that makes the contract clearer.

## Results

The reduced suite passed in Unity **2022.3.62f3** on **2026-09-26**, package **0.1.0**. All **142 tests** passed with no failures or skips, down from 315 tests (173 removed, approximately 55%). Both test assemblies compile without access to internal production APIs.

| Editor | Test Framework | EditMode | PlayMode |
| --- | --- | --- | --- |
| 2022.3.62f3 (`96770f904ca7`) | 1.1.33 | 9 passed | 133 passed |

The ignored XML reports and logs are in `Tests/Unity~/TestResults/PublicContracts/`. Every retained test has an XML purpose summary; an independent suite review checked for private/internal access, duplicate purposes and unused probes.

The previous suite passed **16 EditMode** and **299 PlayMode** tests on **2026-09-26**, before this reduction. Earlier **2026-09-22** checks passed on Unity 6.3 LTS (6000.3.24f1) and Windows Mono players; those runs predate later API changes and do not validate the current suite. Unity 6 and player runs have not been repeated for this revision. IL2CPP, other platforms, performance and rendering quality have not been validated.
