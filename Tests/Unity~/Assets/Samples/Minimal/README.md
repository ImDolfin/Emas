# Quick start

Open `QuickStart.unity` and press Play. One teal cube moves along the position guide. Disable and re-enable **Tracking** to stop and recreate the realm and detector.

## Inspect the setup

The scene contains an instance of `Tracking.prefab` plus an authored camera, light and environment. Open the prefab to see its configuration:

- **Realm Setup** assigns `MarkerBlueprint.asset` as the default blueprint.
- **Anchor Setup** uses the `quick-start` anchor ID with automatic views enabled.
- **Marker Source** supplies the detector and registers the position module before tracking starts.

The assets form a small, complete presentation setup:

| Asset | Purpose |
| --- | --- |
| `MarkerBlueprint.asset` | Maps `minimal.marker` to its Ghost root and default variant. |
| `MarkerRoot.prefab` | Contains the source-independent `Marker` Ghost component. |
| `Default Marker Variant.asset` | Maps the empty variant ID (`Variant.None`) at Full detail to the view. |
| `MarkerView.prefab` | Contains the cube mesh and its `Marker.mat` material. |
| `Tracking.prefab` | Reusable, fully configured realm, anchor and detector provider. |

Change the material or mesh in `MarkerView.prefab` to customize the cube. Swap the blueprint's root or variant assets in the Inspector to change the setup. These assets are saved with the sample; Play Mode only creates the tracked instances.

## Connect a source

The remaining scripts show only the data integration:

- `Reading` stands in for an SDK item with a stable ID and position.
- `MarkerSource` implements `IDetectorProvider` and `IRealmConfigurator`, creating a `MarkerDetector` and registering its position adapter.
- `MarkerDetector` reports the marker's current reading in `OnStart` and `OnUpdate`.
- `MarkerPositionModule` applies each reading to the `Marker` Ghost root.

Replace the simulated reading in `MarkerDetector` with your SDK data. This marker is always present; call `Disappear` with the kind and stable ID when a real entity departs. Call Emas on Unity's main thread.
