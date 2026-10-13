# Emas example

Open `Scenes/Example.unity` and press Play. Three cars drive around the road. After four seconds, `CarSource` switches from SDK One to SDK Two while retaining compatible ghost roots and views. Disable and re-enable **Tracking** to restart with SDK One.

The scene is configured before Play. Expand **Tracking** to inspect its `RealmSetup` and **Cars** anchor (`cars`). The anchor enables **Automatic Views**. It has a detector component and a Ghost initializer; the realm owns startup, updates and cleanup. The scene's environment, camera and light are prefab instances too.

## Follow the asset references

| Asset | What to inspect |
| --- | --- |
| `Prefabs/Tracking.prefab` | Realm blueprint assignments, anchor IDs, automatic views, detector components and initializers |
| `Blueprints/Cars.asset` | Car ghost root, three inline named variant rows and the unknown-vehicle fallback |
| `Prefabs/Ghosts/` | Plain `Ghost` components, configured position/articulation traits and `ApplyPosition` |
| `Prefabs/Views/` | A shared `VehicleView` prefab, actual Unity prefab variants for the vehicle appearances |
| `Materials/` | Saved materials assigned to the authored view and environment renderers |

To customize an appearance, open its view prefab variant and change the body, cabin or material. To add another car appearance, add a row to the **Variants** table in `Cars.asset`, enter a unique name and assign its view prefab. Use that name in your detector's `Variant` value. Alternative LODs can use separate rows such as `small-car-low`. Entity data and SDK handover do not depend on the visual prefab.

## Read the integration code

`CarSource` derives from `PresenceDetectorComponent`. Its sibling `CarInitializer` maps SDK fields to the traits. `CarSource.ReplaceCarSource()` passes its underlying `Detector` to `Anchor.ReplaceDetector`, switching to the plain C# `SdkTwoCarDetector`. Both forms share the same lifecycle and retain compatible Ghost roots.

| Responsibility | Read first |
| --- | --- |
| Read-only application data | `Contracts/I3DPosition.cs`, `IArticulate.cs` |
| Ghost trait definitions | `Entities/PositionTrait.cs`, `ArticulationTrait.cs` and the saved Ghost prefabs |
| SDK-to-trait mapping | `Sources/CarInitializer.cs` |
| One detector per anchor attachment | `Sources/CarSource.cs` |
| SDK replacement | `CarSource.ReplaceCarSource()`; its serialized delay is four seconds |
| Paired query membership and subscription disposal | `Behaviors/SampleStatus.cs` |
| Root position consumption | `Behaviors/ApplyPosition.cs` |
| View consumption through `View.Ghost.TryGet<T>` | `Behaviors/VehicleLogic.cs`, `ArticulationLogic.cs` |

`SampleStatus` uses `Observe`: entry callbacks retain ghost keys and departure callbacks remove them. It disposes its subscription and clears retained state when disabled or when its realm changes. Automatic views are configured on the anchors. Source diagnostic labels are visible in **Window > Emas**.

## Query the cars

The saved Tracking prefab includes `SampleStatus`. Its overlay shows the observed car count, a filtered
small-car count, and position read through `I3DPosition`. Select **Tracking > Sample Status** while playing
and use **Log queried car positions** in the component context menu to enumerate the matching root contracts.

The query is scoped to this scene's Realm and the `cars` Anchor:

```csharp
Query cars = realm.Query().InAnchor("cars").OfKind(SampleKinds.Car)
    .With<I3DPosition>().With<IArticulate>();
Query smallCars = cars.WithVariant(CarVariants.SmallCar);

int smallCarCount = smallCars.Count;
IGhost smallCar = smallCars.FirstOrDefault();
I3DPosition position;
if (smallCar != null && smallCar.TryGet<I3DPosition>(out position))
{
    Debug.Log(position.Position);
}
```

Deriving `smallCars` leaves `cars` unchanged. `Count` and `FirstOrDefault()` evaluate current available
matches; the first result has no ordering guarantee. `With<T>()` checks a unique provider on the Ghost root,
so views do not supply these contracts. Position is anchor-local Unity data. Neither query requests a view.

Read [Behaviors/SampleStatus.cs](Behaviors/SampleStatus.cs) for the full consumer. Its `Observe` subscription
maintains the available-car set across SDK replacement. Callbacks run once per matching membership period,
while the overlay reads current contract data each frame. Departures receive a `Key`, allowing cleanup after
the Unity object has been destroyed. The collection exists before subscribing because current matches can
enter synchronously. Disabling the consumer disposes its handle and clears its state; restarting Tracking
creates a new Realm, so it rebuilds the descriptions and subscription instead of retaining the old scope.

See the [query guide and complete API reference](../../Documentation~/Queries.md) for display-name filters,
known entity IDs, LINQ, `Single()`, all-Realm queries and callback timing.

Positions are local to the owning anchor. The simulated feeds already use that frame; real SDK adapters must convert units, axes and coordinates in the initializer-bound readers. The Ghost traits only consume `Vector3` and `float`; detectors only announce arrivals and departures. Consumers only receive read-only interfaces.

Each detector supplies its SDK feed as the weak `Presence.Source`. The initializer selects the SDK mapping from that object's type and readers access its latest `Current` snapshot, without a second SDK lookup during initialization. Replacement reconnects the retained Ghost traits to the replacement feed.
