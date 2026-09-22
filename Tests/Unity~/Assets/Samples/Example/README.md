# Emas sample

Open `Scenes/Example.unity` and press Play. Cars and aircraft use application contracts; after four seconds the car source changes from SDK One to SDK Two while retaining compatible ghost roots. Toggle the Bootstrap object to stop and restart the demonstration.

| Responsibility | Read first |
| --- | --- |
| Read-only application data | `Contracts/I3DPosition.cs`, `IArticulate.cs` |
| Source mapping and mutation | `Entities/CarGhost.cs`, `Sources/Presence/SdkOneCarSource.cs` |
| Paired query membership, subscription disposal and replacement | `Bootstrap.cs` (`OnEnable`, `OnDisable`, `ReplaceCarSource`) |
| Root position consumption | `Behaviors/ApplyPosition.cs` |
| View consumption through `View.Ghost.TryGet<T>` | `Behaviors/VehicleLogic.cs`, `ArticulationLogic.cs` |

The displayed counts use `Observe`: entry callbacks retain ghost keys and request views, departure callbacks remove keys. `OnDisable` disposes subscriptions and clears those sets. Sources have diagnostic labels visible in **Window > Emas**.

Positions are local to the owning anchor. The simulated SDKs already use that frame; a real adapter must convert units, axes and coordinates before calling the concrete ghost's setters. Consumers only receive the read-only interfaces. The separate cockpit marker illustrates screen-local data without an Emas ghost.

For the smallest integration, start with **Quick start** or **Callback quick start**. This example builds its demo prefabs in code; production projects can assign Blueprint assets in SceneSetup instead.
