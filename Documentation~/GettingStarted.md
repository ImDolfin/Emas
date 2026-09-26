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
    public sealed class Bootstrap : MonoBehaviour, ISourceProvider
    {
        public PresenceSource CreateSource()
        {
            return new PollingPresenceSource<Reading, Marker>(Marker.Kind)
                .ReadFrom(() => new[] { new Reading(id: "one", position: new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f)) })
                .IdentifyBy(item => item.Id)
                .Apply((item, ghost) => ghost.SetPosition(item.Position));
        }
    }
}
```

`Reading` stands in for your SDK payload. Replace `ReadFrom` with a complete collection from your client and map its data in `Apply`. `IdentifyBy` must return stable, non-empty IDs. Positions here are **anchor-local**, not world coordinates.

### Configure the scene and view

1. Create a cube prefab for the visual child. Keep its local position/rotation at zero and scale at one.
2. Create **Assets > Create > Emas > Blueprint**. Set **Kind Id** to `minimal.marker` and **Fallback View Prefab** to the cube prefab. Leave **Ghost Prefab** and **Views** empty.
3. Create a scene object named Tracking. Add **Emas > Realm Setup**, **Emas > Anchor Setup** and `Bootstrap`. On Realm Setup, assign the blueprint as a realm default. On Anchor Setup, set **Anchor Id** to `quick-start` and leave **Automatic Views** enabled.
4. Press Play. Realm Setup creates its own realm, attaches the anchor's one source, and updates the realm each frame. Emas creates a `Marker` root beneath the anchor and attaches the cube view. Move Tracking to move its anchor frame.

Each Realm Setup owns one isolated realm. You can put several in a scene or prefab, with any number of Anchor Setup objects beneath each one. Each anchor needs exactly one enabled component implementing `ISourceProvider` on the same object; `CreateSource()` supplies one presence source each time that anchor starts. Assign any number of default blueprints to Realm Setup and optional overrides to each anchor, with at most one per kind in either list. Empty blueprint lists are valid for data-only tracking. A nested Realm Setup owns its own anchors. Use `realmSetup.Realm` for queries and lookups; it is null while stopped. The setup starts on the first update after enable in Play Mode and calls its realm's `Update()` each frame. `StopRealm()` disposes that realm and its anchors; `StartRealm()` can start it again. Disabling the component or its object also stops it.

Direct code setup is unchanged: `Realm.Default` is still automatically updated, and a realm created with `new Realm()` is still advanced through explicit `Update()` calls. You can configure its anchors, sources, blueprints and reference frame through the existing APIs.

Use `anchorSetup.Anchor.RestartSource(source)` to restart an attached source and `ReplaceSource` to change its instance. A successful handover reuses compatible roots republished during startup; unreported roots are removed after the first subsequent update and queued startup publications finish. A source failure removes its population immediately, so restarting a failed source creates new roots.

When you already know the identity, look it up directly:

```csharp
Realm realm = GetComponent<RealmSetup>().Realm;
Key key = new Key("quick-start", Marker.Kind, "one");
IGhost ghost;
if (realm != null && realm.TryGetGhost(key, out ghost) && ghost.IsAvailable)
{
    Debug.Log(ghost.Name);
}
```

The anchor ID must match your Anchor Setup. Lookup also finds prepared ghosts and unavailable roots during startup handover; removal returns `false`.

For interface-based consumers, paired query arrivals/departures and source replacement, import **Emas sample** and follow its [file guide](../Samples~/Example/README.md). Consumers use `IGhost.TryGet<T>` for optional interfaces or `ghost.GetRequired<T>()` when a missing provider should fail immediately; the application owns those interfaces.

## Choose a source

| Source | Choose when | What deletion means |
| --- | --- | --- |
| `PollingPresenceSource` | The SDK can return the complete current population on startup and each update | Omitted IDs disappear after a successful read; an empty collection removes all |
| `CallbackPresenceSource` | The SDK supplies individual changes and removals | An explicit remove callback or configured inactivity timeout deletes an ID |
| Custom `PresenceSource` | The integration needs its own lifecycle or multiple feeds | Call protected `Remove` or configure an inactivity timeout |

Polling reads on every update by default. For slower feeds, add `.PollEvery(System.TimeSpan.FromMilliseconds(500))` to the builder before tracking. Startup still reads immediately; later reads use unscaled elapsed time, without catch-up bursts. Existing data remains available between reads unless its configured inactivity timeout expires. Restart resets the interval.

The callback builder uses `IdentifyBy`, `Apply` and `Listen`. `Listen` receives publish/remove callbacks and returns an unsubscribe action. Publish initial data inside `Listen`; every event is deferred to a later realm update. Import **Callback quick start**, open `Callbacks.unity`, and inspect its [bootstrap](../Samples~/Callbacks/Bootstrap.cs) for complete wiring, initial population and cleanup.

To remove entities that silently stop publishing, set `source.InactivityTimeout = System.TimeSpan.FromSeconds(10)` before tracking, choosing a duration suited to your feed. Null, the default, disables expiry. Each partial publication refreshes that entity's deadline; custom sources updating cached ghosts call protected `MarkPublished(ghost)`. Expiry removes the root and view. A later publication creates a new root.

Call all Emas APIs, including publish/remove callbacks, on Unity's main thread. Your SDK adapter is responsible for delivering events there. Keep callback payloads unchanged until processed. SDK ownership, coordinate conversion and recovery are covered in [Guidelines](Guidelines.md); exact scheduling and failure contracts are in [API](API.md).

## Keep a network vehicle fixed in Unity

For moving-reference worlds or large global coordinates, configure the **Reference Frame** section of Realm Setup or assign `realm.ReferenceFrame` by code, and add `Spatial` to participating Ghost roots. Choose a manual simulation position or a ghost key to follow; optionally set a presentation distance. Publish simulation positions as `Double3`; the realm calculates the relative displacement before converting to Unity floats. Position and orientation publications can arrive independently. A presentation range hides distant views while keeping their data tracked.

Follow the [relative-world guide](Spatial.md) for a fixed ego car, reference loss and source mapping. Import the separate **Relative world** sample, open `RelativeWorld.unity`, and press Play to see the demonstration with its own realm and generated visuals.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Nothing appears | Check Realm Setup, Anchor Setup and Blueprint Inspector errors, matching kind IDs and the view prefab. A ghost can be available without a view. |
| A source stops | Its ghosts are removed. Open **Window > Emas** during Play Mode or read `source.LastErrorContext` and `source.LastError`. Fix the cause and call `anchor.RestartSource(source)` to repopulate. Assign `source.Name` to distinguish feeds. |
| One view fails | Read its ghost/prefab error in the Console. Tracking stays active. Fix the cause and call `Manifest`, or change its blueprint, variant or detail to retry. |
| Polling entities disappear | Return the full population, not only changes. Null, duplicate/empty IDs and mapping exceptions stop the source. |
| Restart creates duplicates | Unsubscribe in callback cleanup; dispose consumer query subscriptions when their owner stops. |
| No tests appear | Open the prepared **`Tests/Unity~`** project through Unity Hub. Package import alone does not opt a consumer into tests. See [Validation](Validation.md). |
