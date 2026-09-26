# Getting started

Emas tracks SDK entities as stable Presences, initializes invisible Ghost roots and adds visual manifestations when requested. Use Unity 2022.3 or newer; see [validated versions](Validation.md#results).

## Run the sample

Add `package.json` through **Package Manager > Add package from disk**, import **Quick start**, open its `QuickStart.unity` scene and press Play. One cube moves along its anchor's X axis. Disable and re-enable the Tracking object to exercise cleanup and restart.

## Build the same integration

These five files show the [Quick start](../Samples~/Minimal/) detector and module flow without XML comments. Put each class in its own file in your application assembly, referencing `Emas.Runtime` if you use an assembly definition. If you imported the sample, edit its files instead of creating duplicate types.

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

        public string Id { get; private set; }
        public Vector3 Position { get; private set; }
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

### MarkerPositionModule.cs

```csharp
namespace Emas.Minimal
{
    public sealed class MarkerPositionModule : EntityModule<Reading>
    {
        private readonly Marker _marker;

        public MarkerPositionModule(Marker marker)
        {
            _marker = marker;
        }

        public override void Apply(Reading reading)
        {
            _marker.SetPosition(reading.Position);
        }
    }
}
```

### MarkerDetector.cs

```csharp
using UnityEngine;

namespace Emas.Minimal
{
    internal sealed class MarkerDetector : PresenceDetector
    {
        protected override void OnStart()
        {
            PublishReading();
        }

        protected override void OnUpdate()
        {
            PublishReading();
        }

        private void PublishReading()
        {
            Reading reading = new Reading("one", new Vector3(Mathf.Sin(Time.time) * 2f, 0f, 0f));
            Report(reading.Id, Marker.Kind, reading);
        }
    }
}
```

### Bootstrap.cs

```csharp
using UnityEngine;

namespace Emas.Minimal
{
    public sealed class Bootstrap : MonoBehaviour, IDetectorProvider, IRealmConfigurator
    {
        public void ConfigureRealm(Realm realm)
        {
            realm.RegisterPresenceInitializer<Marker>(Marker.Kind, (presence, marker) =>
            {
                MarkerPositionModule module;
                if (!presence.TryGetModule(out module))
                {
                    presence.AddModule(new MarkerPositionModule(marker));
                }
            });
        }

        public PresenceDetector CreateDetector()
        {
            return new MarkerDetector();
        }
    }
}
```

`Reading` stands in for an SDK observation. The detector identifies its entity and forwards each reading to the realm. `ConfigureRealm` runs before the anchor's detector starts, chooses the `Marker` Ghost root and installs the module that copies position data into it. The detector never creates or updates the Ghost itself. Positions here are **anchor-local** Unity coordinates.

### Configure the scene and view

1. Create a cube prefab for the visual child. Keep its local position and rotation at zero and scale at one.
2. Create **Assets > Create > Emas > Manifestation Blueprint**. Set **Kind Id** to `minimal.marker` and **Fallback View Prefab** to the cube prefab. Leave **Ghost Prefab** and **Variants** empty.
3. Create a scene object named Tracking. Add **Emas > Realm Setup**, **Emas > Anchor Setup** and `Bootstrap`. On Realm Setup, assign the manifestation blueprint as a realm default. On Anchor Setup, set **Anchor Id** to `quick-start` and leave **Automatic Views** enabled.
4. Press Play. Realm Setup creates its realm, calls `Bootstrap.ConfigureRealm`, then attaches the anchor's detector. Emas creates a `Presence` and an invisible `Marker` root beneath the anchor, applies each reading through `MarkerPositionModule` and attaches the cube view. Move Tracking to move its anchor frame.

For several appearances of one Kind, create a **Manifestation Variant** asset for each appearance and assign its detail-level view prefabs. Add those assets to the Kind's Manifestation Blueprint. A Kind with no blueprint still gets its Ghost root and remains visually silent until a view is configured.

Each Realm Setup owns one isolated realm and can have several Anchor Setup objects beneath it. Each active anchor needs exactly one enabled `IDetectorProvider` component on the same GameObject; its `CreateDetector()` returns a new or detached detector each time that anchor starts. Enabled `IRealmConfigurator` components beneath the Realm Setup register root and module initializers before detectors start. Nested Realm Setup objects own their own configurators and anchors. Assign realm default manifestation blueprints and optional anchor overrides, at most one per Kind in each list. A blueprint without any view prefabs is silent too. `realmSetup.Realm` is null while stopped. The setup starts on the first update after enable in Play Mode, updates its realm each frame and disposes it on disable or `StopRealm()`. `StartRealm()` creates a fresh realm.

`Realm.Default` is still updated automatically. With `new Realm()`, register initializers and blueprints, attach detectors to anchors, and call `Update()` yourself.

### Find and manifest a presence

A query can find available Ghost roots across all live realms, including realms created later:

```csharp
Query markers = Query.All().OfKind(Marker.Kind);
System.IDisposable subscription = markers.OnAvailable(ghost => Debug.Log(ghost.Name));
```

Dispose the subscription when its consumer stops. Use `realmSetup.Realm.Query(markers)` to apply the same filters to one setup.

When you know the identity, use the stable `Presence` handle. This example requests a view explicitly; turn off **Automatic Views** on the anchor when you want to control manifestation yourself.

```csharp
Realm realm = GetComponent<RealmSetup>().Realm;
Key key = new Key("quick-start", Marker.Kind, "one");
Presence presence;
if (realm != null && realm.TryGetPresence(key, out presence) && presence.IsAvailable)
{
    realm.Manifest(presence, DetailLevel.Full);
}
```

`realm.Demanifest(presence)` removes its view while retaining the detected Presence and Ghost root. `realm.SetDetailLevel(presence, DetailLevel.Reduced)` changes a requested view's detail. `TryGetPresence` can also find an unavailable Presence during startup handover or disappearance grace; check `IsAvailable` before consuming its data. `TryGetGhost` remains available when you only need the root.

Use `anchorSetup.Anchor.RestartDetector(detector)` to restart an attached detector and `ReplaceDetector` to change its instance. A successful handover reuses compatible roots and Presence handles when IDs are reported again. Detector failure removes its population immediately, so recovery creates new handles and roots.

For root interfaces, paired query arrivals and departures, and detector replacement, import **Emas sample** and follow its [file guide](../Samples~/Example/README.md). Consumers use `IGhost.TryGet<T>` for optional root interfaces or `ghost.GetRequired<T>()` when a missing provider is an error.

## Implement your SDK detector

Use an application-specific subclass of `PresenceDetector`. Pass the application-owned SDK client to its constructor and override only the lifecycle methods that feed needs:

| Override | Responsibility |
| --- | --- |
| `OnStart()` | Read initial data or subscribe to SDK events for this attachment |
| `OnUpdate()` | Poll the SDK when needed; choose any polling interval in your detector |
| `OnStop()` | Unsubscribe and release attachment-owned resources, including after startup failure |

Call `Report(id, kind, reading, name, variant, capabilities)` to send SDK data to the realm, or `Detect(id, kind, name, variant, capabilities)` for metadata alone. The optional name labels the entity; the variant chooses its appearance. Call `Disappear(kind, id)` when the SDK removes an entity. The minimal sample has one permanent entity, so it needs no omission tracking or subscription cleanup.

For a complete-snapshot SDK, compare each successful read's IDs with `OwnedPresences` and explicitly call `Disappear` for missing IDs. A missing item in a change-only feed is not a removal. Your detector owns the SDK's validation, scheduling and omission rules; Emas owns the resulting Presence lifecycle. The [README example](../README.md#3-connect-the-detector-and-realm) demonstrates snapshot comparison.

For SDK events, call `CaptureDispatcher()` in `OnStart` and close each event handler over the returned dispatcher. Queue `Report` or `Disappear` through it, retain the exact delegates, and unsubscribe in `OnStop`. Each captured dispatcher belongs to one attachment, so callbacks retained after a restart cannot change the new attachment. `OnStop` also follows failed startup; make cleanup safe when only some subscriptions were acquired. Import **Callback quick start**, open `Callbacks.unity`, and inspect [FeedDetector.cs](../Samples~/Callbacks/FeedDetector.cs) for a complete example. SDK clients remain application-owned.

Pass SDK-reported interface types through the optional `capabilities` argument. These do not add Unity components automatically. A realm initializer can inspect `presence.HasCapability<T>()` and install a matching module. It runs again when capabilities change, so check `presence.TryGetModule<T>(out module)` before adding another instance. Compatible `EntityModule<TData>` instances apply SDK updates to the Ghost. Unmatched payloads are ignored while the Presence remains tracked; exceptions from a module's `Apply` stop its detector.

To detect silence in a feed that should publish regularly, set `detector.InactivityTimeout = System.TimeSpan.FromSeconds(10)` before attachment. Null, the default, disables inactivity expiry. Each detection or data report resets that entity's deadline. Set `detector.DisappearanceGracePeriod` to retain a disappeared Presence and Ghost root for a while; zero, the default, removes them immediately. Disappearance makes it unavailable to queries at once. A new detection or report during grace restores the same handle and root; after grace expires, either creates a new one. Detector failure and anchor removal always remove immediately.

Call all Emas APIs, including SDK publish and disappear callbacks, on Unity's main thread. The application handles any thread transfer; a captured dispatcher defers work on the main thread and does not transfer it between threads. Copy mutable callback payloads before publishing or keep them unchanged until their queued report runs. See the exact lifecycle and module contracts in [API](API.md).

## Keep a network vehicle fixed in Unity

For moving-reference worlds or large global coordinates, configure the **Reference Frame** section of Realm Setup or assign `realm.ReferenceFrame` by code, and add `Spatial` to participating Ghost roots. Choose a manual reference position in the shared coordinate system or a ghost key to follow; optionally set a presentation distance. Publish positions in that shared system as `Double3`; the realm calculates the relative displacement before converting to Unity floats. Position and orientation publications can arrive independently. A presentation range hides distant views while keeping their data tracked.

Follow the [relative-world guide](Spatial.md) for a fixed ego car, reference loss and SDK data mapping. Import the separate **Relative world** sample, open `RelativeWorld.unity`, and press Play to see the demonstration with its own realm and generated visuals.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Nothing appears | Check the Realm Setup, Anchor Setup, Manifestation Blueprint and Manifestation Variant Inspectors for errors. Verify the kind ID and view prefab; an available ghost may intentionally have no view. |
| A detector stops | Its Presences and Ghosts are removed. While the prefab realm runs, find the detector in `anchorSetup.Anchor.Detectors` and inspect `LastErrorContext` and `LastError`. Fix the cause, then call `anchorSetup.Anchor.RestartDetector(detector)`. If startup stopped the realm, use the Console or an application-held detector reference. **Window > Emas** shows only `Realm.Default`. |
| One view fails | Read its ghost/prefab error in the Console. Tracking stays active. Fix the cause and call `Manifest`, or change its manifestation blueprint, variant or detail to retry. |
| Polled entities disappear unexpectedly | Check your detector's snapshot comparison and timeout. Compare omissions only for complete reads; use explicit SDK removals for change-only feeds. Exceptions escaping lifecycle methods or dispatched actions stop the detector. |
| Restart creates duplicates | Unsubscribe in `OnStop`, capture a new dispatcher in each `OnStart`, and dispose consumer query subscriptions when their owner stops. |
| No tests appear | Open the prepared **`Tests/Unity~`** project through Unity Hub. Package import alone does not opt a consumer into tests. See [Validation](Validation.md). |
