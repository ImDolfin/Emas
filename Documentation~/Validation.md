# Validation

Validated on **Unity 2022.3.62f3**, package **0.1.0**, on 2026-09-22.

| Check | Result |
| --- | --- |
| Unity PlayMode suite | 131 passed, 0 failed, 0 skipped (38 callback cases) |
| Editor package asset tests | 3 passed; package scripts resolve their declared types |
| Quick-start assets | Shipped polling and callback scenes, blueprints and prefabs create their configured views |
| Portable C# 8 runtime/sample compile | 0 warnings/errors |
| Portable value/query/API checks | 25 passed |
| Invalid kind/variant calls | 8 expected compiler errors |
| Windows Mono sample builds | Polling and callback quick starts succeeded |
| Polling player smoke | Exit 0; view creation, disable cleanup and re-enable restart passed |
| Callback player smoke | Exit 0; initial view, deferred update, explicit removal, unsubscribe and restart passed |
| Architecture and lifecycle diagrams | Each 9/9 showcase checks; desktop containment and light/dark visual review passed |

Regression coverage:

- Tracking: lifecycle callbacks, replacement, scene unload, anchor reuse and ordered dispatch.
- Sources: snapshot failures/departures, worker callbacks, stale registrations, startup interruption, cleanup exceptions and failure-triggered restart.
- Views/setup: requested detail levels with prefab fallback, configuration errors/retries, automatic views and startup from another component's enable callback.

**Limits:** headless smoke uses a null graphics device and reports unsupported shader messages; it does not validate rendering. IL2CPP, other platforms and performance timings were not tested. Diagram export interactions were not separately tested.

Run package tests through Unity's Test Runner. The ignored `TestProject~/` holds the validation consumer, `TestResults/` reports and `CallbackWork/` restart record. These local artifacts are not required to use the package.
