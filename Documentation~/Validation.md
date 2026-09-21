# Validation

Validated on **Unity 2022.3.62f3**, package **0.1.0**, on 2026-09-21.

| Check | Result |
| --- | --- |
| Unity PlayMode suite | 61 passed, 0 failed, 0 skipped |
| Editor package asset tests | 3 passed; package scripts resolve their declared types |
| Imported sample scene | One Bootstrap; no missing scripts |
| Portable C# 8 runtime/sample compile | 0 warnings/errors |
| Portable value/query/API checks | 25 passed |
| Invalid kind/variant calls | 8 expected compiler errors |
| Windows Mono sample build | Succeeded |
| Five-second headless smoke | Exit 0; no Emas lifecycle exception |
| Architecture and lifecycle diagrams | Each 9/9 showcase checks; desktop containment and light/dark visual review passed |

Regression coverage includes attachment, disposal, callback mutation, failed replacement, scene unload, view/notification ordering, typed queries, bounded dispatch, automatic default updates and recreation after default-context disposal.

**Limits:** headless rendering reports two unsupported sprite-shader errors from the null graphics device. IL2CPP, other platforms and performance timings were not tested. Diagram export interactions were not separately tested.

Run package tests through Unity's Test Runner. The optional ignored `TestProject~/` contains the validation consumer, reports under `TestResults/`, and current validation receipts under `NamingWork/`. Previous work records are archived under `DocumentationArchive/2026-09-21/`. These local artifacts are not required to use the package.
