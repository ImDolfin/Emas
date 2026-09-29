# Emas example

Open `Scenes/Example.unity` and press Play. Ten cars drive around the road and three aircraft move overhead. After four seconds, `CarSource` switches from SDK One to SDK Two while retaining compatible ghost roots and views. Disable and re-enable **Tracking** to restart with SDK One.

The scene is configured before Play. Expand **Tracking** to inspect its `RealmSetup`, **Cars** anchor (`cars`) and **Aircraft** anchor (`aircraft`). Both anchors enable **Automatic Views**. Each has a detector component and a Ghost initializer; the realm owns startup, updates and cleanup. The scene's environment, camera, light, and cockpit are prefab instances too.

## Follow the asset references

| Asset | What to inspect |
| --- | --- |
| `Prefabs/Tracking.prefab` | Realm blueprint assignments, anchor IDs, automatic views, detector components and initializers |
| `Blueprints/Cars.asset` | Car ghost root, three appearance variants and the unknown-vehicle fallback |
| `Blueprints/Aircraft.asset` | Aircraft ghost root and trainer appearance |
| `Variants/SmallCar.asset`, `LargeCar.asset`, `Truck.asset`, `Trainer.asset` | Appearance IDs and their Full-detail view prefabs |
| `Prefabs/Ghosts/` | Plain `Ghost` components, configured position/articulation modules and `ApplyPosition` |
| `Prefabs/Views/` | A shared `VehicleView` prefab, actual Unity prefab variants for the vehicle appearances, and an aircraft view |
| `Materials/` | Saved materials assigned to the authored view and environment renderers |
| `Prefabs/Cockpit.prefab` | A screen and marker wired to the focused `CockpitDemo` behavior |

To customize an appearance, open its view prefab variant and change the body, cabin or material. To add another car appearance, duplicate a `ManifestationVariant` asset, assign a unique variant ID and view prefab, and add it to `Cars.asset`. Map that ID in your detector. Entity data and SDK handover do not depend on the visual prefab.

## Read the integration code

`CarSource` and `AircraftSource` derive from `PresenceDetectorComponent`. Their sibling `GhostInitializer` components map SDK fields to the modules. `CarSource.ReplaceCarSource()` passes its underlying `Detector` to `Anchor.ReplaceDetector`, switching to the plain C# `SdkTwoCarDetector`. Both forms share the same lifecycle and retain compatible Ghost roots.

| Responsibility | Read first |
| --- | --- |
| Read-only application data | `Contracts/I3DPosition.cs`, `IArticulate.cs` |
| Ghost module definitions | `Entities/PositionModule.cs`, `ArticulationModule.cs` and the saved Ghost prefabs |
| SDK-to-module mapping | `Sources/CarInitializer.cs`, `AircraftInitializer.cs` |
| One detector per anchor attachment | `Sources/CarSource.cs`, `AircraftSource.cs` |
| SDK replacement | `CarSource.ReplaceCarSource()`; its serialized delay is four seconds |
| Paired query membership and subscription disposal | `Behaviors/SampleStatus.cs` |
| Root position consumption | `Behaviors/ApplyPosition.cs` |
| View consumption through `View.Ghost.TryGet<T>` | `Behaviors/VehicleLogic.cs`, `ArticulationLogic.cs` |
| Screen-local marker data | `Behaviors/CockpitDemo.cs`, `CockpitMarker.cs` |

`SampleStatus` uses `Observe`: entry callbacks retain ghost keys and departure callbacks remove them. It disposes both subscriptions and clears the retained sets when disabled or when its realm changes. Automatic views are configured on the anchors, so the observer only displays membership. Source diagnostic labels are visible in **Window > Emas**.

Positions are local to the owning anchor. The simulated feeds already use that frame; real SDK adapters must convert units, axes and coordinates in the initializer-bound readers. The Ghost modules only consume `Vector3` and `float`; detectors only announce arrivals and departures. Consumers only receive read-only interfaces. The cockpit marker illustrates screen-local data independently of Emas ghosts.

Each detector supplies its SDK feed as the weak `Presence.Source`. The initializer selects the SDK mapping from that object's type and readers access its latest `Current` snapshot, without a second SDK lookup during initialization. Replacement reconnects the retained Ghost modules to the replacement feed.
