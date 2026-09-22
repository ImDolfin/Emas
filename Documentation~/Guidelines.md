# Integration and contribution guidelines

| Concern | Rule |
| --- | --- |
| Application contracts | Define small read-only interfaces in your application. Implement them on `Ghost` or other root components; keep setters on concrete ghosts for source mapping. Consumers use `IGhost.TryGet<T>`. One root provider per contract. |
| Coordinates | Define units and axes in the application contract. Sample positions are anchor-local. Convert SDK units/axes first; for a Unity world position, use `anchor.Transform.InverseTransformPoint(worldPosition)` before storing it. |
| Ownership | Emas owns anchors, ghost roots, views and availability. Applications own SDK clients and prefab assets. Source cleanup unsubscribes but does not dispose a shared client. Dispose query subscriptions and clear retained membership when their consumer stops; disposal does not invoke departure callbacks. |
| Threading and payloads | Call Emas only on Unity's main thread, including publish/remove and `Dispatch`. The application owns SDK threading and delivers events on that thread; copy mutable/reused SDK payloads before publishing. Order initial data with live events and undo partial subscriptions if startup throws. Custom sources must invalidate old callbacks before reattachment. |
| Failure and recovery | Read `LastErrorContext` and `LastError`, or **Window > Emas**. Set `source.Name` to distinguish feeds. Fix the cause, then use `anchor.RestartSource(source)` to retry the same source or `ReplaceSource` for a different implementation; both preserve compatible identities. Remove/add restarts tracking but deletes the removed source's ghosts. There is no automatic retry; unavailable data may be stale. |
| Extension choice | Start with fluent polling or callback builders. Subclass `PresenceSource` for custom lifecycle requirements; do not expose SDK types to view consumers. Keep callbacks short. |
| Contributions | Use C# 8, Allman braces, XML docs and Unity metadata. Keep runtime independent of editor/sample/SDK assemblies. Cover public behavior with Unity tests, update the changelog, and synchronize shipped samples with `Tests/Unity~/Assets/Samples`. Change version or assembly names only as part of an explicit release decision. |

[API contracts](API.md) | [Architecture](Architecture.md) | [Test workflow and supported matrix](Validation.md)
