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
| Entities | Typed identity and appearance values, root contracts, immutable module membership snapshots with component additions/removals, and double-precision coordinate behavior. |
| Tracking | Detector ownership and status, SDK data application, identity reuse, entity-ID root naming and Unity lookup, cleanup, explicit dispatch, restart, inactivity and disappearance grace. |
| Queries | Filtering and lookups, paired arrival/departure notifications, and subscriptions across live realms. |
| Views | Blueprint selection, reference-frame projection, independent spatial channels, geographic yaw/pitch/roll with preserved separation, late-parent attachment chains and recovery, detach handoff, input failures and presentation availability. |
| Unity setup | Isolated/nested realm ownership, configuration timing, reparenting and automatic lifecycle updates. |
| Editor authoring | Serialized configuration errors, shared realm blueprint mappings, reference selection and visible Ghost fields. |
| Samples | Three runnable sample scenes and required consistency between package samples and their imported copies. |

Keep the suite small, with thorough scenarios around important public API contracts and boundaries. Each test must protect a distinct behavior that an application depends on; extend an existing scenario when it can cover the contract clearly. Failure-path tests must demonstrate a meaningful recovery or cleanup contract; reproducing a past bug alone is not a reason to add or retain a case. Avoid blanket member coverage, duplicate permutations, assertions about private state, tests of test helpers, and checks of trivial constants. Keep the required imported-sample consistency checks.
