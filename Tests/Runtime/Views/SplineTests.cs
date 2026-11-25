using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Exercises spline prediction through timestamped input and public Realm projection.</summary>
    public sealed class SplineTests
    {
        private sealed class Detector : PresenceDetector
        {
            internal Ghost Publish(string id)
            {
                return GetOrCreate<Ghost>(id, new Kind("spline"));
            }
        }

        private Realm _realm;
        private Detector _source;

        /// <summary>Creates a population with a fixed reference and a large source origin.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm { ReferenceFrame = new ReferenceFrame
            {
                Position = new Double3(1e9, -2e9, 3e9), Coordinates = CoordinateSystem.NorthEastDown
            } };
            _source = new Detector();
            _realm.GetOrCreateAnchor("splines", _source);
        }

        /// <summary>Releases all roots and their retained observations.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>Irregular pose and SDK velocity histories extend cubic motion, remain isolated on shared instances and exclude future-only derivatives.</summary>
        [Test]
        public void CubicMotion_UsesTimestampedHistoryAndHonorsSdkDerivatives()
        {
            SplineExtrapolation strategy = new SplineExtrapolation();
            Spatial poses = Create("poses", strategy, out Extrapolation poseMotion);
            Spatial rates = Create("velocities", strategy, out Extrapolation rateMotion);
            Spatial future = Create("future derivative", strategy, out Extrapolation futureMotion);
            Double3 origin = _realm.ReferenceFrame.Position;
            Double3 velocity = new Double3(3, -2, 1);
            Double3 acceleration = new Double3(4, 2, -1);
            Double3 jerk = new Double3(-8, 6, 3);
            foreach (double time in new[] { 0d, 0.025d, 0.06d, 0.095d, 0.135d, 0.17d, 0.215d, 0.25d })
            {
                Double3 position = Path(origin, velocity, acceleration, jerk, time);
                poses.SetCartesianPosition(position, Stamp(time));
                future.SetCartesianPosition(position, Stamp(time));
                // SDK velocity is authoritative even when the observed pose follows a
                // different path. Its own quadratic history defines the future curvature.
                rates.SetCartesianPosition(origin, Stamp(time));
                rateMotion.GetComponent<Spatial>().SetCartesianVelocity(velocity + acceleration * time + jerk * (0.5d * time * time), Stamp(time));
            }

            futureMotion.GetComponent<Spatial>().SetCartesianVelocity(new Double3(10000, 0, 0), Stamp(0.8d));
            AdvanceClock();
            _realm.Update();
            AssertSource(poses, Path(origin, velocity, acceleration, jerk, 0.45d), 0.001d);
            AssertSource(future, Path(origin, velocity, acceleration, jerk, 0.45d), 0.001d);
            Double3 current = Path(origin, velocity, acceleration, jerk, 0.25d);
            Double3 futureDelta = Path(origin, velocity, acceleration, jerk, 0.45d) - current;
            AssertSource(rates, origin + futureDelta, 0.001d);

            // Explicit acceleration constrains the current curvature without discarding
            // the measured rate change. Re-evaluation must leave raw observations intact.
            poseMotion.GetComponent<Spatial>().SetCartesianAcceleration(default(Double3), Stamp(0.25d));
            _realm.Update();
            Double3 endpointVelocity = velocity + acceleration * 0.25d + jerk * (0.5d * 0.25d * 0.25d);
            Double3 constrained = current + endpointVelocity * 0.2d + jerk * (0.2d * 0.2d * 0.2d / 6d);
            AssertSource(poses, constrained, 0.001d);
            _realm.Update();
            AssertSource(poses, constrained, 0.001d);
            Assert.That(poses.Position, Is.EqualTo(current));
            Assert.That(poses.PositionTime, Is.EqualTo(Stamp(0.25d)));

            rates.SetCartesianPosition(origin, Stamp(0.3d));
            _realm.Update();
            Double3 aligned = Path(origin, velocity, acceleration, jerk, 0.5d)
                - Path(origin, velocity, acceleration, jerk, 0.3d);
            AssertSource(rates, origin + aligned, 0.001d);

            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.Predict(new PredictionRequest(default(ExtrapolationContext), default(ExtrapolationContext), default(Double3), Quaternion.identity, -0.1d, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.Predict(new PredictionRequest(default(ExtrapolationContext), default(ExtrapolationContext), default(Double3), Quaternion.identity, 0, double.NaN)));
            Assert.That(strategy.Predict(new PredictionRequest(default(ExtrapolationContext), default(ExtrapolationContext), default(Double3), Quaternion.identity, 0, 0)).Position, Is.EqualTo(default(Double3)));
            Assert.That(strategy.Predict(new PredictionRequest(default(ExtrapolationContext), default(ExtrapolationContext), default(Double3), Quaternion.identity, 0, 0)).Rotation, Is.EqualTo(Quaternion.identity));
        }

        /// <summary>Sparse streams upgrade from held to linear and quadratic motion; teleports and untimed input discard old curve estimates.</summary>
        [Test]
        public void SparseHistory_FallsBackAndRestartsAfterDiscontinuities()
        {
            Spatial spatial = Create("sparse", new SplineExtrapolation(), out Extrapolation motion);
            Double3 origin = _realm.ReferenceFrame.Position;
            spatial.SetCartesianPosition(origin, Stamp(0));
            motion.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(100, 0, 0), Stamp(0));
            AdvanceClock();
            _realm.Update();
            AssertSource(spatial, origin, 0.001d);
            motion.GetComponent<Spatial>().ClearAcceleration();

            spatial.SetCartesianPosition(origin + new Double3(1, 0, 0), Stamp(0.1d));
            _realm.Update();
            AssertSource(spatial, origin + new Double3(3, 0, 0), 0.001d);
            spatial.SetCartesianPosition(origin + new Double3(4, 0, 0), Stamp(0.2d));
            _realm.Update();
            AssertSource(spatial, origin + new Double3(16, 0, 0), 0.001d);

            spatial.SetCartesianPosition(origin + new Double3(1000, 0, 0), Stamp(0.3d));
            spatial.ResetPresentation();
            _realm.Update();
            AssertSource(spatial, origin + new Double3(1000, 0, 0), 0.001d);
            spatial.SetCartesianPosition(origin + new Double3(1001, 0, 0), Stamp(0.4d));
            _realm.Update();
            AssertSource(spatial, origin + new Double3(1003, 0, 0), 0.001d);

            spatial.SetCartesianPosition(origin);
            motion.PredictionTime = 0.2f;
            motion.GetComponent<Spatial>().SetCartesianVelocity(new Double3(2, 0, 0));
            motion.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(3, 0, 0));
            spatial.ResetPresentation();
            _realm.Update();
            AssertSource(spatial, origin + new Double3(0.46d, 0, 0), 0.001d);
        }

        /// <summary>Quaternion-log curves predict accelerating bank through angle wrapping in source, geographic and ECEF representations.</summary>
        /// <param name="space">The orientation representation supplied by the integration.</param>
        [TestCase(RotationSpace.Source)]
        [TestCase(RotationSpace.Geographic)]
        [TestCase(RotationSpace.EarthCentered)]
        public void RotationHistory_ExtendsCurvesAcrossRotationSpaces(RotationSpace space)
        {
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic, GeographicPosition = origin, Coordinates = CoordinateSystem.NorthEastDown
            };
            _realm.ReferenceFrame = frame;
            Spatial spatial = Create("bank", new SplineExtrapolation(), out Extrapolation motion);
            foreach (double time in new[] { 0d, 0.025d, 0.06d, 0.095d, 0.135d, 0.17d, 0.215d, 0.25d })
            {
                spatial.SetEarthCenteredPosition(origin.ToEarthCentered(), Stamp(time));
                double angle = Bank(time) * Mathf.Rad2Deg;
                Quaternion local = Quaternion.AngleAxis((float)angle, Vector3.forward);
                if (space == RotationSpace.Source)
                {
                    spatial.SetSourceRotation(frame.ToSimulationRotation(local, origin), Stamp(time));
                }
                else if (space == RotationSpace.Geographic)
                {
                    spatial.SetGeographicRotation(0, 0, -angle, Stamp(time));
                }
                else
                {
                    spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(local, CoordinateSystem.NorthEastDown),
                        CoordinateSystem.NorthEastDown, Stamp(time));
                }
            }

            AdvanceClock();
            _realm.Update();
            Assert.That(Quaternion.Angle(spatial.transform.rotation,
                Quaternion.AngleAxis((float)(Bank(0.45d) * Mathf.Rad2Deg), Vector3.forward)), Is.LessThan(0.08f));

            motion.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, Stamp(0.25d));
            _realm.Update();
            Assert.That(Quaternion.Angle(spatial.transform.rotation,
                Quaternion.AngleAxis((float)(Bank(0.25d) * Mathf.Rad2Deg), Vector3.forward)), Is.LessThan(0.05f));

            motion.GetComponent<Spatial>().ClearAngularVelocity();
            foreach (double time in new[] { 0d, 0.05d, 0.1d, 0.15d, 0.2d, 0.25d })
            {
                motion.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, 1.2d + 4d * time + 9d * time * time),
                    CoordinateSystem.Unity, Stamp(time));
            }

            _realm.Update();
            Assert.That(Quaternion.Angle(spatial.transform.rotation,
                Quaternion.AngleAxis((float)(Bank(0.45d) * Mathf.Rad2Deg), Vector3.forward)), Is.LessThan(0.05f));

            // Advance attitude independently of the SDK rate channel. Prediction must
            // start with the rate reconstructed at the newer attitude's timestamp.
            Quaternion later = Quaternion.AngleAxis((float)(Bank(0.3d) * Mathf.Rad2Deg), Vector3.forward);
            if (space == RotationSpace.Source)
            {
                spatial.SetSourceRotation(frame.ToSimulationRotation(later, origin), Stamp(0.3d));
            }
            else if (space == RotationSpace.Geographic)
            {
                spatial.SetGeographicRotation(0, 0, -Bank(0.3d) * Mathf.Rad2Deg, Stamp(0.3d));
            }
            else
            {
                spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(later, CoordinateSystem.NorthEastDown),
                    CoordinateSystem.NorthEastDown, Stamp(0.3d));
            }

            _realm.Update();
            Assert.That(Quaternion.Angle(spatial.transform.rotation,
                Quaternion.AngleAxis((float)(Bank(0.5d) * Mathf.Rad2Deg), Vector3.forward)), Is.LessThan(0.05f));
        }

        /// <summary>Curved SDK angular rates retain noncommuting body/world composition instead of exponentiating their sum.</summary>
        /// <param name="world">Whether the SDK rates use world ECEF axes rather than model body axes.</param>
        /// <param name="localWorld">Whether world rates drive a tangent-space source orientation instead of an ECEF orientation.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AngularRateHistory_IntegratesChangingAxes(bool world, bool localWorld)
        {
            GeoPosition origin = new GeoPosition(52, 13, 1000);
            ReferenceFrame frame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
            _realm.ReferenceFrame = frame;
            Spatial spatial = Create("changing axes", new SplineExtrapolation(), out Extrapolation motion);
            motion.MaximumExtrapolation = 0.4f;
            Quaternion observed = Quaternion.Euler(20, 30, 40);
            spatial.SetEarthCenteredPosition(origin.ToEarthCentered(), Stamp(0.2d));
            if (localWorld)
            {
                Quaternion local = frame.ToUnityEarthCenteredRotation(observed, CoordinateSystem.NorthEastDown);
                spatial.SetSourceRotation(frame.ToSimulationRotation(local, origin), Stamp(0.2d));
            }
            else
            {
                spatial.SetEarthCenteredRotation(observed, CoordinateSystem.NorthEastDown, Stamp(0.2d));
            }
            foreach (double time in new[] { 0d, 0.05d, 0.1d, 0.15d, 0.2d })
            {
                Double3 rate = new Double3(2, 1, 1.5) + new Double3(8, -4, 6) * (time - 0.2d);
                if (world)
                {
                    motion.GetComponent<Spatial>().SetEarthCenteredAngularVelocity(rate, Stamp(time));
                }
                else
                {
                    motion.GetComponent<Spatial>().SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                }
            }

            AdvanceClock();
            _realm.Update();
            // Independent quaternion RK4 reference at 10 us steps; step halving agrees
            // to 13 decimal places. These values also protect the acceleration model.
            Quaternion increment = world
                ? new Quaternion(0.5959129153f, 0.0307934963f, 0.5080088183f, 0.6211816147f)
                : new Quaternion(0.6545440819f, 0.0307934963f, 0.4298339296f, 0.6211816147f);
            ReferenceFrame axes = new ReferenceFrame { Position = default(Double3), Coordinates = CoordinateSystem.NorthEastDown };
            Quaternion raw = world ? increment * observed : observed * axes.ToSimulationRotation(increment);
            Quaternion expected = frame.ToUnityEarthCenteredRotation(raw, CoordinateSystem.NorthEastDown);
            Assert.That(Quaternion.Angle(spatial.transform.rotation, expected), Is.LessThan(0.05f));
        }

        private Spatial Create(string id, SplineExtrapolation strategy, out Extrapolation motion)
        {
            Ghost ghost = _source.Publish(id);
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            motion = ghost.gameObject.AddComponent<Extrapolation>();
            motion.Strategy = strategy;
            motion.MaximumExtrapolation = 0.2f;
            return spatial;
        }

        private void AdvanceClock()
        {
            _source.Publish("clock").gameObject.AddComponent<Spatial>()
                .SetCartesianPosition(_realm.ReferenceFrame.Position, Stamp(2d));
        }

        private void AssertSource(Spatial spatial, Double3 expected, double tolerance)
        {
            Assert.That(Double3.Distance(_realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position), expected), Is.LessThan(tolerance));
        }

        private static Double3 Path(Double3 origin, Double3 velocity, Double3 acceleration, Double3 jerk, double time)
        {
            return origin + velocity * time + acceleration * (0.5d * time * time) + jerk * (time * time * time / 6d);
        }

        private static double Bank(double seconds)
        {
            return 6d + 1.2d * seconds + 2d * seconds * seconds + 3d * seconds * seconds * seconds;
        }

        private static Timestamp Stamp(double seconds)
        {
            Timestamp offset = Timestamp.FromSeconds(seconds);
            return new Timestamp(1000000000000L + offset.Seconds, offset.Nanoseconds);
        }
    }
}
