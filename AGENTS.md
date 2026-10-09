# Emas — AI Agent Instructions

## Project Overview

Emas is a **Unity plugin** (UPM package) targeting **Unity 2022.3 LTS and higher**.
The codebase is C# and follows the standard Unity Package Manager layout.

## Tech Stack

- **Engine:** Unity 2022.3+
- **Language:** C# 8.0 (as required by Emas)
- **Package format:** Unity Package Manager (UPM)
- **Test framework:** NUnit (via Unity Test Framework)

## Architecture

| Assembly                          | Purpose            | Platform  |
| --------------------------------- | ------------------ | --------- |
| `Emas.Runtime`              | Runtime logic      | All       |
| `Emas.Editor`       | Editor tooling     | Editor    |
| `Emas.Tests.Runtime`        | Runtime tests      | All       |
| `Emas.Tests.Editor` | Editor tests       | Editor    |

- **Runtime → Editor dependency is forbidden.** Editor assemblies may reference Runtime, never the reverse.
- All public APIs live under the `Emas` namespace (runtime) or `Emas.Editor` namespace (editor).
- All Emas operations, including source publish/remove callbacks and `Dispatch`, require Unity's main thread. SDK threading belongs to the application; do not add thread synchronization or marshalling to Emas.

Runtime entry point: `Realm.Default`. Keep short role names in namespace `Emas`; do not add system-name prefixes. Group runtime files under `Entities`, `Tracking`, `Queries`, `Views` and `Unity`, with one top-level type per file. Tests mirror these responsibilities. The package ID is `com.emas.core`. Use current Emas names throughout; do not add legacy aliases or compatibility annotations.

## Coding Conventions

- Use **C# 8.0** features only. Do not use records, target-typed new, init accessors, relational patterns, static lambdas, global usings or file-scoped namespaces. Use Allman braces and braces for every control-flow body.
- Follow **Unity naming conventions**: `PascalCase` for public members, `_camelCase` for private fields.
- Document every public API and protected extension point with Visual Studio/Doxygen-compatible XML comments: meaningful `<summary>`, `<param>`, `<typeparam>`, `<returns>` and `<value>` tags where applicable. Include units, ownership, timing, validation and lifecycle constraints when they affect callers. Document interface members and enum values too.
- Explain non-obvious algorithms, coordinate conversions and lifecycle ordering with focused code comments. Avoid comments that merely repeat the statement below them.
- Keep methods focused, separate logical blocks with blank lines, and use explicit multi-line braces for control flow; do not write one-line `if` or loop bodies. Use regions only when they make a substantial group of related members easier to navigate.
- Prefer `SerializeField` on private fields over making fields public.
- Use `#if UNITY_EDITOR` guards sparingly; prefer putting editor code in the Editor assembly instead.

## Build & Test

For the preconfigured test project, add `Tests/Unity~` through Unity Hub and open it with Unity **2022.3.62f3**. The repository root is a UPM package, not the runnable project. No manifest editing is required for the included project.

In an existing consuming Unity project, install Test Framework and add `"testables": ["com.emas.core"]` beside `dependencies` in `Packages/manifest.json`. Run EditMode and PlayMode tests through **Window > General > Test Runner**. Reopen Unity if the tests remain hidden.

For repository development, open the optional `Tests/Unity~` project in Unity **2022.3.62f3**. It also tests the imported samples and supports **Run all in player** (Windows Mono build support required). Update `Assets/Samples/` in that project when changing `Samples~/`; EditMode tests detect differences. All required inputs are tracked; only generated output is ignored. See `Documentation~/Validation.md` for test setup and coverage.

Keep `Validation.md` focused on test setup and API contract coverage. Store run reports in ignored `TestResults/` output instead of appending execution histories to the documentation.

## Test Quality

- Keep the suite small and focused on important public API contracts and boundaries. Prefer a few thorough consumer scenarios over blanket, shallow coverage of every member or input permutation.
- Add or retain a test only when it protects a distinct contract, meaningful failure or recovery boundary, or required integration. Strengthen an existing scenario when it can cover the same contract clearly; remove redundant cases without losing important behavior coverage.
- Test observable behavior through public Emas APIs, supported protected detector extension points, or Unity's public serialized-authoring APIs. Do not expose internals to test assemblies or use reflection to reach private production state.
- Give each test one clear consumer purpose and an XML summary explaining the contract it protects. Prefer representative scenarios over permutations of the same behavior.
- Keep coverage for meaningful current behavior, including failure handling. Do not keep cases solely because they once reproduced a regression or exercised a removed/legacy implementation.
- Avoid testing private test helpers, trivial constants, or implementation details. Keep the required imported-sample consistency checks.

## File Organization Rules

- New runtime code goes in `Runtime/` and must be in the `Emas` namespace.
- New editor code goes in `Editor/` and must be in the `Emas.Editor` namespace.
- Package tests go in `Tests/Runtime/` or `Tests/Editor/`; imported-sample tests go in `Tests/Unity~/Assets/Tests/`.
- Sample assets go in `Samples~/` — never in `Runtime/` or `Editor/`.
- Documentation goes in `Documentation~/`.

## Do Not Modify

- `package.json` version field — update only when explicitly releasing a new version.

## Pull Request Expectations

- Every new public API must have focused contract coverage, which may extend an existing scenario instead of adding a separate test for each member.
- Update `CHANGELOG.md` for user-facing changes.
- Keep `package.json` version in sync with `Package.Version`.
