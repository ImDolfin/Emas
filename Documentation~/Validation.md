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

## Results

The full Unity **2022.3.62f3** test project passed **16 EditMode** and **277 PlayMode** tests on **2026-09-26**, package **0.1.0**. This includes imported sample consistency, the serialized `ManifestationVariant` asset in the Callbacks scene, both quick-start scenes, isolated and nested prefab realms, manifestation blueprint defaults and anchor overrides, reference frames, the geodetic Relative world sample, startup cleanup and queries across live realms. The ignored XML and logs are in `Tests/Unity~/TestResults/GeoRelativeWorldFull/`.

| Editor | Test Framework | EditMode | PlayMode |
| --- | --- | --- | --- |
| 2022.3.62f3 (`96770f904ca7`) | 1.1.33 | 16 passed | 277 passed |

Focused validation of the updated **Relative world** sample passed 1 PlayMode scene test on the same date; the ignored report is in `Tests/Unity~/TestResults/GeoRelativeWorld/`. Earlier sample-split reports are in `Tests/Unity~/TestResults/RelativeWorldSplit/`.

The previous **2026-09-22** Windows Mono player runs passed 184 tests on Unity 2022.3.62f3 and Unity 6.3 LTS (6000.3.24f1). Unity 6 also passed 9 EditMode and 184 PlayMode tests then. Those runs preceded the blueprint, subscription performance, realm, Ghost developer-experience, Anchor, PresenceSource, Blueprint snapshot, presentation isolation, ghost lifetime and relative-world improvements; the player and Unity 6 suites have not been rerun for these changes. No failed or skipped tests were reported. Unity 2022 used the prepared repository project; Unity 6 used isolated copies of its Assets, Packages and ProjectSettings. The repository project remains on Unity 2022.3.

Coverage includes detector and Ghost lifetimes, failure cleanup and recovery, stale callbacks, query observations, identity lookup, polling intervals and callback ordering/budgets. New Presence tests cover typed capabilities, stable handles, Realm initializers, EntityModule SDK updates, silent defaults, manifestation by Presence, and disappearance grace. The imported polling and callback quick starts exercise prefab realm configurators. The current suite also verifies presentation failure isolation, bounded source handover, inactivity expiry after partial publications and registration-safe scene cleanup. Spatial coverage includes double-precision projection at large coordinates, independent position and rotation updates, reference following and recovery, transformed parents, range suppression and restoration, activation ordering, and the relative-world sample. Earlier player tests exercise the samples available at the time; EditMode checks cover Inspectors, passive diagnostics and shipped/imported sample consistency. Expected-error capture has dedicated logging regressions.

The earlier fresh Unity 2022.3 consuming project imported the three samples available at the time through Package Manager with its manifest unchanged and no `testables` entry. All **3 sample smoke tests passed**. That check preceded the relative-world example. Package test opt-in remains separate from ordinary installation.

The current Unity 2022 editor reports are in the ignored `Tests/Unity~/TestResults/GeoRelativeWorldFull/` directory. Earlier Presence pipeline reports are in `TestResults/PresencePipeline/`. The earlier global-query reports are in `TestResults/GlobalQuery/`. Earlier prefab realm reports are in `TestResults/RealmSetup/`. Earlier spatial reports are in `TestResults/SpatialFrames/`. Earlier presentation and lifetime reports are in `TestResults/PresentationAndLifetime/`; focused presentation regressions are in `TestResults/ViewFailureIsolation/`. Earlier Blueprint reports are in `TestResults/BlueprintSnapshots/`, PresenceSource reports are in `TestResults/PresenceSourceImprovements/`, Anchor reports are in `TestResults/AnchorImprovements/`, Ghost reports are in `TestResults/GhostDeveloperExperience/`, realm reports are in `TestResults/RealmImprovements/`, subscription reports in `TestResults/SubscriptionPerformance/`, blueprint reports in `TestResults/BlueprintHandling/`, earlier suites in `TestResults/TestCleanup/`, and fresh-install reports in `TestResults/Completion/`. Required test inputs are tracked. Player checks verify behavior, not rendering quality; IL2CPP, other platforms and performance were not tested.
