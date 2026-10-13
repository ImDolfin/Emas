# Queries

Use a `Query` to select **currently available Ghost roots**, read their root contracts, or observe them entering
and leaving a result set. Query operations and callbacks run on Unity's main thread. A query describes filters;
it does not advance a Realm, create Ghosts, request views, or make an unavailable entity available.

The snippets assume `using Emas;`, `using System;`, `using System.Collections.Generic;`, and
`using UnityEngine;`. Add `using System.Linq;` for the LINQ examples.

The runnable [Example consumer](../Samples~/Example/Behaviors/SampleStatus.cs) filters cars by read-only interfaces
and variant. The [Relative World consumer](../Samples~/RelativeWorld/GeoQueryExample.cs) observes parked cars and
reads the bird's geographic separation. Both are already authored in their tracking prefabs and release their
subscriptions when disabled or when Realm Setup restarts.

## Select a scope and compose filters

For a scene-owned Realm, get `RealmSetup.Realm` after startup. For an application-owned Realm, use the instance
you already created. This example uses the Relative World sample's Anchor and Kind:

```csharp
Query cars = realm.Query()
    .InAnchor("relative-world")
    .OfKind(new Kind("relative.car"))
    .With<Spatial>();

Query parkedCars = cars.WithVariant(new Variant("target"));
Query drivingReference = cars.WithVariant(new Variant("origin"));

int parkedCount = parkedCars.Count;
IGhost reference = drivingReference.FirstOrDefault();
```

Every filter returns a **new immutable description**. `cars` still selects both driving and parked cars;
deriving `parkedCars` does not change `cars`, `drivingReference`, or any existing subscription. Keep the returned
query: calling `cars.WithVariant(...)` and discarding the result leaves `cars` unchanged.

Filters combine with AND. Repeated `OfKind`, `InAnchor`, `WithExactName`, or `WithVariant` calls replace that
selector. Repeated `With<T>()` calls add contract requirements. `InAnchor(null)` and `WithExactName(null)` clear
their respective selectors. `WithVariant(Variant.None)` selects unspecified appearances; it does not remove
the variant filter. Derive another query from the unfiltered description when you need a different selection.

`realm.Query("bird")` matches a case-insensitive substring of **Ghost.Name display metadata**.
`WithExactName("Circling bird")` matches the whole display name, also case-insensitively. Neither filters
`Key.EntityId` or the Unity GameObject name. The initial partial-name selector and an exact-name selector
both apply. Anchor, Kind, and Variant IDs use case-sensitive ordinal equality.

## Read results and root contracts

`Count`, `FirstOrDefault()`, and `Single()` evaluate current matches each time they are used. A cached Query
is reusable as entities arrive, disappear, change metadata, or gain or lose a required component.

```csharp
foreach (IGhost ghost in parkedCars)
{
    Spatial spatial;
    if (ghost.TryGet<Spatial>(out spatial) && spatial.HasPosition)
    {
        GeoPosition measuredPosition = GeoPosition.FromEarthCentered(spatial.Position);
        Debug.Log(ghost.Key + ": " + measuredPosition);
    }
}
```

`With<T>()` requires exactly one live root MonoBehaviour providing `T`, including an interface implemented by
a trait. For example, the Example sample uses `.With<I3DPosition>().With<IArticulate>()`, then reads those
contracts through `IGhost.TryGet<T>()`. Disabled components remain eligible. Child view components do not
provide root contracts; reported SDK capability interfaces do not create components or satisfy this filter
on their own. Missing or ambiguous providers do not match; ambiguous root providers also produce the usual
contract-resolution diagnostic.

A component requirement does not guarantee that it has received data. Check application readiness such as
`Spatial.HasPosition` before consuming a channel. Queries include available roots with no view, hidden views,
or spatial presentation outside the reference range. Apply your own visibility or distance policy when needed.

Results have **no ordering guarantee**. `FirstOrDefault()` returns null when there is no match and chooses an
unspecified first result when several match. `Single()` throws `InvalidOperationException` for zero or multiple
matches; use it only when exactly-one membership is an application invariant, rather than during delayed startup.

Each `foreach` or `GetEnumerator()` captures a new **membership snapshot**. Its Ghost references remain live:
their data can change and their Unity objects can be destroyed. Holding an array or list does not preserve
availability or freeze source data. A scoped query returns empty results after its Realm is disposed;
`FirstOrDefault()` returns null, and `Single()` still throws because the count is zero.

## Observe membership and own the subscription

Use `Observe` when a consumer keeps a set, list, or dictionary of matching identities. Initialize callback
state before subscribing, because current matches may be delivered synchronously:

```csharp
Dictionary<Key, IGhost> tracked = new Dictionary<Key, IGhost>();
IDisposable subscription = parkedCars.Observe(
    ghost => tracked[ghost.Key] = ghost,
    key => tracked.Remove(key));
```

Store the handle in the consumer. When it stops, including `OnDisable`, dispose it and clear retained state:

```csharp
subscription.Dispose();
tracked.Clear();
```

Entry callbacks run once while a Ghost remains a match. Trait values changing each frame do not repeat them;
read current data in your own update loop. Removal, availability loss, or a metadata/component change that
breaks the filters produces a departure. Recovery or later matching membership can produce another entry.
Departure callbacks receive a **Key**, because the Ghost's Unity object may already have been destroyed.

Within one Realm, departure notifications run before that subscription's new entries during update notification.
An availability loss followed by recovery before the next update still produces departure then re-entry.
Outside source/finalization/notification callbacks, current matches can enter synchronously. Inside those phases,
initial entries are deferred; a subscription created during notification waits for a later update. Callback
exceptions are logged and isolated. Cross-Realm notification order is unspecified.

`OnAvailable(callback)` provides entries without departures when that is sufficient. It uses the same initial
notification and recovery rules and also returns an owned `IDisposable`. Neither observer is a per-frame data
callback, and editing a derived Query does not change an already active subscription.

Disposing a subscription cancels pending notifications and **does not synthesize departures**. Disposing a scoped
Realm also ends its subscriptions without synthetic departures. Clear your own state and bind a fresh query to
the new `RealmSetup.Realm` after a setup restart. The sample consumers show this lifecycle without manually
updating the Realm from their query code. Creating a scoped subscription after Realm disposal throws
`ObjectDisposedException`; null callbacks throw `ArgumentNullException`.

## Search multiple Realms

`Query.All()` searches every live Realm, including Realms created later. It does not create `Realm.Default`.
Use it only when the consumer intentionally spans scopes. Realm-scoped queries keep unrelated scenes out
of a local consumer's results.

Two Realms can contain identical Keys. Use `ObserveWithRealm` and retain the owner as part of identity:

```csharp
Dictionary<(Realm Owner, Key Identity), IGhost> tracked =
    new Dictionary<(Realm Owner, Key Identity), IGhost>();

IDisposable subscription = Query.All()
    .OfKind(new Kind("relative.bird"))
    .With<Spatial>()
    .ObserveWithRealm(
        (owner, ghost) => tracked[(owner, ghost.Key)] = ghost,
        (owner, key) => tracked.Remove((owner, key)));
```

An all-Realm observer reports departures when an owning Realm is disposed. Disposing the observer itself
still cancels callbacks without departures; dispose its handle and clear the dictionary. `Observe` without
the owner cannot distinguish identical Keys from different Realms.

`realm.Query(description)` rebinds all filters to that one Realm without modifying the original description
or its subscriptions. For example, `realm.Query(Query.All().OfKind(kind))` makes the selection local.

## LINQ, known identities, and unavailable roots

Queries implement `IEnumerable<IGhost>`, so `using System.Linq;` enables application-specific filters, sorting,
and materialization. For example:

```csharp
IGhost[] visibleParkedCars = parkedCars
    .Where(ghost => ghost.GetRequired<Spatial>().IsInRange)
    .ToArray();
```

This array captures matching membership now; its Ghost data remains live. LINQ predicates are ordinary
enumerable operations, rather than part of an Emas Query. Subscribe to the Query before applying LINQ;
`Where(...)` results do not expose `Observe` or `OnAvailable`. If a predicate depends on changing position,
reevaluate it in the consumer's update loop. Enumeration still requires Unity's main thread.

For an exact known identity, use `realm.TryGetGhost(new Key(anchorId, kind, entityId), out ghost)` and check
`ghost.IsAvailable` before consuming current data. This lookup can return unavailable prepared or retained
roots. To search entity-ID fragments, use LINQ explicitly; display-name filters do not match entity IDs:

```csharp
IGhost match = realm.Query()
    .InAnchor("relative-world")
    .OfKind(new Kind("relative.bird-foot"))
    .FirstOrDefault(ghost => ghost.Key.EntityId.Contains("left-foot"));
```

`Realm.Ghosts` is a different read-only membership snapshot: it includes prepared, pending, and retained
unavailable roots. It is useful for resolving a parent during initialization before availability. Check
`IsAvailable` when current data is required; use Queries for ongoing available-entity selection. A later
arrival cannot appear in a previously captured snapshot.

## API reference

| Member | Contract |
| --- | --- |
| `Realm.Query(partialName = null)` | Immutable description for one Realm; optional case-insensitive display-name substring. |
| `Realm.Query(description)` | Copies the description's filters into this Realm's scope; null throws `ArgumentNullException`. |
| `Query.All(partialName = null)` | All live and future Realms; does not create a default Realm. |
| `OfKind(kind)` | Replaces the Kind selector; invalid/default Kind throws `ArgumentException`. |
| `InAnchor(id)` | Replaces the case-sensitive Anchor selector; null clears it. |
| `With<T>()` | Adds an exactly-one root contract requirement; `T` is a class or interface. |
| `WithExactName(name)` | Replaces the case-insensitive exact display-name selector; null clears it, while any partial selector remains. |
| `WithVariant(variant)` | Replaces the case-sensitive Variant selector; `Variant.None` selects unspecified appearances. |
| `Count` | Evaluates the current count; disposed scoped Realms yield zero. |
| `FirstOrDefault()` | Evaluates one match or null; no ordering guarantee. |
| `Single()` | Evaluates exactly one match; zero or multiple matches throw `InvalidOperationException`. |
| `GetEnumerator()` / `foreach` | Copies membership when enumeration begins; data and object lifetimes remain live. |
| `OnAvailable(callback)` | Current/future membership entries, once per matching period; owns a disposable subscription. |
| `Observe(onEnter, onLeave)` | Paired `IGhost` entries and `Key` departures; owns a disposable subscription. |
| `ObserveWithRealm(onEnter, onLeave)` | Paired callbacks also receive the owning Realm; distinguishes duplicate Keys across scopes. |
