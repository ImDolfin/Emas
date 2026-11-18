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

Tests exercise public Emas APIs, supported detector extension points and Unity serialized authoring. They do not use friend assemblies or reflection into private package state. Each test's XML summary states its consumer contract.

| Area | Contracts |
| --- | --- |
| Entities | Identity, variants, root-only contract lookup, duplicate/missing diagnostics, inactive trait access, membership snapshots and double-precision/time values. |
| Tracking | Detection, initialization, availability, inactivity/grace, dispatch generations, bounded queues, rollback, explicit recovery and compatible handover. |
| Queries | Available-root filtering, immutable descriptions, global Realm scope, paired membership notifications and subscription cleanup. |
| Views | Blueprint snapshots, named/fallback appearance selection, requested-view lifetime, isolated presentation failures and shared projection during manifestation. |
| Spatial | Coordinate handedness, WGS84/ECEF conversion, independent pose channels, followed references, attachments, range suppression and recovery. |
| Motion | Shared timestamped playback, packet ordering and clock restart; bounded strategy predictions; supplied/estimated rate provenance; smoothing and SDK motion assistance; live tuning; Singer integration; rotating-body trajectories; high-rate followed-reference bearings and reversed-rate controls; cubic correction and per-root isolation. |
| Unity setup | Realm/Anchor ownership, provider/initializer context, startup, restart, disable/reparent behavior and automatic updates. |
| Editor authoring | Configuration validation, visible root fields, exclusive trait controls, multi-object strategy layout, Undo/Redo, prefab persistence and live tuning. |
| Samples | Runnable authored scenes, SDK replacement, scoped query consumers, geographic and aircraft motion under delay/jitter/noise, and byte-for-byte imported-sample consistency. |

Keep coverage focused on distinct contracts and meaningful failure/recovery boundaries. Strengthen existing scenarios when possible. Avoid private helpers, trivial constants, redundant permutations and removed implementation behavior. Imported-sample consistency checks are required.
