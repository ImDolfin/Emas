# Aircraft formation

Open `AircraftFormation.unity` and press Play. The camera sits at the reference aircraft's origin, looking forward. Two independently tracked aircraft fly ahead: orange on the left, cyan on the right. All three follow the same repeatable, multi-frequency heading, pitch and bank maneuvers at **343 m/s (1,234.8 km/h)**. This sample uses that fixed speed as its Mach 1 reference; it does not model atmospheric sound speed or aircraft dynamics.

The camera remains at Unity `(0, 0, 0)` with identity rotation. **Tracking / Realm Setup** follows `aircraft-formation / flight.aircraft / reference`, cancelling its full presented attitude. The resulting wingman motion, green level-horizon line and fixed central reticle let you evaluate the camera's angular motion in the aircraft frame. The overlay reports presented angular speed in degrees per real second, yaw/pitch/bank, each aircraft's capture age, and wingman azimuth/elevation in degrees. Capture age is SDK elapsed time minus the latest delivered observation time; it is distinct from buffered presentation latency. Angular speed is a frame-to-frame magnitude and includes measurement jitter.

The wingmen have fixed simulation-space offsets of `(-70, 20, 350)` and `(70, -20, 350)` metres. Their identical velocities preserve equal speed and separation during turns. Their offsets do not rotate with the reference body: a rigid rotating formation would require different speeds. The bounded maneuvers keep both aircraft ahead while their screen bearings change. The feed starts at large double-precision Cartesian coordinates to exercise reference subtraction before conversion to Unity floats.

## Compare timing and jitter

Select **Tracking / Flight Source** to change these settings while playing:

| Setting | Default |
| --- | --- |
| Updates Per Second | Reference 60 Hz, left 47 Hz, right 53 Hz |
| Initial capture phase | Reference 0 ms, left 7 ms, right 14 ms |
| Packet Delay | 80 ms, plus 15 ms for the left and 30 ms for the right |
| Delay Jitter | Independent +/-50 ms per packet |
| Position Jitter | Up to 2 m of three-dimensional error per observation |
| Attitude Jitter | Up to +/-0.35 degrees on each local attitude axis |

Each stream has its own seeded noise sequence and retains its capture timestamp on one shared SDK clock. Delivery can reorder packets; older packets are discarded per aircraft. Supplied velocity and true body angular rates describe noise-free motion at capture time, avoiding derivatives of noisy positions or Euler angles. Slow frames skip captures older than the latest quarter-second, and large manual jumps bound numerical integration work; these jumps are unsuitable for precise flight-path comparisons.

The **Use clean, simultaneous observations** button disables noise, delay and capture-time differences together. Re-enable impairments for the same ongoing trajectory with independent stream timing. Changing this mode clears pending packets and preserves travel. To isolate transport effects, leave impairments enabled and set both measurement jitters to zero in the Inspector. Compare those runs with the noisy default, then use **Shared buffer** to switch between 150 ms and immediate playback. The buffer aligns independently sampled entities on one presentation timeline; it adds latency and cannot remove measurement noise or guarantee perfect alignment during missing data.

The authored `AircraftRoot.prefab` enables a 50 ms prediction horizon, 80 ms position smoothing half-life and 40 ms rotation smoothing half-life. Edit these traits on live Ghosts to compare their effects. The reference cockpit view, wingman models, materials, tracking setup, camera and light are saved assets; Play Mode creates only tracked instances. Disable and re-enable Tracking to restart the deterministic flight and clocks.

For deterministic stepping, set `FlightSource.AutomaticAdvance = false` and call `Advance(seconds)` followed by `realm.Update()` on Unity's main thread. Automatic mode uses real elapsed time independently of Unity time scale. Switching clock modes rebases the sample-owned Realm clock on the next detector update. The imported sample tests cover equal-speed maneuvering with irregular steps, exact clean reference projection, and bounded noisy angular presentation with independent timestamps and varied frame waits.
