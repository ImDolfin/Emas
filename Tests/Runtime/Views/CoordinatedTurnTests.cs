using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Checks rotating flight predictions against independent sampled trajectories.</summary>
    public sealed class CoordinatedTurnTests
    {
        private sealed class Detector : PresenceDetector
        {
            internal Ghost Publish(string id)
            {
                return (Ghost)Detect(id, new Kind("turn")).Root;
            }
        }

        private Realm _realm;
        private Detector _source;

        /// <summary>Creates an isolated Cartesian population without playback delay.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm { ReferenceFrame = new ReferenceFrame
            {
                Position = new Double3(1e9, -2e9, 3e9), Coordinates = CoordinateSystem.NorthEastDown
            } };
            _source = new Detector();
            _realm.GetOrCreateAnchor("turns", _source);
        }

        /// <summary>Releases all roots and their motion histories.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>Pose-only turns recover the current tangent rather than predicting from a lagging chord; SDK velocity remains authoritative.</summary>
        /// <param name="suppliedRate">Whether the angular rate comes from the SDK or timestamped attitudes.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void SteadyTurn_PositionHistoryRecoversEndpointVelocity(bool suppliedRate)
        {
            CoordinatedTurnExtrapolation model = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0 };
            Spatial estimated = Create("pose-derived", model, out Extrapolation estimatedMotion);
            Spatial supplied = Create("SDK velocity", model, out Extrapolation suppliedMotion);
            Spatial straight = Create("SDK zero", model, out Extrapolation straightMotion);
            Quaternion initial = Quaternion.Euler(15, 20, -30);
            Double3 rate = new Double3(0.2d, 1.2d, -0.3d);
            Double3 bodyVelocity = new Double3(4, 2, 150);
            foreach (double time in new[] { 0d, 0.025d, 0.07d, 0.115d, 0.18d, 0.24d })
            {
                Quaternion attitude = Attitude(initial, rate, time);
                Double3 position = Path(initial, rate, bodyVelocity, default(Double3), time);
                SetPose(estimated, position, attitude, time);
                SetPose(supplied, position, attitude, time);
                SetPose(straight, position, attitude, time);
                suppliedMotion.SetCartesianVelocity(ToSourceVector(Rotate(attitude, bodyVelocity)), Stamp(time));
                straightMotion.SetCartesianVelocity(default(Double3), Stamp(time));
                if (suppliedRate)
                {
                    estimatedMotion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                    suppliedMotion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                    straightMotion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                }

                if (time == 0)
                {
                    _realm.Update();
                }
            }

            AdvanceClock();
            _realm.Update();
            Double3 expected = Path(initial, rate, bodyVelocity, default(Double3), 0.64d);
            double estimateError = Error(estimated, expected);
            double suppliedError = Error(supplied, expected);
            TestContext.WriteLine("Turn error: pose-derived=" + estimateError + " m; supplied=" + suppliedError + " m");
            Assert.That(suppliedError, Is.LessThan(0.002d));
            Assert.That(estimateError, Is.LessThan(0.004d));
            Assert.That(Quaternion.Angle(estimated.transform.rotation, Attitude(initial, rate, 0.64d)), Is.LessThan(0.08f));
            Assert.That(Error(straight, Path(initial, rate, bodyVelocity, default(Double3), 0.24d)), Is.LessThan(0.002d));

            // Repeated evaluation must not feed predictions back into the raw samples.
            _realm.Update();
            Assert.That(Error(estimated, expected), Is.LessThan(0.004d));
            Assert.That(estimated.PositionTime, Is.EqualTo(Stamp(0.24d)));
        }

        /// <summary>Cold batches and reset windows reconstruct velocity with supplied inertial acceleration, including explicit zero.</summary>
        [Test]
        public void PositionHistory_UsesInertialAccelerationAndRestartsAfterTeleport()
        {
            _realm.ReferenceFrame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic, GeographicPosition = new GeoPosition(48, 11, 1200)
            };
            CoordinatedTurnExtrapolation model = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0 };
            Spatial spatial = Create("accelerating turn", model, out Extrapolation motion);
            Quaternion initial = Quaternion.Euler(10, 30, 20);
            Double3 rate = new Double3(0.3d, 0.8d, 0.1d);
            Double3 velocity = new Double3(2, 1, 100);
            Double3 acceleration = new Double3(1, -2, 8);
            foreach (double time in new[] { 0d, 0.045d, 0.12d, 0.24d })
            {
                Quaternion attitude = Attitude(initial, rate, time);
                SetPose(spatial, Path(initial, rate, velocity, acceleration, time), attitude, time);
                motion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                Double3 currentVelocity = velocity + acceleration * time;
                motion.SetCartesianAcceleration(ToSourceVector(Rotate(attitude, acceleration + Cross(rate, currentVelocity))), Stamp(time));
            }

            AdvanceClock();
            _realm.Update();
            Assert.That(Error(spatial, Path(initial, rate, velocity, acceleration, 0.64d)), Is.LessThan(0.004d));

            // A teleport cannot fit against motion on the other side of the discontinuity.
            SetPose(spatial, new Double3(1000, 0, 0), Quaternion.identity, 0.3d);
            motion.ClearAcceleration();
            motion.ClearAngularVelocity();
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1000, 0, 0)), Is.LessThan(0.001d));
            SetPose(spatial, new Double3(1001, 0, 0), Quaternion.identity, 0.4d);
            motion.SetCartesianAcceleration(default(Double3), Stamp(0.4d));
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1005, 0, 0)), Is.LessThan(0.001d));
        }

        /// <summary>Default packet correction and optional smoothing retain steady-turn tracking at the shared predicted time.</summary>
        /// <returns>An iterator delivering a continuous timestamped aircraft stream.</returns>
        [UnityTest]
        public IEnumerator ContinuousTurn_DefaultPacketCorrectionTracksThePredictedTrajectory()
        {
            ReferenceFrame frame = _realm.ReferenceFrame;
            frame.FollowedGhost = new Key("turns", new Kind("turn"), "reference");
            frame.FollowRotation = false;
            frame.PredictionTime = 0.1f;
            Spatial reference = _source.Publish("reference").gameObject.AddComponent<Spatial>();
            CoordinatedTurnExtrapolation model = new CoordinatedTurnExtrapolation();
            Spatial direct = Create("packet correction", model, out Extrapolation directMotion);
            Spatial filtered = Create("with smoothing", model, out Extrapolation filteredMotion);
            Smoothing smoothing = filtered.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.08f;
            smoothing.RotationHalfLife = 0.04f;
            Quaternion initial = Quaternion.Euler(15, 20, -30);
            Double3 rate = new Double3(0.2d, 1.2d, -0.3d);
            Double3 bodyVelocity = new Double3(4, 2, 150);
            double started = Time.realtimeSinceStartupAsDouble;
            double maxPositionError = 0;
            float maxAngularError = 0;
            int checkedFrames = 0;
            do
            {
                double time = Time.realtimeSinceStartupAsDouble - started;
                reference.SetCartesianPosition(frame.Position, Stamp(time));
                Quaternion attitude = Attitude(initial, rate, time);
                Double3 position = Path(initial, rate, bodyVelocity, default(Double3), time);
                SetPose(direct, position, attitude, time);
                SetPose(filtered, position, attitude, time);
                Double3 velocity = ToSourceVector(Rotate(attitude, bodyVelocity));
                directMotion.SetCartesianVelocity(velocity, Stamp(time));
                filteredMotion.SetCartesianVelocity(velocity, Stamp(time));
                directMotion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                filteredMotion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
                _realm.Update();

                // Compare at the actual shared target, so scheduling jitter does not become
                // an assumed clock error. Give the filter time to settle after startup.
                if (time > 0.25d)
                {
                    double target = _realm.ProjectedTime.Value.ElapsedSince(Stamp(0));
                    Double3 expected = Path(initial, rate, bodyVelocity, default(Double3), target);
                    Quaternion expectedRotation = Attitude(initial, rate, target);
                    maxPositionError = Math.Max(maxPositionError, Math.Max(Error(direct, expected), Error(filtered, expected)));
                    maxAngularError = Mathf.Max(maxAngularError, Mathf.Max(Quaternion.Angle(direct.transform.rotation, expectedRotation),
                        Quaternion.Angle(filtered.transform.rotation, expectedRotation)));
                    checkedFrames++;
                }

                yield return new WaitForSecondsRealtime(1f / 60f);
            }
            while (Time.realtimeSinceStartupAsDouble - started < 0.55d);

            TestContext.WriteLine("Continuous turn: max position error=" + maxPositionError + " m; max angular error=" + maxAngularError + " degrees");
            Assert.That(checkedFrames, Is.GreaterThan(2));
            // Allow the packet-correction transient and variable editor frame spacing,
            // while rejecting a delayed trajectory (15 m behind at this speed and lead).
            Assert.That(maxPositionError, Is.LessThan(1d));
            Assert.That(maxAngularError, Is.LessThan(0.2f));
        }

        /// <summary>CTA recovers constant body acceleration from poses or SDK velocities in Cartesian and ECEF flight, including independently timed channels.</summary>
        /// <param name="geographic">Whether the feed uses ECEF positions and world angular rates rather than Cartesian NED and pose-derived rates.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstantTurnAcceleration_EstimatesAcceleratingTurns(bool geographic)
        {
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            if (geographic)
            {
                _realm.ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
            }

            ConstantTurnAccelerationExtrapolation strategy = new ConstantTurnAccelerationExtrapolation { PacketSmoothingDuration = 0 };
            Spatial poses = Create("CTA poses", strategy, out Extrapolation poseMotion);
            Spatial supplied = Create("CTA velocities", strategy, out Extrapolation suppliedMotion);
            Spatial currentOnly = Create("CTA current velocity", strategy, out Extrapolation currentMotion);
            Spatial control = Create("constant body speed", new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0 }, out Extrapolation controlMotion);
            Quaternion initial = Quaternion.Euler(15, 20, -30);
            Double3 rate = new Double3(0.2d, 1.2d, -0.3d);
            Double3 velocity = new Double3(4, 2, 150);
            Double3 acceleration = new Double3(1, -2, 8);
            foreach (double time in new[] { 0d, 0.03d, 0.075d, 0.12d, 0.18d, 0.24d })
            {
                Quaternion attitude = Attitude(initial, rate, time);
                Double3 position = Path(initial, rate, velocity, acceleration, time);
                SetPose(poses, position, attitude, time);
                SetPose(supplied, position, attitude, time);
                SetPose(currentOnly, position, attitude, time);
                SetPose(control, position, attitude, time);
                Double3 sourceVelocity = ToSourceVector(Rotate(attitude, velocity + acceleration * time));
                suppliedMotion.SetCartesianVelocity(sourceVelocity, Stamp(time));
                controlMotion.SetCartesianVelocity(sourceVelocity, Stamp(time));
                if (geographic)
                {
                    Double3 world = Rotate(attitude, rate);
                    Double3 enu = new Double3(-world.X, -world.Z, -world.Y);
                    poseMotion.SetGeographicAngularVelocity(enu, origin, Stamp(time));
                    suppliedMotion.SetGeographicAngularVelocity(enu, origin, Stamp(time));
                    currentMotion.SetGeographicAngularVelocity(enu, origin, Stamp(time));
                    controlMotion.SetGeographicAngularVelocity(enu, origin, Stamp(time));
                }
            }

            // A feed may expose only one current SDK velocity. Retained positions can
            // still determine acceleration while keeping that velocity authoritative.
            currentMotion.SetCartesianVelocity(ToSourceVector(Rotate(Attitude(initial, rate, 0.24d),
                velocity + acceleration * 0.24d)), Stamp(0.24d));
            AdvanceClock();
            _realm.Update();
            Double3 expected = Path(initial, rate, velocity, acceleration, 0.64d);
            double poseError = Error(poses, expected);
            double velocityError = Error(supplied, expected);
            double turnError = Error(control, expected);
            TestContext.WriteLine("Accelerating turn: CTA poses=" + poseError + " m; CTA velocities=" + velocityError + " m; CT=" + turnError + " m");
            Assert.That(poseError, Is.LessThan(0.008d));
            Assert.That(velocityError, Is.LessThan(0.008d));
            Assert.That(Error(currentOnly, expected), Is.LessThan(0.008d));
            Assert.That(turnError, Is.GreaterThan(0.3d));
            Assert.That(Quaternion.Angle(poses.transform.rotation, Attitude(initial, rate, 0.64d)), Is.LessThan(0.08f));
            _realm.Update();
            Assert.That(Error(poses, expected), Is.LessThan(0.008d));
            Assert.That(poses.PositionTime, Is.EqualTo(Stamp(0.24d)));

            // SDK velocity predates the new pose. The fit must supply its endpoint value,
            // rather than starting the next prediction with an old held speed.
            SetPose(supplied, Path(initial, rate, velocity, acceleration, 0.3d), Attitude(initial, rate, 0.3d), 0.3d);
            _realm.Update();
            Assert.That(Error(supplied, Path(initial, rate, velocity, acceleration, 0.7d)), Is.LessThan(0.008d));
        }

        /// <summary>Supplied inertial acceleration and zero motion override CTA fits, while reset windows recover from held through linear and accelerating motion.</summary>
        [Test]
        public void ConstantTurnAcceleration_PreservesSdkOverridesAndResetRecovery()
        {
            ConstantTurnAccelerationExtrapolation strategy = new ConstantTurnAccelerationExtrapolation { PacketSmoothingDuration = 0 };
            Spatial spatial = Create("CTA overrides", strategy, out Extrapolation motion);
            Quaternion initial = Quaternion.Euler(15, 20, -30);
            Double3 rate = new Double3(0.2d, 1.2d, -0.3d);
            Double3 velocity = new Double3(4, 2, 150);
            Double3 acceleration = new Double3(1, -2, 8);
            foreach (double time in new[] { 0d, 0.06d, 0.12d, 0.18d, 0.24d })
            {
                Quaternion attitude = Attitude(initial, rate, time);
                SetPose(spatial, Path(initial, rate, velocity, acceleration, time), attitude, time);
                motion.SetCartesianVelocity(ToSourceVector(Rotate(attitude, velocity + acceleration * time)), Stamp(time));
                motion.SetBodyAngularVelocity(rate, CoordinateSystem.Unity, Stamp(time));
            }

            AdvanceClock();
            _realm.Update();
            Double3 position = Path(initial, rate, velocity, acceleration, 0.24d);
            Quaternion observed = Attitude(initial, rate, 0.24d);
            Double3 currentVelocity = velocity + acceleration * 0.24d;
            motion.SetCartesianAcceleration(default(Double3), Stamp(0.24d));
            _realm.Update();
            Double3 expected = position + Path(observed, rate, currentVelocity, Cross(rate, currentVelocity) * -1d, 0.4d);
            Assert.That(Error(spatial, expected), Is.LessThan(0.008d));

            motion.ClearVelocity();
            motion.SetCartesianVelocity(default(Double3), Stamp(0.24d));
            _realm.Update();
            Assert.That(Error(spatial, position), Is.LessThan(0.002d));

            motion.ClearVelocity();
            motion.ClearAcceleration();
            motion.ClearAngularVelocity();
            SetPose(spatial, new Double3(1000, 0, 0), Quaternion.identity, 0.3d);
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1000, 0, 0)), Is.LessThan(0.001d));
            SetPose(spatial, new Double3(1001, 0, 0), Quaternion.identity, 0.4d);
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1005, 0, 0)), Is.LessThan(0.001d));
            SetPose(spatial, new Double3(1004, 0, 0), Quaternion.identity, 0.5d);
            _realm.Update();
            Assert.That(Error(spatial, new Double3(1036, 0, 0)), Is.LessThan(0.001d));

            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.Gravity = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.PacketSmoothingDuration = -0.1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.ExtrapolateDisplacement(default(ExtrapolationContext), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.ExtrapolateRotation(default(ExtrapolationContext), double.PositiveInfinity));
        }

        /// <summary>CTA without attitude uses world acceleration fits and does not invent velocity from acceleration alone.</summary>
        [Test]
        public void ConstantTurnAcceleration_MissingAttitudeUsesWorldMotion()
        {
            Spatial spatial = Create("CTA no attitude", new ConstantTurnAccelerationExtrapolation { PacketSmoothingDuration = 0 }, out Extrapolation motion);
            Double3 origin = _realm.ReferenceFrame.Position;
            spatial.SetCartesianPosition(origin, Stamp(0.2d));
            motion.SetCartesianVelocity(new Double3(5, 0, 0), Stamp(0));
            motion.SetCartesianVelocity(new Double3(7, 0, 0), Stamp(0.1d));
            AdvanceClock();
            _realm.Update();
            Assert.That(Double3.Distance(_realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position),
                origin + new Double3(5.2d, 0, 0)), Is.LessThan(0.001d));

            motion.ClearVelocity();
            motion.SetCartesianAcceleration(new Double3(20, 0, 0), Stamp(0.2d));
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(Double3.Distance(_realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position), origin), Is.LessThan(0.001d));
        }

        private Spatial Create(string id, IExtrapolationStrategy model, out Extrapolation motion)
        {
            Ghost ghost = _source.Publish(id);
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            motion = ghost.gameObject.AddComponent<Extrapolation>();
            motion.Strategy = model;
            motion.MaximumExtrapolation = 0.4f;
            return spatial;
        }

        private void AdvanceClock()
        {
            _source.Publish("clock").gameObject.AddComponent<Spatial>()
                .SetCartesianPosition(default(Double3), Stamp(2d));
        }

        private void SetPose(Spatial spatial, Double3 position, Quaternion attitude, double time)
        {
            ReferenceFrame frame = _realm.ReferenceFrame;
            spatial.SetCartesianPosition(frame.ToSimulationPosition(ToVector(position)), Stamp(time));
            if (frame.Space == ReferenceSpace.Geographic)
            {
                spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(attitude), sampleTime: Stamp(time));
            }
            else
            {
                spatial.SetSourceRotation(frame.ToSimulationRotation(attitude), Stamp(time));
            }
        }

        private Double3 ToSourceVector(Double3 vector)
        {
            return _realm.ReferenceFrame.ToSimulationPosition(ToVector(vector)) - _realm.ReferenceFrame.Position;
        }

        private static Vector3 ToVector(Double3 value)
        {
            return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        }

        private static Timestamp Stamp(double seconds)
        {
            Timestamp offset = Timestamp.FromSeconds(seconds);
            return new Timestamp(1000000000000L + offset.Seconds, offset.Nanoseconds);
        }

        private static double Error(Spatial spatial, Double3 expected)
        {
            Vector3 position = spatial.transform.position;
            return Double3.Distance(new Double3(position.x, position.y, position.z), expected);
        }

        private static Quaternion Attitude(Quaternion initial, Double3 rate, double seconds)
        {
            double speed = Math.Sqrt(rate.X * rate.X + rate.Y * rate.Y + rate.Z * rate.Z);
            return initial * Quaternion.AngleAxis((float)(speed * seconds * Mathf.Rad2Deg),
                new Vector3((float)(rate.X / speed), (float)(rate.Y / speed), (float)(rate.Z / speed)));
        }

        private static Double3 Path(Quaternion initial, Double3 rate, Double3 velocity, Double3 acceleration, double seconds)
        {
            // Independently integrate the prescribed aircraft motion. Midpoint sampling
            // avoids copying the strategy's closed-form integral into the expected result.
            const int steps = 4000;
            double step = seconds / steps;
            Double3 position = default(Double3);
            for (int index = 0; index < steps; index++)
            {
                double time = (index + 0.5d) * step;
                position += Rotate(Attitude(initial, rate, time), velocity + acceleration * time) * step;
            }

            return position;
        }

        private static Double3 Rotate(Quaternion rotation, Double3 value)
        {
            Vector3 rotated = rotation * new Vector3((float)value.X, (float)value.Y, (float)value.Z);
            return new Double3(rotated.x, rotated.y, rotated.z);
        }

        private static Double3 Cross(Double3 left, Double3 right)
        {
            return new Double3(left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);
        }
    }
}
