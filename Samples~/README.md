# Samples

Import through Package Manager before opening a scene.

| Sample | Open | Demonstrates |
| --- | --- | --- |
| **Quick start** (`Minimal/`) | `QuickStart.unity` | One polling source, ghost and Inspector-configured view |
| **Emas sample** (`Example/`) | `Scenes/Example.unity` | Multiple sources, typed views and source replacement |

Imported samples appear beneath `Assets/Samples/Emas/0.1.0/`, in a folder matching their displayed sample name.

The larger sample shows ten cars, three aircraft and a cockpit marker. After four seconds, another simulated SDK takes over the cars while their ghost identities survive. Source adapters convert both SDK shapes into the same application interfaces. Root behaviors handle position; view behaviors handle presentation.

Both samples are standalone application assemblies referencing Emas. Their sources are deterministic simulations with no network dependencies.
