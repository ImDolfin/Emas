# Validation

## Open the included test project

1. In **Unity Hub**, use **Add project from disk** and select **`Tests/Unity~`** inside this repository.
2. Open it with **Unity 2022.3.62f3** and wait for package import and compilation.
3. Open **Window > General > Test Runner**; choose **Run All** in **EditMode**, then **PlayMode**.

The saved project already includes Test Framework, package test registration and imported samples. No manifest editing is needed. Sample scenes are under `Assets/Samples/`. The repository root contains the UPM package; opening that root as a Unity project does not load this configuration.

For standalone validation, select Windows in Build Settings and choose **Run all in player** in Test Runner (Windows Mono build support required).

Required project files are Git-tracked; generated output is ignored. The `~` suffix excludes the nested project from package import. After changing `Samples~/`, update the corresponding folder in `Tests/Unity~/Assets/Samples/`; EditMode tests detect differences.

## Test in an existing Unity project

Importing a UPM sample copies sample assets into that project. It does not import the included test project's settings. To run the package tests there, install Test Framework and add `com.emas.core` to the top-level `testables` array in that project's `Packages/manifest.json`, preserving existing entries:

```json
"testables": ["com.emas.core"]
```

Reopen Unity if the tests remain hidden. [Unity requires this opt-in for installed package tests](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-tests.html). Use the included test project above to avoid this manual setup.

## Results

Verified on **Unity 2022.3.62f3**, package **0.1.0**, on 2026-09-22:

| Setup | EditMode | PlayMode | Windows Mono player |
| --- | --- | --- | --- |
| Separate consuming project | 3 passed | 133 passed | Not repeated |
| Repository project, including samples | 6 passed | 135 passed | 135 passed |

No failed or skipped tests. A consuming project without `testables` imported successfully but discovered zero Emas tests.

Coverage includes identity, queries, views, source replacement, scene cleanup, polling failures, callback ordering/threading, stale subscriptions, unsubscribe failures and sample disable/re-enable behavior. Player checks verify behavior, not rendering quality. IL2CPP, other platforms and performance were not tested.
