# Relative world

Import **Relative world** from the Emas package, open `RelativeWorld.unity`, and press Play. The scene has one **Relative World** component; it creates an isolated realm, camera, ground, and simple car views.

The green ego car stays at the Unity origin while its simulation coordinates move around one billion metres. Orange traffic was published once and moves in Unity when the reference changes. A distant traffic view appears when it enters the 45-metre presentation range.

| File | Role |
| --- | --- |
| `RelativeWorld.cs` | Owns the realm, configures the ghost-following `ReferenceFrame`, builds views, and calls `realm.Update()`. |
| `RelativeCar.cs` | Requires `Spatial` on each car Ghost root. |
| `RelativeCarDetector.cs` | Publishes `Double3` positions and rotations independently without converting global positions to floats. |

Disable the **Relative World** object to release its realm and generated scene objects. The sample has its own assembly and can be imported without **Emas sample**. For coordinate mapping, reference loss, and range behavior, see the [relative-world guide](../../Documentation~/Spatial.md).