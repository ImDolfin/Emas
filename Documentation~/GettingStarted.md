# Getting started

Emas separates source integration, entity data and presentation: a coordinator updates a ghost; a blueprint supplies its optional view.

## Install and run

1. In a Unity 2022.3+ project, choose **Package Manager > Add package from disk** and select this repository's `package.json`.
2. Import the **Emas sample** from the package's Samples section.
3. Open `Assets/Samples/Emas/0.1.0/Example/Scenes/Example.unity` and press Play.

The sample shows ten cars, three aircraft and a cockpit marker. After four seconds, another simulated SDK takes over the cars while their ghost identities survive. Primitive templates are generated at runtime; no external SDK or model assets are required.

## Integrate a source

| Application component | Responsibility |
| --- | --- |
| `Kind` / `Variant` constants | Identify populations and appearances |
| `Ghost` subclass | Hold source-independent data and implement consumer interfaces |
| `Coordinator` subclass | Read the SDK, convert values, publish changes and remove departures |
| `Blueprint` asset | Assign the kind, optional ghost prefab and view mappings |

Inside a coordinator's `OnUpdate()`, publish each source entity and finish assigning its data:

```csharp
var car = GetOrCreate<CarGhost>(
    proxy.Id, VehicleKinds.Car, CarVariants.SmallCar);
car.SetPosition(ConvertPosition(proxy));
```

`CarGhost`, the named constants and conversion are application-defined. See the [sample source](../Samples~/Example/Bootstrap.cs) for working implementations. Use `Remove(kind, entityId)` for departures. Background SDK callbacks must enqueue copied values through `Dispatch`, rather than mutate Unity objects directly.

Register the blueprint and start tracking from application setup:

```csharp
Context.Default.RegisterBlueprint(carBlueprint);
var origin = Context.Default.CreateOriginFor("simulation", carCoordinator);
```

Unity updates the default context automatically. Do not also call `Context.Default.Update()` each frame.

## Consume ghosts and request views

```csharp
var cars = Context.Default.Query().OfKind(VehicleKinds.Car).With<I3DPosition>();
var subscription = cars.OnAvailable(ghost => Context.Default.Manifest(ghost));
```

Queries return available ghosts only. `With<T>()` checks root components; view children do not satisfy it. Root behaviors continue without a view. Put presentation-only behaviors on the view prefab.

Dispose `subscription` when the consumer stops. `Context.Default.Demanifest(ghost)` removes only the view; `Context.Default.RemoveOrigin("simulation")` ends tracking and removes the population.

## Testing

Access the shared context through `Context.Default`. Disposing it is supported; the next access creates a fresh default. Use an isolated `Context`, attach a fake coordinator and call `Update()` explicitly in Unity tests. No authored scene or live SDK is needed. Test plain consumer interfaces separately with ordinary fakes. Run package tests in **Window > General > Test Runner**.

See [API reference](API.md) for selection and query rules, and [Architecture](Architecture.md) for timing, replacement and disposal.
