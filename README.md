# Emas

A Unity 2022.3+ package that tracks source entities as scene-owned ghosts and creates optional views. Applications define their own data contracts, presence sources, kinds and appearances.

## Try it

1. Add this repository's `package.json` through **Package Manager > Add package from disk**.
2. Import the **Quick start** sample.
3. Open its `QuickStart.unity` scene and press Play.

The quick start shows one moving marker with Inspector-configured tracking. The **Callback quick start** sample adds SDK events, explicit removals and unsubscribe cleanup. The larger Emas sample demonstrates multiple sources and replacement.

## Documentation

- [Getting started](Documentation~/GettingStarted.md): install and integrate a source.
- [API reference](Documentation~/API.md): operations and behavior contracts.
- [Architecture](Documentation~/Architecture.md): ownership, update phases and lifecycle diagrams.
- [Validation](Documentation~/Validation.md): test results and limits.
