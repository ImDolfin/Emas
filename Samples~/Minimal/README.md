# Quick start

Open `QuickStart.unity` and press Play. One teal cube moves along the position guide. Disable and re-enable **Tracking** to stop and recreate the realm and detector.

## Inspect the setup

The scene contains an instance of `Tracking.prefab` plus an authored camera, light and environment. Open the prefab to see its configuration:

- **Realm Setup** assigns `MarkerBlueprint.asset` as the default blueprint.
- **Anchor Setup** uses the `quick-start` anchor ID with automatic views enabled.
- **Marker Source** supplies the detector and binds the configured position module before tracking starts.

The assets form a small, complete presentation setup:

| Asset | Purpose |
| --- | --- |
| `MarkerBlueprint.asset` | Maps `minimal.marker` to its Ghost root and default variant. |
| `MarkerRoot.prefab` | Contains the `Marker` Ghost and its reusable `MarkerPositionModule`. |
| `Default Marker Variant.asset` | Maps the empty variant ID (`Variant.None`) at Full detail to the view. |
| `MarkerView.prefab` | Contains the cube mesh and its `Marker.mat` material. |
| `Tracking.prefab` | Reusable, fully configured realm, anchor and detector provider. |

Change the material or mesh in `MarkerView.prefab` to customize the cube. Swap the blueprint's root or variant assets in the Inspector to change the setup. These assets are saved with the sample; Play Mode only creates the tracked instances.

## Connect a source

The remaining scripts show only the data integration:

- `MarkerSource` implements `IDetectorProvider` and `IRealmConfigurator`, creating a `MarkerDetector` and binding the Ghost's position module.
- `MarkerDetector` detects the permanent marker once in `OnStart`.
- `MarkerPositionModule` reads a mapped `Vector3` each realm update and applies it to the root.

Replace the simulated position reader in `MarkerSource` with a reader of your SDK proxy. This marker is always present; call `Disappear` with the kind and stable ID when a real entity departs. Call Emas on Unity's main thread.

The detector supplies its `MarkerSource` adapter as the weak `Presence.Source`. The initializer resolves that adapter inside the position reader, illustrating support for Unity source objects without retaining them through the Ghost.
