# Getting started

Emas connects source data to scene ghosts and optional views. Requires Unity 2022.3+.

## Run the quick start

1. Add this repository's `package.json` through **Package Manager > Add package from disk**.
2. Import the **Quick start** sample.
3. Open its `QuickStart.unity` scene and press Play. One cube follows the source position.

The imported scene is under `Assets/Samples/Emas/0.1.0/Quick start/`. The [sample source](../Samples~/Minimal/Bootstrap.cs) contains one setup call; its blueprint and prefab are assigned in the Inspector.

## Connect your source

1. Write a `Ghost` subclass with the data or behavior your application needs. Put its `Kind` constant on that class.
2. Add **Emas > Scene Setup** to a scene object. Give it a unique anchor ID, assign blueprints, and enable **Automatic Views** if wanted.
3. Call `Track` from your bootstrap's `OnEnable`:

```csharp
GetComponent<SceneSetup>().Track(
    new PollingCoordinator<SdkItem, Car>(Car.Kind)
        .ReadFrom(() => client.ReadAll())
        .IdentifyBy(item => item.Id)
        .Apply((item, ghost) => ghost.SetPosition(item.Position)));
```

`SdkItem`, `Car` and `client` are your application types. The selectors supply identity and copy data; optional `.WithVariant(item => ...)` selects an appearance. No custom coordinator class is needed for this polling path.

**Return a complete snapshot each time.** An empty collection removes the population. Null, duplicate/empty IDs or an exception stop that source and retain its existing ghosts as unavailable. A partial/delta feed must use a custom `Coordinator` instead.

`SceneSetup` registers its blueprints, creates the anchor under its transform and requests views for configured kinds. Disabling it removes its anchor, ghosts, views and subscription. Re-enable and call `Track` again to restart; toggling the sample's whole Tracking object does this through its bootstrap. Blueprint registrations remain shared realm configuration.

## Optional features

| Need | Use |
| --- | --- |
| Data-only tracking | Leave blueprints empty; views and custom interfaces are optional |
| Consume available entities | `Realm.Default.Query().OfKind(Car.Kind)` |
| SDK push callbacks or delta updates | Subclass `Coordinator`; marshal worker callbacks through `Dispatch` |
| Explicit lifetime or update control | Use `Realm` and `CreateAnchorFor` directly |
| Multiple sources and replacement | Import the **Emas sample** and open its `Scenes/Example.unity` |

Unity advances the default realm automatically. Do not also call `Update()` every frame. See [API](API.md) for contracts and [architecture](Architecture.md) for update order and cleanup.
