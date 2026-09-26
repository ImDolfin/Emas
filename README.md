# Emas

A Unity 2022.3+ package that tracks source entities as scene-owned ghosts and creates optional views. Applications define their own data contracts, presence sources, kinds and appearances.

## Try it

1. Add this repository's `package.json` through **Package Manager > Add package from disk**.
2. Import the **Quick start** sample.
3. Open its `QuickStart.unity` scene and press Play.

The quick start shows one moving marker with Inspector-configured tracking. **Callback quick start** adds SDK events and cleanup. **Emas sample** demonstrates multiple sources and replacement. **Relative world** shows double-precision spatial placement around a moving reference.

## Tests

1. In **Unity Hub**, add the project from **`Tests/Unity~`** and open it with **Unity 2022.3.62f3**.
2. Wait for package import and compilation to finish.
3. Open **Window > General > Test Runner** and choose **Run All** in both **EditMode** and **PlayMode**.

This project already includes the samples and test configuration. No manifest editing or sample import is needed. The repository root is a UPM package; `Tests/Unity~` is the Unity project to open. See [validation](Documentation~/Validation.md) for testing in other projects.

## Documentation

- [Getting started](Documentation~/GettingStarted.md): install and integrate a source.
- [Guidelines](Documentation~/Guidelines.md): contracts, ownership and contributions.
- [API reference](Documentation~/API.md): operations and behavior contracts.
- [Architecture](Documentation~/Architecture.md): ownership, update phases and lifecycle diagrams.
- [Validation](Documentation~/Validation.md): Unity Test Runner setup, results and limits.
