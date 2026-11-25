using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Exercises history-based acceleration through public source, strategy and projection APIs.</summary>
    public sealed class ConstantAccelerationTests
    {
        private sealed class Detector : PresenceDetector
        {
            internal Ghost Publish(string id)
            {
                return GetOrCreate<Ghost>(id, new Kind("acceleration"));
            }
        }

        private sealed class Probe : IExtrapolationStrategy
        {
            internal readonly ConstantAccelerationExtrapolation Model = new ConstantAccelerationExtrapolation();
            internal Action<ExtrapolationContext> Observed;

            /// <summary>Reads history during a complete prediction before delegating to the built-in model.</summary>
            /// <param name="request">The complete independently timed prediction request.</param>
            /// <returns>The acceleration model's full prediction.</returns>
            public Prediction Predict(PredictionRequest request)
            {
                Observed?.Invoke(request.PositionContext);
                return Model.Predict(request);
            }
        }

        private Realm _realm;
        private Detector _source;

        /// <summary>Creates an isolated Realm with a manually positioned Cartesian reference.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm { ReferenceFrame = new ReferenceFrame { Position = default(Double3) } };
            _source = new Detector();
            _realm.GetOrCreateAnchor("samples", _source);
        }

        /// <summary>Releases the population after each consumer scenario.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>Irregular noisy position observations recover endpoint velocity and acceleration, while shared models, explicit SDK motion and resets stay isolated.</summary>
        [Test]
        public void PositionHistory_EstimatesAccelerationAndRetainsSdkOverrides()
        {
            ConstantAccelerationExtrapolation model = new ConstantAccelerationExtrapolation();
            Double3 origin = new Double3(1e9, -2e9, 3e9);
            _realm.ReferenceFrame.Position = origin;
            Spatial first = Create("first", model, 0.2f, out Extrapolation firstMotion);
            Spatial second = Create("second", model, 0.2f, out _);
            Spatial control = Create("constant velocity", new ConstantVelocityExtrapolation(), 0.2f, out _);
            Double3 velocity = new Double3(3, 1, -2);
            Double3 acceleration = new Double3(4, -2, 1);
            Double3 secondAcceleration = new Double3(-6, 3, 2);
            double[] times = { 0, 0.02, 0.055, 0.095, 0.14, 0.2 };
            for (int index = 0; index < times.Length; index++)
            {
                double time = times[index];
                Timestamp stamp = Stamp(time);
                first.SetCartesianPosition(Trajectory(origin, velocity, acceleration, time), stamp);
                control.SetCartesianPosition(Trajectory(origin, velocity, acceleration, time), stamp);
                // Deterministic millimetre noise must not become an unbounded second difference.
                double noise = index % 2 == 0 ? 0.001d : -0.001d;
                second.SetCartesianPosition(Trajectory(origin, velocity, secondAcceleration, time) + new Double3(noise, 0, 0), stamp);
                if (index == 0)
                {
                    _realm.Update();
                }
            }

            AdvanceClock(1.2d);
            _realm.Update();
            AssertSource(first, Trajectory(origin, velocity, acceleration, 0.4d), 0.001d);
            AssertSource(second, Trajectory(origin, velocity, secondAcceleration, 0.4d), 0.03d);
            Assert.That(Double3.Distance(_realm.ReferenceFrame.ToSimulationPosition(control.transform.position),
                Trajectory(origin, velocity, acceleration, 0.4d)), Is.GreaterThan(0.05d));
            Assert.That(first.PositionTime, Is.EqualTo(Stamp(0.2d)));

            // SDK acceleration, including zero, wins over the fitted acceleration. Position-derived
            // velocity remains an endpoint estimate until an explicit SDK velocity is supplied.
            Double3 endpoint = Trajectory(origin, velocity, acceleration, 0.2d);
            Double3 endpointVelocity = velocity + acceleration * 0.2d;
            firstMotion.GetComponent<Spatial>().SetCartesianAcceleration(default(Double3), Stamp(0.2d));
            _realm.Update();
            AssertSource(first, endpoint + endpointVelocity * 0.2d, 0.001d);
            firstMotion.GetComponent<Spatial>().ClearAcceleration();
            firstMotion.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(8, 0, 0), Stamp(0.2d));
            firstMotion.GetComponent<Spatial>().SetCartesianVelocity(new Double3(17, 0, 0), Stamp(0.2d));
            _realm.Update();
            Double3 supplied = endpoint + new Double3(17d * 0.2d + 4d * 0.2d * 0.2d, 0, 0);
            AssertSource(first, supplied, 0.001d);
            _realm.Manifest(first.GetComponent<Ghost>());
            _realm.Update();
            AssertSource(first, supplied, 0.001d);

            // Clock restart discards the old fitting window without changing the shared strategy instance.
            _realm.ResetSpatialTime();
            firstMotion.GetComponent<Spatial>().ClearVelocity();
            firstMotion.GetComponent<Spatial>().ClearAcceleration();
            first.SetCartesianPosition(origin + new Double3(5, 0, 0), new Timestamp(1, 0));
            _source.Publish("restart clock").gameObject.AddComponent<Spatial>()
                .SetCartesianPosition(origin, new Timestamp(2, 0));
            _realm.Update();
            AssertSource(first, origin + new Double3(5, 0, 0), 0.001d);
        }

        /// <summary>SDK velocity history supplies missing acceleration, excludes future-only fit samples and yields to explicit acceleration without changing other strategies.</summary>
        [Test]
        public void VelocityHistory_RespectsObservationTimeAndExplicitAcceleration()
        {
            Probe probe = new Probe();
            Spatial spatial = Create("velocity samples", probe, 0.1f, out Extrapolation motion);
            spatial.SetCartesianPosition(default(Double3), Stamp(0.2d));
            foreach (double time in new[] { 0d, 0.03d, 0.07d, 0.11d, 0.16d })
            {
                motion.GetComponent<Spatial>().SetCartesianVelocity(new Double3(2d + 6d * time, 0, 0), Stamp(time));
            }
            AdvanceClock(1.2d);
            _realm.Update();
            Assert.That(spatial.transform.position.x, Is.EqualTo(3.2d * 0.1d + 3d * 0.01d).Within(0.0001d),
                "The strategy must advance the older SDK velocity to the position's observation time.");

            Double3 currentVelocity = default(Double3);
            int observations = 0;
            probe.Observed = context =>
            {
                observations++;
                Assert.That(context.TryGetVelocitySample(0, out Timestamp newest, out Double3 value), Is.True);
                Assert.That(newest, Is.EqualTo(Stamp(0.16d)));
                Assert.That(value.X, Is.EqualTo(2.96d).Within(1e-9));
                Assert.That(context.TryGetVelocitySample(1, out Timestamp previous, out _), Is.True);
                Assert.That(previous, Is.EqualTo(Stamp(0.11d)));
                Assert.That(context.TryGetVelocitySample(128, out _, out _), Is.False);
                Assert.Throws<ArgumentOutOfRangeException>(() => context.TryGetVelocitySample(-1, out _, out _));
                Assert.That(context.TryGetPositionSample(0, out Timestamp poseTime, out _), Is.True);
                Assert.That(poseTime, Is.EqualTo(context.ObservationTime));
                currentVelocity = context.Velocity.Value;
            };
            // Future SDK values may participate in the caller's interpolation, but they must
            // not leak into the strategy's acceleration fit at an older position time.
            motion.GetComponent<Spatial>().SetCartesianVelocity(new Double3(1000, 0, 0), Stamp(0.5d));
            _realm.Update();
            Assert.That(observations, Is.GreaterThan(0));
            Assert.That(spatial.transform.position.x, Is.EqualTo(currentVelocity.X * 0.1d + 0.03d).Within(0.0001d));
            motion.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(1000, 0, 0), Stamp(0.5d));
            _realm.Update();
            Assert.That(spatial.transform.position.x, Is.EqualTo(currentVelocity.X * 0.1d + 0.03d).Within(0.0001d),
                "Future-only acceleration must not override a fit at the older position time.");
            motion.GetComponent<Spatial>().ClearAcceleration();
            motion.GetComponent<Spatial>().SetCartesianAcceleration(default(Double3), Stamp(0.1d));
            _realm.Update();
            Assert.That(spatial.transform.position.x, Is.EqualTo(currentVelocity.X * 0.1d).Within(0.0001d));
            Assert.That(motion.GetComponent<Spatial>().Acceleration, Is.EqualTo(default(Double3)));
            probe.Observed = null;
            motion.GetComponent<Spatial>().ClearAcceleration();
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(spatial.transform.position.x, Is.EqualTo(currentVelocity.X * 0.1d).Within(0.0001d),
                "Reset must discard derivative fits while retaining SDK interpolation at the pose time.");
        }

        /// <summary>A strategy can evaluate a built-in model again after restarting estimation during the same callback; retained raw readers stay live and later samples recover prediction.</summary>
        [Test]
        public void StrategyComposition_SeesLiveHistoryResetWithinOneCall()
        {
            Probe probe = new Probe();
            Spatial spatial = Create("live history", probe, 0.1f, out Extrapolation motion);
            foreach (double time in new[] { 0d, 0.1d, 0.2d })
            {
                spatial.SetCartesianPosition(new Double3(time * time, 0, 0), Stamp(time));
            }

            bool checkedHistory = false;
            probe.Observed = context =>
            {
                if (checkedHistory)
                {
                    return;
                }

                Double3 before = probe.Model.Predict(new PredictionRequest(context, default(ExtrapolationContext), default(Double3), Quaternion.identity, 0.1d, 0)).Position;
                motion.Reset();
                Double3 after = probe.Model.Predict(new PredictionRequest(context, default(ExtrapolationContext), default(Double3), Quaternion.identity, 0.1d, 0)).Position;
                Assert.That(before.X, Is.EqualTo(0.05d).Within(0.00001d));
                Assert.That(after, Is.EqualTo(default(Double3)));
                checkedHistory = true;
            };
            AdvanceClock(1.2d);
            _realm.Update();
            Assert.That(checkedHistory, Is.True);
            Assert.That(spatial.transform.position.x, Is.EqualTo(0.04d).Within(0.00001d));
            spatial.SetCartesianPosition(new Double3(0.09d, 0, 0), Stamp(0.3d));
            spatial.SetCartesianPosition(new Double3(0.16d, 0, 0), Stamp(0.4d));
            _realm.Update();
            Assert.That(spatial.transform.position.x, Is.EqualTo(0.25d).Within(0.00001d));
        }

        /// <summary>Accelerating bank is recovered from orientation or body/world rate histories across axis conventions, including quaternion wrap and zero-rate/reset recovery.</summary>
        /// <param name="space">The source attitude representation used by the SDK mapping.</param>
        [TestCase(RotationSpace.Source)]
        [TestCase(RotationSpace.Geographic)]
        [TestCase(RotationSpace.EarthCentered)]
        public void AngularHistory_PredictsAcceleratingBankAcrossRotationSpaces(RotationSpace space)
        {
            GeoPosition origin = new GeoPosition(52, 13, 1000);
            ReferenceFrame frame = new ReferenceFrame { Space = ReferenceSpace.Geographic,
                GeographicPosition = origin, Coordinates = CoordinateSystem.NorthEastDown };
            _realm.ReferenceFrame = frame;
            ConstantAccelerationExtrapolation model = new ConstantAccelerationExtrapolation();
            Spatial observed = Create("observed bank", model, 0.15f, out _);
            Spatial supplied = Create("body rates", model, 0.15f, out Extrapolation body);
            Spatial world = Create("world rates", model, 0.15f, out Extrapolation earth);
            foreach (double time in new[] { 0d, 0.02d, 0.05d, 0.09d, 0.14d, 0.2d })
            {
                Timestamp stamp = Stamp(time);
                double angle = 350d + 40d * time + 60d * time * time;
                double bodyAngle = 20d + 50d * time - 35d * time * time;
                SetAttitude(observed, frame, origin, space, angle, stamp);
                SetAttitude(supplied, frame, origin, space, bodyAngle, stamp);
                SetAttitude(world, frame, origin, space, angle, stamp);
                body.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, (50d - 70d * time) * Math.PI / 180d), CoordinateSystem.Unity, stamp);
                earth.GetComponent<Spatial>().SetGeographicAngularVelocity(new Double3(0, -(40d + 120d * time) * Math.PI / 180d, 0), origin, stamp);
            }

            AdvanceClock(1.2d);
            _realm.Update();
            double future = 0.35d;
            Quaternion expected = Quaternion.AngleAxis((float)(350d + 40d * future + 60d * future * future), Vector3.forward);
            Quaternion suppliedExpected = Quaternion.AngleAxis((float)(20d + 50d * future - 35d * future * future), Vector3.forward);
            Assert.That(Quaternion.Angle(observed.transform.rotation, expected), Is.LessThan(0.1f));
            Assert.That(Quaternion.Angle(world.transform.rotation, expected), Is.LessThan(0.1f));
            Assert.That(Quaternion.Angle(supplied.transform.rotation, suppliedExpected), Is.LessThan(0.1f));

            // A supplied zero is authoritative even though earlier orientations show a changing turn.
            body.GetComponent<Spatial>().ClearAngularVelocity();
            body.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, Stamp(0.2d));
            observed.ResetPresentation();
            world.ResetPresentation();
            _realm.Update();
            Quaternion held = Quaternion.AngleAxis((float)(350d + 40d * 0.2d + 60d * 0.04d), Vector3.forward);
            Quaternion suppliedHeld = Quaternion.AngleAxis((float)(20d + 50d * 0.2d - 35d * 0.04d), Vector3.forward);
            Assert.That(Quaternion.Angle(observed.transform.rotation, held), Is.LessThan(0.1f));
            Assert.That(Quaternion.Angle(supplied.transform.rotation, suppliedHeld), Is.LessThan(0.1f));
            Quaternion worldHeldRate = Quaternion.AngleAxis((float)(350d + 40d * 0.2d + 60d * 0.04d + 64d * 0.15d), Vector3.forward);
            Assert.That(Quaternion.Angle(world.transform.rotation, worldHeldRate), Is.LessThan(0.1f),
                "SDK angular rate survives reset, but its earlier acceleration-fitting window does not.");
        }

        /// <summary>Changing angular axes integrate in body or world order instead of using a single averaged rotation vector.</summary>
        /// <param name="world">True to supply ECEF world rates; false to supply model body rates.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AngularAcceleration_IntegratesChangingAxesInTheSelectedFrame(bool world)
        {
            GeoPosition origin = new GeoPosition(52, 13, 1000);
            ReferenceFrame frame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
            _realm.ReferenceFrame = frame;
            Spatial spatial = Create("mixed axes", new ConstantAccelerationExtrapolation(), 0.4f, out Extrapolation motion);
            Quaternion observed = Quaternion.Euler(20, 30, 40);
            spatial.SetEarthCenteredPosition(origin.ToEarthCentered(), Stamp(0.2d));
            spatial.SetEarthCenteredRotation(observed, CoordinateSystem.NorthEastDown, Stamp(0.2d));
            Double3 endpointRate = new Double3(2, 1, 1.5);
            Double3 acceleration = new Double3(8, -4, 6);
            foreach (double time in new[] { 0d, 0.05d, 0.1d, 0.15d, 0.2d })
            {
                Double3 rate = endpointRate + acceleration * (time - 0.2d);
                if (world)
                {
                    motion.GetComponent<Spatial>().SetEarthCenteredAngularVelocity(rate, Stamp(time));
                }
                else
                {
                    motion.GetComponent<Spatial>().SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                }
            }
            AdvanceClock(1.2d);
            _realm.Update();

            // Independent quaternion RK4 reference, integrating q'=q*omega/2 or omega*q/2
            // for 0.4 s at 10 us steps; step halving agrees to 13 decimal places.
            Quaternion increment = world
                ? new Quaternion(0.5959129153f, 0.0307934963f, 0.5080088183f, 0.6211816147f)
                : new Quaternion(0.6545440819f, 0.0307934963f, 0.4298339296f, 0.6211816147f);
            ReferenceFrame bodyAxes = new ReferenceFrame { Position = default(Double3), Coordinates = CoordinateSystem.NorthEastDown };
            Quaternion raw = world ? increment * observed : observed * bodyAxes.ToSimulationRotation(increment);
            Quaternion expected = frame.ToUnityEarthCenteredRotation(raw, CoordinateSystem.NorthEastDown);
            Assert.That(Quaternion.Angle(spatial.transform.rotation, expected), Is.LessThan(0.05f));
        }

        private Spatial Create(string id, IExtrapolationStrategy strategy, float horizon, out Extrapolation motion)
        {
            Ghost ghost = _source.Publish(id);
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            motion = ghost.gameObject.AddComponent<Extrapolation>();
            motion.Strategy = strategy;
            motion.MaximumExtrapolation = horizon;
            return spatial;
        }

        private void AdvanceClock(double time)
        {
            _source.Publish("delivery clock").gameObject.AddComponent<Spatial>()
                .SetCartesianPosition(default(Double3), Stamp(time));
        }

        private void AssertSource(Spatial spatial, Double3 expected, double tolerance)
        {
            Assert.That(Double3.Distance(_realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position), expected), Is.LessThan(tolerance));
        }

        private static Double3 Trajectory(Double3 origin, Double3 velocity, Double3 acceleration, double time)
        {
            return origin + velocity * time + acceleration * (0.5d * time * time);
        }

        private static Timestamp Stamp(double time)
        {
            Timestamp relative = Timestamp.FromSeconds(time);
            return new Timestamp(1000000000000L + relative.Seconds, relative.Nanoseconds);
        }

        private static void SetAttitude(Spatial spatial, ReferenceFrame frame, GeoPosition origin, RotationSpace space, double angle, Timestamp time)
        {
            spatial.SetEarthCenteredPosition(origin.ToEarthCentered(), time);
            Quaternion local = Quaternion.AngleAxis((float)angle, Vector3.forward);
            if (space == RotationSpace.Source)
            {
                spatial.SetSourceRotation(frame.ToSimulationRotation(local, origin), time);
            }
            else if (space == RotationSpace.Geographic)
            {
                spatial.SetGeographicRotation(0, 0, -angle, time);
            }
            else
            {
                spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(local, CoordinateSystem.NorthEastDown), CoordinateSystem.NorthEastDown, time);
            }
        }
    }
}
