# Emas sample

This sample is an external assembly that demonstrates the complete package workflow:

- application-owned Kind values and autocomplete-friendly Variant values;
- CarGhost implementing position and articulation parts directly on the root;
- a reusable ghost-root position behavior plus view-only vehicle and articulation behaviors;
- two simulated network-library shapes feeding the same CarGhost;
- automatic discovery of ten moving cars and three moving aircraft on one origin;
- source replacement after four seconds without replacing the car ghosts;
- kind and interface queries with optional views;
- small-car, large-car, truck and unknown-vehicle fallback view templates;
- a cockpit marker driven by moving screen ID and normalized top-left XY data.

## Run it

1. Add this package from disk in Package Manager, then import the Emas sample.
2. Open Assets/Samples/Emas/0.1.0/Example/Scenes/Example.unity (the imported copy of Samples~/Example/Scenes/Example.unity).
3. Press Play.

Do not double-click the scene while it is still inside the package Samples~ folder; import the sample into a project first. The scene supplies a camera and light. The bootstrap creates the ground, road, runway, cockpit screen, primitive vehicle templates and view mappings at runtime, so no model assets are required.

The ghost roots are created and updated on every Emas tick even when no view is requested. Cars are placed around an oval road and move through their source-independent position data. Aircraft use a separate coordinator and fly a looping path above the road. The yellow cockpit marker receives a new normalized coordinate every frame and is converted to the current screen transform. The status panel reports both populations and changes from SDK One to SDK Two (replaced) after the first car coordinator is replaced.

## How the network libraries are mocked

The mock is deliberately split at the same boundary as a real SDK integration:

- SdkOneVehicleFeed returns SdkOneVehicleProxy, which exposes separate X, Y and Z values plus an integer type code.
- SdkTwoVehicleFeed returns SdkTwoVehicleProxy, which exposes a Vector3, a different integer model code and a differently named steering value.
- SdkOneCarCoordinator and SdkTwoCarCoordinator convert those source-specific shapes into the same CarGhost calls: SetPosition, SetArticulation and the typed CarVariants mapping.
- SimulatedAircraftFeed and SimulatedCockpitFeed model two additional polling sources.

The feeds are deterministic functions of elapsed time, so the sample has no sockets, threads or external package dependencies. Replacing the car coordinator after four seconds demonstrates that a new source can continue updating the existing ghost identities and views. The feed constructors are injectable, which lets an application test a coordinator with a controlled source.

The sample source is deliberately outside the Emas runtime. Bootstrap shows registration, automatic discovery, typed variant mapping, queries, subscriptions, source replacement and cockpit screen-coordinate conversion in one small example.
