# Emas example

Open `Scenes/Example.unity` and press Play. Ten cars drive around the road and three aircraft move overhead. After four seconds, `CarSource` switches from SDK One to SDK Two while retaining compatible ghost roots and views. Disable and re-enable **Tracking** to restart with SDK One.

The scene is configured before Play. Expand **Tracking** to inspect its `RealmSetup`, **Cars** anchor (`cars`) and **Aircraft** anchor (`aircraft`). Both anchors enable **Automatic Views**. Each has one small detector provider; the realm owns startup, updates and cleanup. The scene's environment, camera, light, and cockpit are prefab instances too.

## Follow the asset references

| Asset | What to inspect |
| --- | --- |
| `Prefabs/Tracking.prefab` | Realm blueprint assignments, anchor IDs, automatic views and detector providers |
| `Blueprints/Cars.asset` | Car ghost root, three appearance variants and the unknown-vehicle fallback |
| `Blueprints/Aircraft.asset` | Aircraft ghost root and trainer appearance |
| `Variants/SmallCar.asset`, `LargeCar.asset`, `Truck.asset`, `Trainer.asset` | Appearance IDs and their Full-detail view prefabs |
| `Prefabs/Ghosts/` | `CarGhost` or `AircraftGhost`, plus `ApplyPosition` on each root |
| `Prefabs/Views/` | A shared `VehicleView` prefab, actual Unity prefab variants for the vehicle appearances, and an aircraft view |
| `Materials/` | Saved materials assigned to the authored view and environment renderers |
| `Prefabs/Cockpit.prefab` | A screen and marker wired to the focused `CockpitDemo` behavior |

To customize an appearance, open its view prefab variant and change the body, cabin or material. To add another car appearance, duplicate a `ManifestationVariant` asset, assign a unique variant ID and view prefab, and add it to `Cars.asset`. Map that ID in your detector. Entity data and SDK handover do not depend on the visual prefab.

## Read the integration code

| Responsibility | Read first |
| --- | --- |
| Read-only application data | `Contracts/I3DPosition.cs`, `IArticulate.cs` |
| Source mapping and mutation | `Entities/CarGhost.cs`, `Sources/Presence/SdkOneCarDetector.cs` |
| One detector per anchor attachment | `Sources/CarSource.cs`, `AircraftSource.cs` |
| SDK replacement | `CarSource.ReplaceCarSource()`; its serialized delay is four seconds |
| Paired query membership and subscription disposal | `Behaviors/SampleStatus.cs` |
| Root position consumption | `Behaviors/ApplyPosition.cs` |
| View consumption through `View.Ghost.TryGet<T>` | `Behaviors/VehicleLogic.cs`, `ArticulationLogic.cs` |
| Screen-local marker data | `Behaviors/CockpitDemo.cs`, `CockpitMarker.cs` |

`SampleStatus` uses `Observe`: entry callbacks retain ghost keys and departure callbacks remove them. It disposes both subscriptions and clears the retained sets when disabled or when its realm changes. Automatic views are configured on the anchors, so the observer only displays membership. Source diagnostic labels are visible in **Window > Emas**.

Positions are local to the owning anchor. The simulated feeds already use that frame; real SDK adapters must convert units, axes and coordinates before calling the concrete ghost's setters. Consumers only receive read-only interfaces. The cockpit marker illustrates screen-local data independently of Emas ghosts.
