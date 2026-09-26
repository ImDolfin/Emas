# Samples

Import through Package Manager before opening a scene.

| Sample | Open | Demonstrates |
| --- | --- | --- |
| **Quick start** (`Minimal/`) | `QuickStart.unity` | One polling source, authored Ghost and view prefabs, variant and blueprint |
| **Callback quick start** (`Callbacks/`) | `Callbacks.unity` | Prefab-configured callback source, removal and unsubscribe cleanup |
| **Emas sample** (`Example/`) | `Scenes/Example.unity` | Isolated Realm Setup, cars/aircraft child anchors, typed views and source replacement |
| **Relative world** (`RelativeWorld/`) | `RelativeWorld.unity` | Authored `Spatial` roots and variants, `Double3` positions and a configured moving reference |

Imported samples appear beneath `Assets/Samples/Emas/0.1.0/`, in a folder matching their displayed sample name.

Every scene uses a reusable tracking prefab with Realm Setup and Anchor Setup components. Ghost roots, views, manifestation variants, blueprints, materials, cameras and environments are saved as inspectable assets or scene objects. Customize those assets in the Inspector; the small source components connect SDK data and register modules. Play Mode creates only the tracked instances.

Each sample is a standalone application assembly referencing Emas. Their feeds generate deterministic example data without network dependencies.
