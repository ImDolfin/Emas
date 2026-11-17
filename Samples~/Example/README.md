# Emas example

Open `Scenes/Example.unity` and press Play. Three cars drive around the road. After four seconds, Car Source switches from SDK One to SDK Two while retaining compatible Ghost roots and views. Disable/re-enable Tracking to restart with SDK One.

## Inspect the assets

| Asset | What to change |
| --- | --- |
| `Prefabs/Tracking.prefab` | Realm blueprint mappings, the `cars` Anchor, automatic views, detector and initializer. |
| `Blueprints/Cars.asset` | Root prefab, named variants and unknown-vehicle fallback. |
| `Prefabs/Ghosts/` | Plain Ghost, position/articulation traits and ApplyPosition. |
| `Prefabs/Views/` | Shared VehicleView and Unity prefab variants for each appearance. |
| `Materials/` | View and environment materials. |

Add appearances as named blueprint rows and report their Variant from the detector. LODs can use separate names such as `small-car-low`; Emas does not choose them automatically. Data integration and SDK handover remain independent of the visual prefab.

## Follow the integration

| Responsibility | Code |
| --- | --- |
| SDK One detector component | `Sources/CarSource.cs` |
| SDK Two plain C# replacement | `Sources/Presence/SdkTwoCarDetector.cs` |
| SDK-to-trait readers | `Sources/CarInitializer.cs` |
| Source-independent state | `Entities/PositionTrait.cs`, `ArticulationTrait.cs` |
| Read-only root contracts | `Contracts/I3DPosition.cs`, `IArticulate.cs` |
| Root positioning | `Behaviors/ApplyPosition.cs` |
| View contract consumption | `Behaviors/VehicleLogic.cs`, `ArticulationLogic.cs` |
| Query membership/display | `Behaviors/SampleStatus.cs` |

`CarSource.ReplaceCarSource()` passes its Detector to Anchor.ReplaceDetector. Both SDKs use the same lifecycle. Each feed is a weak Presence.Source; the initializer maps its current snapshot and reconnects retained traits during replacement. Detectors announce membership; traits consume Vector3/float; consumers read interfaces.

Positions are Anchor-local Unity coordinates. Real SDK mappings must convert units/axes into that contract. The sample's root ApplyPosition behavior is an alternative to shared Spatial projection.

## Query the cars

The Tracking prefab includes SampleStatus. Its overlay counts observed cars and small-car variants and reads I3DPosition. Choose **Log queried car positions** in the component context menu for an explicit enumeration.

```csharp
Query cars = realm.Query().InAnchor("cars").OfKind(SampleKinds.Car)
    .With<I3DPosition>().With<IArticulate>();
Query smallCars = cars.WithVariant(CarVariants.SmallCar);

IGhost smallCar = smallCars.FirstOrDefault();
I3DPosition position;
if (smallCar != null && smallCar.TryGet<I3DPosition>(out position))
{
    Debug.Log(position.Position);
}
```

Derived filters leave `cars` unchanged. Results contain available root providers independently of views and have no ordering guarantee. SampleStatus initializes its collection before subscribing because current entries may arrive synchronously. Its paired Observe callbacks retain/remove Keys across SDK replacement; disable or Realm restart disposes the handle and clears state before rebinding.

Read [SampleStatus](Behaviors/SampleStatus.cs) for the complete consumer. The installed package's `Documentation~/Queries.md` covers filters, lookup, global scope and callback timing. **Window > Emas** shows diagnostic feed names and health.
