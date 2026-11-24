using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Checks horizontal turning and acceleration with an independent constant climb rate.</summary>
    public sealed class ClimbingTurnTests
    {
        private sealed class Detector : PresenceDetector
        {
            internal Ghost Publish(string id)
            {
                return GetOrCreate<Ghost>(id, new Kind("climbing turn"));
            }
        }

        private Realm _realm;
        private Detector _source;

        /// <summary>Creates an isolated Cartesian NED population with a large source origin.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm { ReferenceFrame = new ReferenceFrame
            {
                Position = new Double3(1e9, -2e9, 3e9), Coordinates = CoordinateSystem.NorthEastDown
            } };
            _source = new Detector();
            _realm.GetOrCreateAnchor("climbing turns", _source);
        }

        /// <summary>Releases all roots and their histories.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>Pose, SDK velocity and course-only feeds reproduce accelerating horizontal turns while climb stays linear in Cartesian and geographic space.</summary>
        /// <param name="geographic">Whether the integration uses ECEF positions and orientations rather than Cartesian NED.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void TurningClimb_UsesHistoryAndPreservesIndependentVerticalMotion(bool geographic)
        {
            if (geographic)
            {
                _realm.ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = new GeoPosition(48, 11, 1200) };
            }

            ConstantTurnClimbAccelerationExtrapolation strategy = new ConstantTurnClimbAccelerationExtrapolation { PacketSmoothingDuration = 0 };
            Spatial poses = Create("poses", strategy, out _);
            Spatial sdk = Create("SDK", strategy, out Extrapolation sdkMotion);
            Spatial positionsOnly = Create("positions only", strategy, out _);
            Spatial velocitiesOnly = Create("velocities only", strategy, out Extrapolation velocityMotion);
            Spatial courseAttitude = Create("one attitude", strategy, out _);
            foreach (double time in new[] { 0d, 0.025d, 0.06d, 0.105d, 0.16d, 0.2d, 0.24d })
            {
                Double3 position = Path(time);
                Quaternion attitude = Attitude(time);
                SetPosition(poses, position, time);
                SetPosition(sdk, position, time);
                SetPosition(positionsOnly, position, time);
                SetPosition(velocitiesOnly, position, time);
                SetPosition(courseAttitude, position, time);
                SetRotation(poses, attitude, time);
                SetRotation(sdk, attitude, time);
                Double3 velocity = SourceVector(Velocity(time));
                sdkMotion.SetCartesianVelocity(velocity, Stamp(time));
                velocityMotion.SetCartesianVelocity(velocity, Stamp(time));
                Vector3 rate = Quaternion.Inverse(attitude) * Vector3.up * 0.6f;
                sdkMotion.SetBodyAngularVelocity(new Double3(rate.x, rate.y, rate.z), CoordinateSystem.Unity, Stamp(time));
            }

            SetRotation(courseAttitude, Attitude(0.24d), 0.24d);
            AdvanceClock();
            _realm.Update();
            Double3 expected = Path(0.64d);
            foreach (Spatial spatial in new[] { poses, sdk, positionsOnly, velocitiesOnly, courseAttitude })
            {
                double error = Error(spatial, expected);
                TestContext.WriteLine(spatial.name + ": " + error + " m");
                Assert.That(error, Is.LessThan(0.01d));
            }

            Assert.That(Quaternion.Angle(poses.transform.rotation, Attitude(0.64d)), Is.LessThan(0.08f));
            Assert.That(Quaternion.Angle(courseAttitude.transform.rotation, Attitude(0.64d)), Is.LessThan(0.08f));
            _realm.Update();
            Assert.That(Error(positionsOnly, expected), Is.LessThan(0.01d));
            Assert.That(poses.PositionTime, Is.EqualTo(Stamp(0.24d)));

            // Inertial acceleration includes the normal turn component. Its vertical
            // component must not accelerate climb, either in prediction or time alignment.
            sdkMotion.SetCartesianAcceleration(SourceVector(Acceleration(0.24d)), Stamp(0.24d));
            _realm.Update();
            Assert.That(Error(sdk, expected), Is.LessThan(0.01d));
            SetPosition(sdk, Path(0.3d), 0.3d);
            SetRotation(sdk, Attitude(0.3d), 0.3d);
            sdkMotion.SetCartesianAcceleration(SourceVector(Acceleration(0.3d)), Stamp(0.3d));
            _realm.Update();
            Assert.That(Error(sdk, Path(0.7d)), Is.LessThan(0.01d));
        }

        /// <summary>Pure roll or pitch at a banked, pitched attitude changes orientation without a spurious horizontal turn or rotated climb velocity.</summary>
        /// <param name="roll">Whether the SDK supplies body-roll motion or world-level pitch motion.</param>
        [TestCase(true)]
        [TestCase(false)]
        public void BankAndPitch_PredictAttitudeWithoutBendingTheClimb(bool roll)
        {
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            _realm.ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
            Spatial spatial = Create("bank and pitch", new ConstantTurnClimbAccelerationExtrapolation { PacketSmoothingDuration = 0 }, out Extrapolation motion);
            Quaternion attitude = Quaternion.Euler(25, 20, -30);
            SetPosition(spatial, default(Double3), 0.24d);
            SetRotation(spatial, attitude, 0.24d);
            Double3 velocity = new Double3(30, 20, 100);
            motion.SetCartesianVelocity(SourceVector(velocity), Stamp(0.24d));
            Vector3 angular = roll ? attitude * Vector3.forward * 3f : Quaternion.Euler(0, 20, 0) * Vector3.right * 2f;
            motion.SetGeographicAngularVelocity(new Double3(-angular.x, -angular.z, -angular.y), origin, Stamp(0.24d));
            AdvanceClock();
            _realm.Update();
            Assert.That(Error(spatial, velocity * 0.4d), Is.LessThan(0.001d));
            Quaternion expected = Quaternion.AngleAxis(angular.magnitude * 0.4f * Mathf.Rad2Deg, angular.normalized) * attitude;
            Assert.That(Quaternion.Angle(spatial.transform.rotation, expected), Is.LessThan(0.08f));
        }

        /// <summary>Sparse input upgrades to accelerating flight, teleports clear fits, and supplied zero horizontal velocity, vertical rate and caps stay authoritative.</summary>
        [Test]
        public void SparseHistory_ResetsAndHonorsVerticalRateAndPredictionCaps()
        {
            ConstantTurnClimbAccelerationExtrapolation strategy = new ConstantTurnClimbAccelerationExtrapolation { PacketSmoothingDuration = 0 };
            Spatial spatial = Create("sparse", strategy, out Extrapolation motion);
            SetPosition(spatial, default(Double3), 0);
            motion.SetCartesianAcceleration(SourceVector(new Double3(100, 200, 0)), Stamp(0));
            AdvanceClock();
            _realm.Update();
            Assert.That(Error(spatial, default(Double3)), Is.LessThan(0.001d));
            motion.ClearAcceleration();
            SetPosition(spatial, new Double3(1, 2, 0), 0.1d);
            _realm.Update();
            Assert.That(Error(spatial, new Double3(5, 10, 0)), Is.LessThan(0.001d));
            SetPosition(spatial, new Double3(4, 4, 0), 0.2d);
            _realm.Update();
            Assert.That(Error(spatial, new Double3(36, 12, 0)), Is.LessThan(0.001d));

            SetPosition(spatial, new Double3(1000, 100, 0), 0.3d);
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1000, 100, 0)), Is.LessThan(0.001d));
            motion.SetCartesianVelocity(SourceVector(new Double3(0, 5, 0)), Stamp(0.3d));
            motion.SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, Stamp(0.3d));
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1000, 102, 0)), Is.LessThan(0.001d));
            motion.SetCartesianAcceleration(SourceVector(new Double3(100, 200, 0)), Stamp(0.3d));
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1008, 102, 0)), Is.LessThan(0.001d));

            // The latest SDK velocity is still zero horizontally at t=0.3. A newer
            // pose/acceleration at t=0.4 must align that velocity through a turning start.
            SetPosition(spatial, new Double3(1000, 100, 0), 0.4d);
            SetRotation(spatial, Quaternion.AngleAxis(0.06f * Mathf.Rad2Deg, Vector3.up), 0.4d);
            motion.ClearAngularVelocity();
            motion.SetBodyAngularVelocity(new Double3(0, 0.6d, 0), CoordinateSystem.Unity, Stamp(0.4d));
            motion.SetCartesianAcceleration(SourceVector(new Double3(8d * Math.Sin(0.06d) + 0.48d * Math.Cos(0.06d), 200,
                8d * Math.Cos(0.06d) - 0.48d * Math.Sin(0.06d))), Stamp(0.4d));
            _realm.Update();
            // Independent midpoint integral, 80,000 steps over t=0.1..0.5 with
            // speed=8*t and course=0.6*t. Step halving agrees within 2e-11 metres.
            Assert.That(Error(spatial, new Double3(1000.196606352d, 102, 0.937648240d)), Is.LessThan(0.001d));
            motion.MaximumExtrapolation = 0;
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1000, 100, 0)), Is.LessThan(0.001d));
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.Gravity = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.PacketSmoothingDuration = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.ExtrapolateDisplacement(default(ExtrapolationContext), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.ExtrapolateRotation(default(ExtrapolationContext), double.NaN));
        }

        private Spatial Create(string id, IExtrapolationStrategy strategy, out Extrapolation motion)
        {
            Ghost ghost = _source.Publish(id);
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            motion = ghost.gameObject.AddComponent<Extrapolation>();
            motion.Strategy = strategy;
            motion.MaximumExtrapolation = 0.4f;
            return spatial;
        }

        private void AdvanceClock()
        {
            _source.Publish("clock").gameObject.AddComponent<Spatial>().SetCartesianPosition(_realm.ReferenceFrame.Position, Stamp(2));
        }

        private void SetPosition(Spatial spatial, Double3 position, double time)
        {
            spatial.SetCartesianPosition(_realm.ReferenceFrame.ToSimulationPosition(Vector(position)), Stamp(time));
        }

        private void SetRotation(Spatial spatial, Quaternion rotation, double time)
        {
            ReferenceFrame frame = _realm.ReferenceFrame;
            if (frame.Space == ReferenceSpace.Geographic)
            {
                spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(rotation), sampleTime: Stamp(time));
            }
            else
            {
                spatial.SetSourceRotation(frame.ToSimulationRotation(rotation), Stamp(time));
            }
        }

        private Double3 SourceVector(Double3 vector)
        {
            return _realm.ReferenceFrame.ToSimulationPosition(Vector(vector)) - _realm.ReferenceFrame.Position;
        }

        private static double Error(Spatial spatial, Double3 expected)
        {
            Vector3 actual = spatial.transform.position;
            return Double3.Distance(new Double3(actual.x, actual.y, actual.z), expected);
        }

        private static Vector3 Vector(Double3 vector)
        {
            return new Vector3((float)vector.X, (float)vector.Y, (float)vector.Z);
        }

        private static Double3 Path(double seconds)
        {
            // Independent midpoint integration of speed and course, with a separate
            // analytic height term. No production turn integrals enter the oracle.
            const int steps = 4000;
            Double3 result = default(Double3);
            double step = seconds / steps;
            for (int index = 0; index < steps; index++)
            {
                result += Velocity((index + 0.5d) * step) * step;
            }

            return new Double3(result.X, 12d * seconds, result.Z);
        }

        private static Double3 Velocity(double seconds)
        {
            double course = 0.35d + 0.6d * seconds;
            double speed = 120d + 8d * seconds;
            return new Double3(speed * Math.Sin(course), 12, speed * Math.Cos(course));
        }

        private static Double3 Acceleration(double seconds)
        {
            double course = 0.35d + 0.6d * seconds;
            double speed = 120d + 8d * seconds;
            return new Double3(8d * Math.Sin(course) + 0.6d * speed * Math.Cos(course), 100,
                8d * Math.Cos(course) - 0.6d * speed * Math.Sin(course));
        }

        private static Quaternion Attitude(double seconds)
        {
            return Quaternion.AngleAxis((float)((0.35d + 0.6d * seconds) * Mathf.Rad2Deg), Vector3.up) * Quaternion.Euler(20, 0, -35);
        }

        private static Timestamp Stamp(double seconds)
        {
            Timestamp offset = Timestamp.FromSeconds(seconds);
            return new Timestamp(1000000000000L + offset.Seconds, offset.Nanoseconds);
        }
    }
}
