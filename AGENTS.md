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

Runtime entry point: `Context.Default`. Keep short role names in namespace `Emas`; do not add system-name prefixes. Group runtime files under `Entities`, `Tracking`, `Queries`, `Views` and `Unity`, with one top-level type per file. Tests mirror these responsibilities. The package ID is `com.emas.core`. Use current Emas names throughout; do not add legacy aliases or compatibility annotations.

## Coding Conventions

- Use **C# 8.0** features only. Do not use records, target-typed new, init accessors, relational patterns, static lambdas, global usings or file-scoped namespaces. Use Allman braces and braces for every control-flow body.
- Follow **Unity naming conventions**: `PascalCase` for public members, `_camelCase` for private fields.
- All public classes and methods must have XML doc comments (`<summary>`).
- Prefer `SerializeField` on private fields over making fields public.
- Use `#if UNITY_EDITOR` guards sparingly; prefer putting editor code in the Editor assembly instead.

## Build & Test

```bash
# There is no CLI build. Open the package in Unity via:
#   Package Manager → Add package from disk → select package.json

# Run tests from Unity:
#   Window → General → Test Runner → Run All
```

## File Organization Rules

- New runtime code goes in `Runtime/` and must be in the `Emas` namespace.
- New editor code goes in `Editor/` and must be in the `Emas.Editor` namespace.
- New tests go in `Tests/Runtime/` or `Tests/Editor/` as appropriate.
- Sample assets go in `Samples~/` — never in `Runtime/` or `Editor/`.
- Documentation goes in `Documentation~/`.

## Do Not Modify

- `package.json` version field — update only when explicitly releasing a new version.

## Pull Request Expectations

- Every new public API must have a corresponding test.
- Update `CHANGELOG.md` for user-facing changes.
- Keep `package.json` version in sync with `Package.Version`.
