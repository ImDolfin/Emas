# Validation

## Open the included test project

1. In **Unity Hub**, use **Add project from disk** and select **`Tests/Unity~`** inside this repository.
2. Open it with **Unity 2022.3.62f3** and wait for package import and compilation.
3. Open **Window > General > Test Runner**; choose **Run All** in **EditMode**, then **PlayMode**.

The saved project already includes Test Framework, package test registration and imported samples. No manifest editing is needed. Sample scenes are under `Assets/Samples/`. The repository root contains the UPM package; opening that root as a Unity project does not load this configuration.

For standalone validation, select Windows in Build Settings and choose **Run all in player** in Test Runner (Windows Mono build support required). Close other Unity editors during player runs so they cannot intercept the test connection. The prepared project enables full scene and domain reload.

Required project files are Git-tracked; generated output is ignored. The `~` suffix excludes the nested project from package import. After changing `Samples~/`, update the corresponding folder in `Tests/Unity~/Assets/Samples/`; EditMode tests detect differences.

## Test in an existing Unity project

Importing a UPM sample copies sample assets into that project. It does not import the included test project's settings. To run the package tests there, install Test Framework and add `com.emas.core` to the top-level `testables` array in that project's `Packages/manifest.json`, preserving existing entries:

```json
"testables": ["com.emas.core"]
```

Reopen Unity if the tests remain hidden. [Unity requires this opt-in for installed package tests](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-tests.html). Use the included test project above to avoid this manual setup.

## Results

Verified on **2026-09-22**, package **0.1.0**, Windows Mono:

| Editor | Test Framework | EditMode | PlayMode | Windows player |
| --- | --- | --- | --- | --- |
| 2022.3.62f3 (`96770f904ca7`) | 1.1.33 | 9 passed | 147 passed | 147 passed |
| 6000.3.24f1 / Unity 6.3 LTS (`4e7b9b5b6244`) | 1.6.0 | 9 passed | 147 passed | 147 passed |

No failed or skipped tests in the completed suites. Unity 2022 used the prepared repository project; Unity 6 used an isolated copy of its Assets, Packages and ProjectSettings. The repository project remains on Unity 2022.3.

Coverage includes source status and primary-error retention, startup/update/dispatch failures, cleanup, stale callbacks and restart, replacement recovery, read-only snapshots, shared Inspector validation and passive diagnostics. Player tests exercise both quick starts and the larger example: views, explicit removal, disable/re-enable cleanup, interface consumption and stable identities across replacement. EditMode checks also detect differences between shipped and imported samples.

A fresh Unity 2022.3 consuming project imported all three samples through Package Manager with its manifest unchanged and no `testables` entry. All **3 sample smoke tests passed**. Package test opt-in remains separate from ordinary installation.

Reports and logs from this run are in the ignored `Tests/Unity~/TestResults/Completion/` directory. Required test inputs are tracked. Player checks verify behavior, not rendering quality; IL2CPP, other platforms and performance were not tested.
