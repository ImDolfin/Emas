# Samples

Import through Package Manager before opening a scene.

| Sample | Open | Demonstrates |
| --- | --- | --- |
| **Quick start** (`Minimal/`) | `QuickStart.unity` | One arrival detector with a mapped position trait, authored Ghost and view prefabs, blueprint with a named variant |
| **Emas sample** (`Example/`) | `Scenes/Example.unity` | Isolated Realm Setup, three cars, typed views, filtered car queries with membership subscriptions and source replacement |
| **Relative world** (`RelativeWorld/`) | `RelativeWorld.unity` | Authored `Spatial` roots and blueprint variant rows, `Double3` positions, parked-car query membership and bird-distance lookup around a driving reference |
| **Aircraft formation** (`AircraftFormation/`) | `AircraftFormation.unity` | A Mach 1 cockpit reference, two equal-speed wingmen, combined angular maneuvers, independent capture/delivery timing, noise and live angular instruments |

Imported samples appear beneath `Assets/Samples/Emas/<version>/`, in a folder matching their displayed sample name.

Package-wide guides live in the installed package's `Documentation~` folder; sample-relative source links work after import too.

Every scene uses a reusable tracking prefab with Realm Setup and Anchor Setup components. Ghost roots, views, blueprints with inline variants, materials, cameras and environments are saved as inspectable assets or scene objects. Customize those assets in the Inspector; the small source components connect SDK data to traits already configured on the Ghost roots. Play Mode creates only the tracked instances.

Each sample is a standalone application assembly referencing Emas. Their feeds generate deterministic example data without network dependencies.
