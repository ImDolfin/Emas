# Validation

## Open the included test project

1. In **Unity Hub**, use **Add project from disk** and select **`Tests/Unity~`** inside this repository.
2. Open it with **Unity 2022.3.62f3** and wait for package import and compilation.
3. Open **Window > General > Test Runner**; choose **Run All** in **EditMode**, then **PlayMode**.

Failure-path tests capture and verify their declared exception messages. Their Test Runner output and exported XML contain short `Verified expected failure` entries. Unexpected errors still reach the Console and fail the test.

The saved project already includes Test Framework, package test registration and imported samples. No manifest editing is needed. Sample scenes are under `Assets/Samples/`. The repository root contains the UPM package; opening that root as a Unity project does not load this configuration.

For standalone validation, select Windows in Build Settings and choose **Run all in player** in Test Runner (Windows Mono build support required). Close other Unity editors during player runs so they cannot intercept the test connection. The prepared project enables full scene and domain reload.

Required project files are Git-tracked; generated output is ignored. The `~` suffix excludes the nested project from package import. After changing `Samples~/`, update the corresponding folder in `Tests/Unity~/Assets/Samples/`; EditMode tests detect differences.

## Command-line validation

On Windows, install and activate Unity **2022.3.62f3**, close the included project in any other Unity editor, and run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools~\Validate.ps1
```

The execution-policy override applies only to this PowerShell process; it does not change the machine's saved policy. The script runs EditMode and PlayMode sequentially against `Tests/Unity~`. It discovers the standard Windows Unity Hub installation; use `-UnityPath 'D:\Unity\2022.3.62f3\Editor\Unity.exe'` or the `UNITY_PATH` environment variable for a different location. Use `-TestPlatform EditMode` or `-TestPlatform PlayMode` to run one suite.

Each selected suite replaces its XML report and editor log in `TestResults/Validation~/`. The command waits for Unity to exit, attempts both selected suites even if one fails, and returns a nonzero exit code for editor errors, missing or malformed XML, unsuccessful results, or zero discovered tests. Generated reports stay out of version control. The `~` suffix excludes validation output and `Tools~` from Unity package import, so growing logs cannot trigger repeated imports.

## GitHub Actions

[The validation workflow](../.github/workflows/validation.yml) runs the same script on pushes, pull requests from branches in this repository, and manual dispatch. It uses a **self-hosted Windows x64 runner** with the custom label `unity-2022.3.62f3` and an installed, activated Unity **2022.3.62f3** editor. Register a current GitHub Actions runner under **Settings > Actions > Runners**, add that label, and ensure the account running the runner can use the activated editor. The pinned actions require runner version **2.327.1 or newer**. Install Unity in the standard Hub location or set `UNITY_PATH` in the runner's environment before starting it.

Both suites run sequentially, and XML reports and editor logs are uploaded even when a suite fails. No Unity account credentials or license files are passed to a third-party action. A matching runner must be online before jobs can execute. CI covers editor EditMode and PlayMode; standalone player validation remains the manual step described above.

The workflow skips fork pull requests, but contributors can modify workflow files. Restrict this self-hosted runner to trusted code through repository access and workflow-approval settings; the YAML condition alone is not a security boundary. Review external changes before running them on the machine. See [GitHub's self-hosted runner guidance](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners).

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
| Entities | Typed identity and appearance values, root contracts with missing/duplicate component diagnostics and root-only lookup, trait-to-Ghost access while inactive or disabled, immutable trait membership snapshots with component additions/removals, double-precision coordinate behavior, and nanosecond-preserving SDK sample intervals/order. |
| Tracking | Detector ownership and status, SDK data application, identity reuse, partial-ID LINQ searches over pending roots during initialization, read-only root snapshots across removal/disposal, entity-ID root naming and Unity lookup, cleanup, explicit dispatch, restart, inactivity and disappearance grace. |
| Queries | Filtering and lookups, exclusion of destroyed roots from results and contract filters, paired arrival/departure notifications, and subscriptions across live realms. |
| Views | Blueprint selection, reference-frame projection, independent spatial channels, optional Smoothing/Prediction behavior Traits with half-life controls and preserved channel history, shared buffered position/rotation playback including burst observations, shared timestamp alignment with duplicate/stale packet handling and SDK clock restart, geographic peer and attachment alignment on the buffered timeline, velocity estimation and bounded SDK velocity/acceleration and angular gap prediction, ENU motion conversion at an explicit tangent origin, geographic yaw/pitch/roll with preserved separation, smoothed reference following and immediate reference rotation reprojection, late-parent attachment chains and recovery, detach handoff, input failures and presentation availability. |
| Unity setup | Isolated/nested realm ownership, initializer and detector Anchor/Realm context before startup and through cleanup, restart, direct disposal, reparenting and automatic lifecycle updates. |
| Editor authoring | Serialized configuration errors, shared realm blueprint mappings, reference selection, visible Ghost fields, exclusive trait controls with owner-removal recovery, serialized settings and enabled-state Undo/Redo, trait prefab persistence, and continuous live spatial tuning against equivalent property changes, including zero settings, toggles and repeated edits on moving references. |
| Samples | Three runnable sample scenes, source-clock continuity across slow Editor frames, explicit manual stepping, independent smoothing of the noisy 360 km/h bird with prediction disabled and held-packet convergence, buffered versus immediate jitter/noise measurements with 60 Hz observations and requested 90 Hz presentation, abrupt stop/restart alignment, and required consistency between package samples and their imported copies. |

Keep the suite small, with thorough scenarios around important public API contracts and boundaries. Each test must protect a distinct behavior that an application depends on; extend an existing scenario when it can cover the contract clearly. Failure-path tests must demonstrate a meaningful recovery or cleanup contract; reproducing a past bug alone is not a reason to add or retain a case. Avoid blanket member coverage, duplicate permutations, assertions about private state, tests of test helpers, and checks of trivial constants. Keep the required imported-sample consistency checks.
