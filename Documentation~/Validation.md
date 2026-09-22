# Validation

Validated on **Unity 2022.3.62f3**, package **0.1.0**, on 2026-09-22.

| Check | Result |
| --- | --- |
| Unity PlayMode suite | 85 passed, 0 failed, 0 skipped |
| Editor package asset tests | 3 passed; package scripts resolve their declared types |
| Quick-start assets | Scene, blueprint and prefab create the configured view |
| Portable C# 8 runtime/sample compile | 0 warnings/errors |
| Portable value/query/API checks | 25 passed |
| Invalid kind/variant calls | 8 expected compiler errors |
| Windows Mono sample build | Succeeded |
| Quick-start player smoke | Exit 0; view creation, disable cleanup and re-enable restart passed |
| Architecture and lifecycle diagrams | Each 9/9 showcase checks; desktop containment and light/dark visual review passed |

Regression coverage includes lifecycle callbacks, replacement, scene unload, update ordering, dispatch, polling configuration, snapshot failures/departures, Inspector ownership, automatic views and startup from another component's enable callback.

**Limits:** headless smoke uses a null graphics device and reports unsupported shader messages; it does not validate rendering. IL2CPP, other platforms and performance timings were not tested. Diagram export interactions were not separately tested.

Run package tests through Unity's Test Runner. The optional ignored `TestProject~/` contains the validation consumer, reports under `TestResults/`, and current validation receipts under `ApiClarityWork/`. Previous work records are archived under `DocumentationArchive/2026-09-21/`. These local artifacts are not required to use the package.
