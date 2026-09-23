# Getting started

Emas turns application data into stable scene ghosts with optional visual children. Use Unity 2022.3 or newer; see [validated versions](Validation.md#results).

## Run the sample

Add `package.json` through **Package Manager > Add package from disk**, import **Quick start**, open its `QuickStart.unity` scene and press Play. One cube moves along its anchor's X axis. Disable and re-enable the Tracking object to exercise cleanup and restart.

## Build the same integration

These three files are the complete [Quick start](../Samples~/Minimal/) code, without XML comments. Put them in separate files in your application assembly, referencing `Emas.Runtime` if you use an assembly definition. If you imported the sample, edit those files instead of creating duplicate types.

### Reading.cs

```csharp
using UnityEngine;

namespace Emas.Minimal
{
    public sealed class Reading
    {
        public Reading(string id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        public string Id
        {
            get;
            private set;
        }

        public Vector3 Position
        {
            get;
            private set;
        }
    }
}
```

### Marker.cs

```csharp
using UnityEngine;

namespace Emas.Minimal
{
    public sealed class Marker : Ghost
    {
        public static readonly Kind Kind = new Kind("minimal.marker");

        public void SetPosition(Vector3 position)
        {
            transform.localPosition = position;
        }
    }
}
```

### Bootstrap.cs

```csharp
using UnityEngine;

namespace Emas.Minimal
{
    public sealed class Bootstrap : MonoBehaviour
    {
        private void OnEnable()
        {
            GetComponent<SceneSetup>().Track(new PollingPresenceSource<Reading, Marker>(Marker.Kind)
                .ReadFrom(() => new[] { new Reading(id: "one", position: new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f)) })
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.SetPosition(item.Position)));
        }
    }
}
```

`Reading` stands in for your SDK payload. Replace `ReadFrom` with a complete collection from your client and map its data in `Apply`. `IdentifyBy` must return stable, non-empty IDs. Positions here are **anchor-local**, not world coordinates.

### Configure the scene and view

1. Create a cube prefab for the visual child. Keep its local position/rotation at zero and scale at one.
2. Create **Assets > Create > Emas > Blueprint**. Set **Kind Id** to `minimal.marker` and **Fallback View Prefab** to the cube prefab. Leave **Ghost Prefab** and **Views** empty.
3. Create a scene object named Tracking. Add **Emas > Scene Setup** and the `Bootstrap` component. Set a unique **Anchor Id**, assign the blueprint and leave **Automatic Views** enabled.
4. Press Play. Emas creates a `Marker` root beneath the anchor, updates its data and attaches the cube view. Move Tracking to see the coordinate frame move with it.

Unity advances `Realm.Default` automatically. `SceneSetup.StopTracking()` removes its anchor, ghosts and views while leaving the component enabled; another `Track` call starts again. Disabling also removes tracking; the bootstrap calls `Track` again on re-enable. Toggle the whole Tracking object so both components share that lifetime. Inspector blueprints apply only to that anchor and leave with it. An empty blueprint list is valid for data-only tracking.

To retry an attached source while retaining its ghosts, call `setup.Anchor.RestartSource(source)`. Use `ReplaceSource` when changing the source instance. Both require new publication before retained data becomes available.

When you already know the identity, look it up directly:

```csharp
Key key = new Key("default", Marker.Kind, "one");
IGhost ghost;
if (Realm.Default.TryGetGhost(key, out ghost) && ghost.IsAvailable)
{
    Debug.Log(ghost.Name);
}
```

The anchor ID must match your SceneSetup. Lookup also finds retained unavailable ghosts, whose data may be stale; removal returns `false`.

For interface-based consumers, paired query arrivals/departures and source replacement, import **Emas sample** and follow its [file guide](../Samples~/Example/README.md). Consumers use `IGhost.TryGet<T>` for optional interfaces or `ghost.GetRequired<T>()` when a missing provider should fail immediately; the application owns those interfaces.

## Choose a source

| Source | Choose when | What deletion means |
| --- | --- | --- |
| `PollingPresenceSource` | The SDK can return the complete current population on startup and each update | Omitted IDs disappear after a successful read; an empty collection removes all |
| `CallbackPresenceSource` | The SDK supplies individual changes and removals | Only an explicit remove callback deletes an ID |
| Custom `PresenceSource` | The integration needs its own lifecycle or multiple feeds | Call protected `Remove` yourself |

Polling reads on every update by default. For slower feeds, add `.PollEvery(System.TimeSpan.FromMilliseconds(500))` to the builder before tracking. Startup still reads immediately; later reads use unscaled elapsed time, without catch-up bursts. Existing data remains available between reads. Restart resets the interval.

The callback builder uses `IdentifyBy`, `Apply` and `Listen`. `Listen` receives publish/remove callbacks and returns an unsubscribe action. Publish initial data inside `Listen`; every event is deferred to a later realm update. Import **Callback quick start**, open `Callbacks.unity`, and inspect its [bootstrap](../Samples~/Callbacks/Bootstrap.cs) for complete wiring, initial population and cleanup.

Call all Emas APIs, including publish/remove callbacks, on Unity's main thread. Your SDK adapter is responsible for delivering events there. Keep callback payloads unchanged until processed. SDK ownership, coordinate conversion and recovery are covered in [Guidelines](Guidelines.md); exact scheduling and failure contracts are in [API](API.md).

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Nothing appears | Check Blueprint and SceneSetup Inspector errors, matching kind IDs and the view prefab. A ghost can be available without a view. |
| A source stops | Open **Window > Emas** during Play Mode. Inspect its label, status and failure details, or read `source.LastErrorContext` and `source.LastError`. Fix the cause and call `anchor.RestartSource(source)`. Assign `source.Name` to distinguish feeds. |
| Polling entities disappear | Return the full population, not only changes. Null, duplicate/empty IDs and mapping exceptions stop the source. |
| Restart creates duplicates | Unsubscribe in callback cleanup; dispose consumer query subscriptions when their owner stops. |
| No tests appear | Open the prepared **`Tests/Unity~`** project through Unity Hub. Package import alone does not opt a consumer into tests. See [Validation](Validation.md). |
