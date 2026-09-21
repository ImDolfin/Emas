# API reference

Runtime APIs use the `Emas` namespace. `Context.Default` is updated automatically by Unity; an isolated `Context` implements `IDisposable` and advances through explicit `Update()`.

## Tracking and lifecycle

| Operation | Contract |
| --- | --- |
| `RegisterBlueprint(blueprint)` | Register prefab/view configuration by kind |
| `CreateOriginFor(id, params coordinators)` | Create an origin and start its coordinators; overload accepts a `Transform` frame |
| `Prepare<TGhost>(originId, kind, entityId, variant = null)` | Optionally create an unavailable identity before discovery |
| `Query(partialName = null)` | Describe filters over available ghosts |
| `Query(description)` | Rebind an existing query description to this context |
| `RemoveOrigin(id)` | Stop its coordinators and remove all its records, including prepared ghosts |
| `Update()` | Advance an explicitly managed context; the default context advances automatically |
| `context.Dispose()` | Release the context and all owned state |

An `Origin` exposes `Id`, `Transform`, `Context`, `AddCoordinator`, `RemoveCoordinator`, `ReplaceCoordinator(current, replacement)` and `Dispose()`. Replacement retains compatible identities; removal destroys the removed coordinator's population. See [lifecycle rules](Architecture.md#failure-and-cleanup).

## Coordinator and ghost contracts

| Member | Use |
| --- | --- |
| `OnStart`, `OnUpdate`, `OnStop` | Override source lifecycle hooks |
| `GetOrCreate<TGhost>(entityId, kind, variant = null)` | Obtain a stable owned ghost; another overload accepts a display name |
| `Remove(kind, entityId)` | Remove one owned ghost |
| `Dispatch(action)` | Queue main-thread source work; stopped/stale registrations cannot execute it |
| `OwnedGhosts` | Snapshot of owned ghosts, including unavailable ones |
| `IGhost.Key`, `Name`, `Variant`, `IsAvailable` | Read identity, label, appearance and availability |
| `IGhost.TryGet<T>(out part)` | Resolve a root component contract; excludes view children and rejects ambiguous providers |

Source-specific types and coordinate conversion stay in application coordinators. One coordinator owns each identity; an application coordinator can compose multiple feeds.

## Queries and subscriptions

Queries are immutable and combine all filters. They never create ghosts or components.

| Filter/result | Meaning |
| --- | --- |
| `Query(partialName)` / `WithExactName(name)` | Case-insensitive substring / exact display-name match |
| `OfKind(kind)` / `InOrigin(id)` / `WithVariant(variant)` | Exact, case-sensitive ID match |
| `With<T>()` | Require a root contract |
| Enumeration / `Count` | Current available matches; empty when none match |
| `FirstOrDefault()` | First match or null; no ordering guarantee |
| `Single()` | Exactly one match; otherwise throws |
| `OnAvailable(callback)` | Notify current and future complete matches; dispose the returned subscription to stop |

Subscriptions notify once while a ghost remains a match. Availability loss permits a fresh notification on recovery. Outside source/finalization/notification callbacks, current matches notify immediately. Inside those phases, notification is deferred; subscriptions created during notification wait for a later update. Callback exceptions are isolated, and each match is rechecked before invoking the callback.

## Typed values

```csharp
public static readonly Kind Car = new Kind("vehicles.car");
public static readonly Variant SmallCar = new Variant("small-car");
```

Declare named constants in application classes for autocomplete. Kinds and variants are distinct extensible value types; raw strings and kinds cannot be passed as variants. A variant does not enforce membership in a kind or guarantee a prefab mapping.

| Value | Meaning |
| --- | --- |
| `Key` | Identity tuple: origin ID, kind and entity ID |
| Omitted/null variant in `Prepare` or `GetOrCreate` | Preserve the existing appearance |
| `Variant.None` | Unspecified appearance; explicitly passing it clears the appearance |
| `WithVariant(None)` | Match unspecified appearances; omitting the filter matches any appearance |
| `DetailLevel` | Non-negative level: None=0, Minimal=1, Reduced=2, Full=3; custom levels allowed |

## Views and blueprints

| Operation | Effect |
| --- | --- |
| `Manifest(ghost)` | Request Full for a new request; preserve an existing requested degree |
| `Manifest(ghost, degree)` | Request the selected degree; return the current view or null |
| `SetDegree(ghost, degree)` | Update degree; does not create a request for a never-requested ghost |
| `Demanifest(ghost)` or degree None | Remove the view and request, preserving the ghost |

A blueprint supplies a kind, optional ghost prefab, view mappings and optional fallback. Configure through the Inspector or `Configure(kind, ghostPrefab, mappings, fallback)`. A mapping is `ViewMapping(variant, degree, prefab)`; duplicate variant/degree pairs, non-positive mapping degrees and null view prefabs are rejected.

Selection: **exact variant/degree > highest lower positive degree for that variant > fallback > no view**. Missing selection removes an obsolete view and reports a diagnostic. Selecting the same prefab rebinds it; another prefab replaces only the child. Without a ghost prefab, Emas creates a root with the requested ghost component.

Views require an available ghost and a positive request. View binding finishes before activation. Requests made during source changes/finalization defer refresh, so `Manifest` may return the previous view or null until that phase completes. Requests outside those phases refresh immediately.

Source: [context](../Runtime/Context.cs), [queries](../Runtime/Queries/Query.cs), [blueprints](../Runtime/Views/Blueprint.cs).
