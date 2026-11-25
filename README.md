# Emas

Track SDK entities in Unity, keep their state on stable GameObject roots, and create visual views when needed.

**Requires Unity 2022.3+ and C# 8.** Runtime APIs are in `Emas`; application assemblies reference `Emas.Runtime`.

Emas provides entity identity and lifecycle, reusable data traits, queries, optional views, and double-precision spatial projection. Your application owns the SDK connection and maps its data into traits. Every Emas operation runs on Unity's main thread.

## Try it

1. In **Window > Package Manager**, choose **Add package from disk** and select this package's `package.json`.
2. Import **Quick start** from the package's Samples list.
3. Open `QuickStart.unity` and press Play. A tracked marker moves; disabling the Tracking object cleans it up.

The sample includes configured tracking, Ghost and view prefabs. Start with [Getting started](Documentation~/GettingStarted.md) to build the same integration.

## The five roles

| Role | What it does |
| --- | --- |
| `Realm` | Owns tracking, updates, queries and presentation. |
| `PresenceDetector` | Reports SDK arrivals, identity metadata and departures. |
| `Ghost` | The entity's stable root, holding traits and behavior independently of visuals. |
| `Trait<TData>` | Reads a mapped value and applies it to the root. |
| `View` | An optional visual child selected by the entity's Kind and Variant. |

A `Presence` is the stable tracking handle. Its `Key` combines Anchor ID, Kind and entity ID within a Realm. An Anchor groups detector attachments and identities.

## Connect your SDK

1. Author a Ghost prefab with its traits. Use a plain `Ghost` unless combined entity behavior needs a subclass.
2. Create a **Manifestation Blueprint** for its Kind. Assign the root prefab and optional named view variants.
3. Derive `PresenceDetectorComponent` for scene setup, or `PresenceDetector` for code setup. Report membership with `Detect` and `Disappear`.
4. Bind the authored traits in `GhostInitializer.Initialize` or `Realm.RegisterPresenceInitializer<TGhost>`. Readers map SDK data; repeated data changes need no new detection.
5. Add `RealmSetup` and child `AnchorSetup` components, or construct and update a `Realm` yourself.

`RealmSetup` owns an isolated Realm, updates it each frame and disposes it on disable. `Realm.Default` is a separate, automatically updated Realm. A directly constructed Realm requires `Update()` and `Dispose()`.

Resolve `presence.Source` inside trait readers: Emas holds it weakly, and the application owns its lifetime. SDK callbacks must reach Unity's main thread before calling Emas. See [detector integration and recovery](Documentation~/GettingStarted.md#implement-your-sdk-detector).

## Read tracked entities

Use the Realm that owns your entities. This example selects available vehicles in the default Realm:

```csharp
using Emas;

Query vehicles = Realm.Default.Query()
    .OfKind(new Kind("vehicle"))
    .With<Spatial>();

foreach (IGhost vehicle in vehicles)
{
    Spatial spatial = vehicle.GetRequired<Spatial>();
    if (spatial.HasPosition)
    {
        UnityEngine.Debug.Log(vehicle.Key + ": " + spatial.Position);
    }
}
```

Filters return new queries; results have no ordering guarantee. Views and spatial range do not affect membership. `Observe(onEnter, onLeave)` reports membership changes; keep and dispose its subscription. Use `TryGetPresence(key, out presence)` for an exact identity, including pending or temporarily unavailable entities. See [Queries](Documentation~/Queries.md).

## Present positions and motion

Add `Spatial` to a Ghost for shared-coordinate placement. Supply Cartesian `Double3`, WGS84 `GeoPosition`, or ECEF input, then configure an optional fixed or followed `ReferenceFrame`. Global positions remain doubles until projection near the Unity origin.

Optional filters such as `PacketCorrection` and `Smoothing` process observations before `Extrapolation` predicts gaps. **Prediction Interval** uses automatic calculation at zero; **Prediction limit** caps the gap. Configure traits under **Ghost > Traits** and drag their **≡ grips** to order filters. Raw input remains unchanged.

| Extrapolation strategy | Use |
| --- | --- |
| `ConstantAccelerationExtrapolation` | Default: velocity with optional linear acceleration. |
| `ConstantVelocityExtrapolation` | Continue linear and angular velocity. |
| `SingerExtrapolation` | Gradually decay observed acceleration during a gap. |
| `CoordinatedTurnExtrapolation` | Rotating-body aircraft motion; optional Filter traits preprocess observations. |
| `ConstantTurnAccelerationExtrapolation` | Continue turning while speed changes; estimate missing body acceleration from history. |
| `ConstantTurnClimbAccelerationExtrapolation` | Accelerate through a horizontal turn while holding vertical climb rate constant. |
| `SplineExtrapolation` | Continue cubic Bézier motion fitted to timestamped pose or velocity history. |
| `NoExtrapolation` | Hold observations during gaps; retain buffered interpolation. |

For example, inside an initializer where `ghost` has an authored Extrapolation trait:

```csharp
Extrapolation extrapolation = ghost.Trait<Extrapolation>();
extrapolation.MaximumExtrapolation = 0.1f; // Seconds beyond the latest observation.
extrapolation.Strategy = new SingerExtrapolation
{
    ManeuverTimeConstant = 0.5f // Seconds for acceleration to decay to about 37%.
};
```

Use original SDK timestamps on one shared clock. `Realm.InterpolationDelay` adds shared playback buffering; half-lives control filtering, `PredictionTime` requests forward prediction, and `MaximumExtrapolation` caps prediction. These are separate settings. See [Spatial input, timing and strategies](Documentation~/Spatial.md) for units, axes and examples of every strategy.

**Realm Setup > Presentation** groups automatic views, view distance and processing distance for every Anchor. Enable **Limit processing distance** to use the latest raw poses outside that radius, without buffering, prediction or smoothing. These entities remain visible subject to the separate view-distance limit. Code-owned Realms use `realm.PoseProcessingDistance`; null processes every distance.

## Custom extrapolation strategies

Implement `IExtrapolationStrategy.Predict`. Emas calls it once per Ghost projection with the filtered state and prediction intervals. This simple example extends linear velocity and holds rotation:

```csharp
using System;
using Emas;
using UnityEngine;

[Serializable]
public sealed class LinearPrediction : IExtrapolationStrategy
{
    public Prediction Predict(PredictionRequest request)
    {
        Double3 velocity = request.PositionContext.Velocity.GetValueOrDefault();
        return request.CreatePrediction(
            velocity * request.PositionSeconds, Quaternion.identity, velocity);
    }
}
```

Assign it with `ghost.Trait<Extrapolation>().Strategy = new LinearPrediction();`, or select it in the Inspector. For angular prediction, use `RotationContext` and `RotationSeconds`; `CreatePrediction` handles offset composition and axis conversion.

## Custom filters

Derive from `Filter` and override `Process`. Modify the state copy and return it. This example adds a fixed position correction in source units:

```csharp
using Emas;

public sealed class PositionOffset : Filter
{
    protected override FilterState Process(FilterState input, FilterContext context)
    {
        if (input.Position.HasValue)
        {
            input.Position += new Double3(0, 0.1, 0);
        }

        return input;
    }
}
```

Add it through the Ghost Inspector or `ghost.gameObject.AddComponent<PositionOffset>();`. Enabled filters run top to bottom before prediction. Keep timestamps consistent with the estimated state and clear any stored history in `OnReset()`. See [filter and strategy details](Documentation~/Spatial.md#filter-observations-before-prediction).

## Samples and documentation

| Sample | Demonstrates |
| --- | --- |
| [Quick start](Samples~/Minimal/README.md) | One marker, three small integration classes and authored prefabs. |
| [Example](Samples~/Example/README.md) | Cars, reusable traits, queries and SDK replacement. |
| [Relative world](Samples~/RelativeWorld/README.md) | WGS84 input, a moving reference, buffering and noisy motion. |
| [Aircraft formation](Samples~/AircraftFormation/README.md) | Aircraft turns, independent observation streams and flight instruments. |

[Getting started](Documentation~/GettingStarted.md) · [API reference](Documentation~/API.md) · [Architecture](Documentation~/Architecture.md) · [Integration guidelines](Documentation~/Guidelines.md) · [Changelog](CHANGELOG.md)

## Validate the package

Open **`Tests/Unity~`** in **Unity 2022.3.62f3** and run EditMode and PlayMode tests. The repository root is a UPM package; the nested project includes its test configuration and imported samples.

On Windows, close that project in Unity and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools~\Validate.ps1
```

See [Validation](Documentation~/Validation.md) for editor selection, reports, CI and testing in a consuming project.
