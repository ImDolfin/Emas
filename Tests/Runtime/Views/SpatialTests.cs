using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies optional realm-relative placement, partial spatial publications and presentation range.
    /// </summary>
    public sealed class SpatialTests
    {
        private sealed class TestGhost : Ghost
        {
            internal float Articulation { get; set; }
        }

        private sealed class HalfSpeedExtrapolation : IExtrapolationStrategy
        {
            internal Action<ExtrapolationContext> PositionObserved;
            internal Action<ExtrapolationContext> RotationObserved;

            /// <summary>Demonstrates a complete prediction that halves linear and angular travel.</summary>
            /// <param name="request">Both independently timed channels.</param>
            /// <returns>A full pose and its motion, composed using the source convention.</returns>
            public Prediction Predict(PredictionRequest request)
            {
                PositionObserved?.Invoke(request.PositionContext);
                RotationObserved?.Invoke(request.RotationContext);
                Double3 velocity = request.PositionContext.Velocity.GetValueOrDefault() * 0.5d;
                Double3 angular = new Double3(0, request.RotationContext.AngularVelocity.GetValueOrDefault().Y * 0.5d, 0);
                Quaternion offset = Quaternion.AngleAxis((float)(angular.Y * request.RotationSeconds * 180d / Math.PI), Vector3.up);
                return request.CreatePrediction(velocity * request.PositionSeconds, offset, velocity, angular);
            }
        }

        private sealed class VelocityHeadingExtrapolation : IExtrapolationStrategy
        {
            private readonly ConstantAccelerationExtrapolation _linear = new ConstantAccelerationExtrapolation();

            /// <summary>Combines built-in linear prediction with an application-specific heading.</summary>
            /// <param name="request">Both pose channels and their independent prediction durations.</param>
            /// <returns>A complete pose oriented along velocity when available.</returns>
            public Prediction Predict(PredictionRequest request)
            {
                Prediction linear = _linear.Predict(request);
                ExtrapolationContext context = request.RotationContext;
                if (request.RotationSeconds <= 0 || !context.Velocity.HasValue || !context.Rotation.HasValue)
                {
                    return linear;
                }

                Double3 velocity = context.Velocity.Value;
                Quaternion heading = Quaternion.LookRotation(new Vector3((float)velocity.X, (float)velocity.Y, (float)velocity.Z));
                return new Prediction(linear.Position, heading, linear.Velocity);
            }
        }

        private sealed class DistanceExtrapolation : IExtrapolationStrategy, IPacketSmoothingStrategy
        {
            internal int Calls;
            internal PredictionRequest Request;
            internal Prediction Result;
            private readonly ConstantAccelerationExtrapolation _motion = new ConstantAccelerationExtrapolation();

            /// <summary>Enables packet correction alongside prediction and smoothing.</summary>
            /// <value>A quarter-second correction duration.</value>
            public float PacketSmoothingDuration => 0.25f;

            /// <summary>Records complete model evaluations through the consumer API.</summary>
            /// <param name="request">The complete independently timed prediction request.</param>
            /// <returns>The built-in acceleration model's full result.</returns>
            public Prediction Predict(PredictionRequest request)
            {
                Calls++;
                Request = request;
                Result = _motion.Predict(request);
                return Result;
            }
        }

        private sealed class EnableProbe : MonoBehaviour
        {
            internal int EnableCount { get; private set; }
            internal Vector3 PositionOnEnable { get; private set; }

            private void OnEnable()
            {
                EnableCount++;
                PositionOnEnable = transform.position;
            }
        }

        private sealed class GeographicReading
        {
            internal GeoPosition Position;
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class GeographicTrait : Trait<GeoPosition>
        {
            /// <summary>Applies a bound WGS84 reading through the public geographic channel.</summary>
            /// <param name="position">The absolute WGS84 observation supplied by the bound reader.</param>
            public override void Apply(GeoPosition position)
            {
                GetComponent<Spatial>().SetGeographicPosition(position);
            }
        }

        private sealed class GeographicSource : PresenceDetector
        {
            internal Presence Arrive(string entityId, GeographicReading source)
            {
                return Detect(entityId, SpatialKind, source: source);
            }
        }

        private sealed class TestSource : PresenceDetector
        {
            internal Action NextUpdate { get; set; }

            internal TestGhost Publish(string entityId)
            {
                return GetOrCreate<TestGhost>(entityId, SpatialKind);
            }

            internal TestGhost PublishPosition(string entityId, Double3 position)
            {
                TestGhost ghost = Publish(entityId);
                GetSpatial(ghost).SetCartesianPosition(position);
                return ghost;
            }

            internal TestGhost PublishEarthCenteredPosition(string entityId, Double3 position)
            {
                TestGhost ghost = Publish(entityId);
                GetSpatial(ghost).SetEarthCenteredPosition(position);
                return ghost;
            }

            internal TestGhost PublishRotation(string entityId, Quaternion rotation)
            {
                TestGhost ghost = Publish(entityId);
                GetSpatial(ghost).SetSourceRotation(rotation);
                return ghost;
            }

            internal void PublishArticulation(string entityId, float articulation)
            {
                Publish(entityId).Articulation = articulation;
            }

            internal void RemoveEntity(string entityId)
            {
                Disappear(SpatialKind, entityId);
            }

            /// <summary>Consumes the queued action once so the scenario can publish changes during a Realm update.</summary>
            protected override void OnUpdate()
            {
                Action update = NextUpdate;
                NextUpdate = null;
                if (update != null)
                {
                    update();
                }
            }

            private static Spatial GetSpatial(TestGhost ghost)
            {
                Spatial spatial = ghost.GetComponent<Spatial>();
                return spatial != null ? spatial : ghost.gameObject.AddComponent<Spatial>();
            }
        }

        private static readonly Kind SpatialKind = new Kind("spatial.entity");
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private Realm _realm;
        private TestSource _source;
        private Anchor _anchor;

        /// <summary>
        /// Creates an isolated realm and source for each spatial scenario.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
            _source = new TestSource();
            _anchor = _realm.GetOrCreateAnchor("simulation", _source);
        }

        /// <summary>
        /// Releases tracked entities and test presentation assets.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
            for (int index = _assets.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(_assets[index]);
            }

            _assets.Clear();
        }

        /// <summary>Observed-pose diagnostics project accepted input through the presented reference, preserve custom attitude conventions, and leave prediction and filters untouched.</summary>
        /// <param name="representation">Zero for Cartesian NED, one for geographic attitude, or two for ECEF attitude with custom body axes.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ObservedPose_ProjectsRawInputThroughThePresentedReferenceWithoutMutation(int representation)
        {
            // Test the same moving reference with Cartesian, geographic and ECEF input.
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            GeoPosition targetLocation = new GeoPosition(48.001, 11.002, 1250);
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = representation == 0 ? ReferenceSpace.Cartesian : ReferenceSpace.Geographic,
                Position = representation == 0 ? new Double3(1e9, 0, 0) : origin.ToEarthCentered(),
                Coordinates = representation == 0 ? CoordinateSystem.NorthEastDown : CoordinateSystem.Unity,
                MaxDistance = 5000
            };
            _realm.ReferenceFrame = frame;
            TestGhost reference = _source.Publish("observed reference");
            Spatial referenceSpatial = reference.gameObject.AddComponent<Spatial>();
            Assert.That(referenceSpatial.TryGetObservedUnityPose(out _), Is.False);
            Timestamp time = new Timestamp(1000, 0);
            referenceSpatial.SetCartesianPosition(frame.Position, time);
            referenceSpatial.SetSourceRotation(Quaternion.identity, time);
            Extrapolation referenceExtrapolation = reference.gameObject.AddComponent<Extrapolation>();
            referenceExtrapolation.MaximumExtrapolation = 0.1f;
            if (representation == 0)
            {
                referenceExtrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(100, 0, 0), time);
            }
            else
            {
                referenceExtrapolation.GetComponent<Spatial>().SetGeographicVelocity(new Double3(0, 100, 0), origin, time);
            }
            // Use slow filters so a new raw input is clearly different from the displayed pose.
            frame.FollowedGhost = reference.Key;
            TestGhost ghost = _source.Publish("observed target");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.1f;
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(10, 0, 0), time);
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, 1), sampleTime: time);
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 10;
            smoothing.RotationHalfLife = 10;
            spatial.SetCartesianPosition(representation == 0 ? frame.Position + new Double3(100, 200, -50) : targetLocation.ToEarthCentered(), time);
            if (representation == 2)
            {
                ReferenceFrame local = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = targetLocation, FollowRotation = false };
                spatial.SetEarthCenteredRotation(local.ToEarthCenteredRotation(Quaternion.identity, CoordinateSystem.EastNorthUp), CoordinateSystem.EastNorthUp, time);
            }
            else if (representation == 1)
            {
                spatial.SetGeographicRotation(0, 0, 0, time);
            }
            else
            {
                spatial.SetSourceRotation(Quaternion.identity, time);
            }
            _source.Publish("observation clock").gameObject.AddComponent<Spatial>()
                .SetCartesianPosition(frame.Position, new Timestamp(1002, 0));
            _realm.Update();
            Assert.That(referenceSpatial.TryGetObservedUnityPose(out Pose referenceObservation), Is.True);
            Assert.That(Vector3.Distance(referenceObservation.position, reference.transform.position), Is.EqualTo(10).Within(0.002f),
                "The followed Ghost's observation must use the predicted reference rather than pinning the raw marker to the origin.");

            // Publish a sudden pose change without resetting the filters.
            time = new Timestamp(1001, 0);
            Double3 input = representation == 0 ? frame.Position + new Double3(150, 200, -50) : new GeoPosition(48.002, 11.002, 1250).ToEarthCentered();
            spatial.SetCartesianPosition(input, time);
            Quaternion localRotation = Quaternion.AngleAxis(75, Vector3.up) * Quaternion.AngleAxis(-20, Vector3.right);
            Quaternion expectedRotation;
            if (representation == 2)
            {
                ReferenceFrame local = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = GeoPosition.FromEarthCentered(input), FollowRotation = false };
                Quaternion earthRotation = local.ToEarthCenteredRotation(localRotation, CoordinateSystem.EastNorthUp);
                spatial.SetEarthCenteredRotation(earthRotation, CoordinateSystem.EastNorthUp, time);
                expectedRotation = frame.ToUnityEarthCenteredRotation(earthRotation, CoordinateSystem.EastNorthUp);
            }
            else if (representation == 1)
            {
                spatial.SetGeographicRotation(75, 20, 0, time);
                expectedRotation = frame.ToUnityRotation(localRotation, GeoPosition.FromEarthCentered(input));
            }
            else
            {
                spatial.SetSourceRotation(localRotation, time);
                expectedRotation = frame.ToUnityRotation(localRotation);
            }

            _realm.Update();
            Vector3 presentedPosition = ghost.transform.position;
            Quaternion presentedRotation = ghost.transform.rotation;
            Assert.That(frame.TryToUnityPosition(input, out Vector3 expectedPosition), Is.True);
            // Repeated debug reads must return raw input without changing the displayed pose or sample time.
            for (int read = 0; read < 3; read++)
            {
                Assert.That(spatial.TryGetObservedUnityPose(out Pose observed), Is.True);
                AssertPosition(observed.position, expectedPosition, 0.002f);
                AssertRotation(observed.rotation, expectedRotation);
                Assert.That(Vector3.Distance(observed.position, presentedPosition), Is.GreaterThan(30));
                Assert.That(Quaternion.Angle(observed.rotation, presentedRotation), Is.GreaterThan(40));
                Assert.That(ghost.transform.position, Is.EqualTo(presentedPosition));
                Assert.That(ghost.transform.rotation, Is.EqualTo(presentedRotation));
                Assert.That(spatial.Position, Is.EqualTo(input));
                Assert.That(spatial.PositionTime, Is.EqualTo(time));
            }

            // Changing the range limit may hide the debug pose, but must keep the raw observation.
            frame.MaxDistance = 1;
            Assert.That(spatial.TryGetObservedUnityPose(out _), Is.False);
            frame.MaxDistance = 5000;
            Assert.That(spatial.TryGetObservedUnityPose(out _), Is.True);
        }

        /// <summary>Sudden bank and pitch changes apply the full inverse reference attitude without motion traits or playback delay, even when position observations are older.</summary>
        /// <param name="geographic">True for WGS84 and named aircraft angles; false for Cartesian NED input.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void ReferenceAttitude_SuddenBankAndPitchImmediatelyRepositionSideTargets(bool geographic)
        {
            // Keep positions still and independently timed so rotation alone determines the response.
            GeoPosition location = new GeoPosition(52, 13, 1000);
            Double3 origin = geographic ? location.ToEarthCentered() : new Double3(1e9, 0, 0);
            TestGhost reference = _source.PublishPosition("banking reference", origin);
            Spatial spatial = reference.GetRequired<Spatial>();
            spatial.SetCartesianPosition(origin, new Timestamp(1, 0));
            reference.gameObject.AddComponent<Extrapolation>().enabled = false;
            reference.gameObject.AddComponent<Smoothing>().enabled = false;
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = geographic ? ReferenceSpace.Geographic : ReferenceSpace.Cartesian,
                Coordinates = geographic ? CoordinateSystem.Unity : CoordinateSystem.NorthEastDown,
                FollowedGhost = reference.Key
            };
            _realm.ReferenceFrame = frame;
            _realm.InterpolationDelay = 0;
            Vector3[] offsets = { new Vector3(100, 0, 500), new Vector3(-100, 0, 500), new Vector3(10000, 0, 5000) };
            TestGhost[] targets = new TestGhost[offsets.Length];
            for (int index = 0; index < targets.Length; index++)
            {
                Vector3 offset = offsets[index];
                Double3 displacement = geographic
                    ? location.ToEarthCenteredVector(new Double3(offset.x, offset.z, offset.y))
                    : new Double3(offset.z, offset.x, -offset.y);
                targets[index] = _source.PublishPosition("side target " + index, origin + displacement);
            }

            // Step directly between large angles. Prediction must not be needed to apply an accepted attitude.
            Vector2[] attitudes = { new Vector2(0, 30), new Vector2(0, 85), new Vector2(0, -85), new Vector2(60, 70) };
            for (int step = 0; step < attitudes.Length; step++)
            {
                float pitch = attitudes[step].x;
                float bank = attitudes[step].y;
                Timestamp time = new Timestamp(2 + step, 0);
                if (geographic)
                {
                    spatial.SetGeographicRotation(0, pitch, bank, time);
                }
                else
                {
                    spatial.SetSourceRotation(Quaternion.AngleAxis(pitch, Vector3.up)
                        * Quaternion.AngleAxis(bank, Vector3.right), time);
                }

                _realm.Update();
                AssertRotation(reference.transform.rotation, Quaternion.identity);
                Assert.That(spatial.PositionTime, Is.EqualTo(new Timestamp(1, 0)));
                double p = pitch * Math.PI / 180d;
                double r = bank * Math.PI / 180d;
                for (int index = 0; index < targets.Length; index++)
                {
                    // Undo pitch, then bank, directly with scalar trigonometry rather than Emas conversion helpers.
                    Vector3 offset = offsets[index];
                    Vector3 expected = new Vector3(
                        (float)(offset.x * Math.Cos(r) + offset.z * Math.Sin(p) * Math.Sin(r)),
                        (float)(offset.x * Math.Sin(r) - offset.z * Math.Sin(p) * Math.Cos(r)),
                        (float)(offset.z * Math.Cos(p)));
                    AssertPosition(targets[index].transform.position, expected, 0.02f);
                }
            }

            // A world-level view follows position only: its targets stay level while the reference model banks.
            frame.FollowRotation = false;
            _realm.Update();
            for (int index = 0; index < targets.Length; index++)
            {
                AssertPosition(targets[index].transform.position, offsets[index], 0.02f);
            }
            Assert.That(Quaternion.Angle(reference.transform.rotation, Quaternion.identity), Is.GreaterThan(60));
        }

        /// <summary>
        /// An absent reference maps spatial poses to world space, including after clearing a custom frame.
        /// </summary>
        [Test]
        public void MissingReference_UsesIdentityWorldPoseAndClearingRestoresIt()
        {
            // With no reference set, source positions must map directly to world positions even under a moved anchor.
            Assert.That(_realm.ReferenceFrame, Is.Null);
            _anchor.Transform.position = new Vector3(50, 20, -10);
            _anchor.Transform.rotation = Quaternion.Euler(0, 30, 0);
            _anchor.Transform.localScale = new Vector3(2, 3, 4);
            TestGhost ghost = _source.PublishPosition("remote", new Double3(1000, 2, 10));
            Quaternion rotation = Quaternion.Euler(10, 45, 20);
            ghost.transform.rotation = rotation;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(1000, 2, 10), 0.001f);
            AssertRotation(ghost.transform.rotation, rotation);
            Assert.That(ghost.GetComponent<Spatial>().TryGetObservedUnityPose(out Pose observed), Is.True);
            AssertPosition(observed.position, ghost.transform.position);
            Assert.That(observed.rotation, Is.EqualTo(Quaternion.identity), "Position-only observations must not invent an attitude from the presented transform.");

            rotation = Quaternion.Euler(20, 60, 30);
            _source.PublishRotation("remote", rotation);
            _realm.Update();
            AssertRotation(ghost.transform.rotation, rotation);

            ReferenceFrame frame = new ReferenceFrame
            {
                Position = new Double3(1000, 0, 0),
                Rotation = Quaternion.Euler(0, 90, 0)
            };
            _realm.ReferenceFrame = frame;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(-10, 2, 0), 0.001f);
            AssertRotation(ghost.transform.rotation, frame.ToUnityRotation(rotation));

            _realm.ReferenceFrame = null;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(1000, 2, 10), 0.001f);
            AssertRotation(ghost.transform.rotation, rotation);
            Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.True);

            // Geographic angles need a Geographic reference. Hide incompatible input and recover when the reference is fixed.
            Spatial spatial = ghost.GetComponent<Spatial>();
            spatial.SetGeographicRotation(0, 0, 0);
            ExpectedErrors.Verify(() => _realm.Update(), "InvalidOperationException: Geographic yaw/pitch/roll requires a Geographic reference frame");
            Assert.That(spatial.IsInRange, Is.False);
            AssertPosition(ghost.transform.position, new Vector3(1000, 2, 10), 0.001f);
            spatial.SetSourceRotation(rotation);
            _realm.Update();
            Assert.That(spatial.RotationSpace, Is.EqualTo(RotationSpace.Source));
            Assert.That(spatial.IsInRange, Is.True);
            AssertRotation(ghost.transform.rotation, rotation);
        }

        /// <summary>
        /// Disabling Spatial lets the application position the root even without a reference frame.
        /// </summary>
        [Test]
        public void DisabledSpatial_ReleasesIdentityPlacementUntilReenabled()
        {
            TestGhost ghost = _source.PublishPosition("remote", new Double3(10, 20, 30));
            _realm.Update();
            Spatial spatial = ghost.GetComponent<Spatial>();
            spatial.enabled = false;
            Vector3 position = new Vector3(1, 2, 3);
            Quaternion rotation = Quaternion.Euler(10, 20, 30);
            ghost.transform.SetPositionAndRotation(position, rotation);
            _source.PublishPosition("remote", new Double3(40, 50, 60));
            _source.PublishRotation("remote", Quaternion.identity);
            _realm.Update();
            AssertPosition(ghost.transform.position, position);
            AssertRotation(ghost.transform.rotation, rotation);

            spatial.enabled = true;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(40, 50, 60));
            AssertRotation(ghost.transform.rotation, Quaternion.identity);
        }

        /// <summary>
        /// Moving the reference reprojects stationary entities without another publication.
        /// </summary>
        [Test]
        public void ReferenceMovement_ReprojectsCachedEntityPosition()
        {
            ReferenceFrame frame = new ReferenceFrame { Position = new Double3(1e12, 0, 0) };
            _realm.ReferenceFrame = frame;
            TestGhost ghost = _source.PublishPosition("remote", new Double3(1e12 + 20.25, 0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(20.25f, 0, 0));

            frame.Position = new Double3(1e12 + 2, 0, 0);
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(18.25f, 0, 0));
        }

        /// <summary>
        /// Changing a frame's convention reprojects cached poses on the next update without replacing Ghosts or views.
        /// </summary>
        [Test]
        public void CoordinateChanges_ReprojectCachedPoseAndKeepView()
        {
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame
            {
                Position = default,
                Coordinates = CoordinateSystem.NorthEastDown
            };
            _realm.ReferenceFrame = frame;
            Double3 sourcePosition = new Double3(10, 20, -3);
            Quaternion sourceRotation = Quaternion.AngleAxis(90, Vector3.forward);
            TestGhost ghost = _source.PublishPosition("car", sourcePosition);
            _source.PublishRotation("car", sourceRotation);
            _realm.Update();
            View view = _realm.Manifest(ghost);
            AssertPosition(ghost.transform.position, new Vector3(20, 3, 10));
            AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(90, Vector3.up));

            frame.Coordinates = CoordinateSystem.Unity;
            AssertPosition(ghost.transform.position, new Vector3(20, 3, 10));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(10, 20, -3));
            AssertRotation(ghost.transform.rotation, sourceRotation);
            Assert.That(_realm.Query().Single(), Is.SameAs(ghost));
            Assert.That(ActiveView(ghost), Is.SameAs(view));
            Assert.That(ghost.GetComponent<Spatial>().Position, Is.EqualTo(sourcePosition));
            AssertRotation(ghost.GetComponent<Spatial>().Rotation, sourceRotation);
        }

        /// <summary>
        /// A followed NED pose anchors both position and heading, including while its last valid pose is retained after loss.
        /// </summary>
        [Test]
        public void FollowedCoordinates_AlignAndRetainReferencePose()
        {
            Double3 origin = new Double3(1e12, 1e12, -1e12);
            Quaternion heading = Quaternion.AngleAxis(90, Vector3.forward);
            TestGhost ego = _source.PublishPosition("ego", origin);
            _source.PublishRotation("ego", heading);
            TestGhost target = _source.PublishPosition("target", origin + new Double3(10, 0, 0));
            _source.PublishRotation("target", heading);
            ReferenceFrame frame = new ReferenceFrame
            {
                FollowedGhost = ego.Key,
                Coordinates = CoordinateSystem.NorthEastDown
            };
            _realm.ReferenceFrame = frame;
            _realm.Update();
            AssertPosition(ego.transform.position, Vector3.zero);
            AssertRotation(ego.transform.rotation, Quaternion.identity);
            AssertPosition(target.transform.position, new Vector3(-10, 0, 0));
            AssertRotation(target.transform.rotation, Quaternion.identity);

            _source.RemoveEntity("ego");
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            Assert.That(frame.Position, Is.EqualTo(origin));
            AssertPosition(target.transform.position, new Vector3(-10, 0, 0));
            AssertRotation(target.transform.rotation, Quaternion.identity);
        }

        /// <summary>
        /// Geographic following reprojects cached entities and their local attitudes, retaining views and freezing on reference loss.
        /// </summary>
        [Test]
        public void GeographicFollowing_UpdatesCachedPosesAndRetainsReferenceOnLoss()
        {
            // Move the followed origin without changing the target's raw ECEF position or replacing its view.
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame { Space = ReferenceSpace.Geographic, FollowRotation = false };
            _realm.ReferenceFrame = frame;
            TestGhost ego = _source.PublishEarthCenteredPosition("ego", new GeoPosition(0, 0, 0).ToEarthCentered());
            TestGhost target = _source.PublishEarthCenteredPosition("target", new GeoPosition(0, 0, 10).ToEarthCentered());
            _source.PublishRotation("target", Quaternion.identity);
            frame.FollowedGhost = ego.Key;
            _realm.Update();
            View view = _realm.Manifest(target);
            Assert.That(view, Is.Not.Null);
            AssertPosition(target.transform.position, new Vector3(0, 10, 0));
            Double3 stored = target.GetComponent<Spatial>().Position;

            ego.GetComponent<Spatial>().SetGeographicPosition(new GeoPosition(0, 90, 0));
            _realm.Update();
            AssertPosition(target.transform.position, new Vector3(-6378147, -6378137, 0), 1);
            AssertRotation(target.transform.rotation, Quaternion.AngleAxis(90, Vector3.forward));
            Assert.That(target.GetComponent<Spatial>().Position, Is.EqualTo(stored));
            Assert.That(ActiveView(target), Is.SameAs(view));
            Vector3 projected = target.transform.position;
            // Keep the last valid reference when its position becomes unusable or its Ghost disappears.
            ego.GetComponent<Spatial>().SetEarthCenteredPosition(new Double3(double.MaxValue, double.MaxValue, double.MaxValue));
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            AssertPosition(target.transform.position, projected, 1);
            _source.RemoveEntity("ego");
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            AssertPosition(target.transform.position, projected, 1);

            // A new Ghost with the same key restores following without replacing the target's view.
            TestGhost replacement = _source.PublishEarthCenteredPosition("ego", new GeoPosition(0, 0, 0).ToEarthCentered());
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.True);
            AssertPosition(target.transform.position, new Vector3(0, 10, 0));
            AssertRotation(target.transform.rotation, Quaternion.identity);
            Assert.That(ActiveView(target), Is.SameAs(view));
        }

        /// <summary>Independent geographic input channels retain their tangent origins and validate stale packets while height, origins and input representations change.</summary>
        [Test]
        public void GeographicInputs_KeepIndependentOriginsAcrossRepeatedAndChangedSamples()
        {
            TestGhost ghost = _source.Publish("independent geographic traits");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            GeoPosition first = new GeoPosition(48, 11, 1000);
            GeoPosition second = new GeoPosition(-30, -70, -100);
            GeoPosition angularOrigin = new GeoPosition(10, 120, 0);
            Double3 velocity = new Double3(3, 4, 5);
            Double3 acceleration = new Double3(1, 2, 3);
            Double3 angular = new Double3(0.01d, 0.02d, 0.03d);
            GeoPosition[] positions = { first, new GeoPosition(48, 11, 1250), second, first };
            for (int index = 0; index < positions.Length; index++)
            {
                GeoPosition position = positions[index];
                Timestamp time = new Timestamp(1000 + index, 0);
                _realm.ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = position };
                // Model separate physical traits, including different SDK tangent origins.
                spatial.SetGeographicVelocity(velocity, position, time);
                if (index == 2)
                {
                    spatial.SetEarthCenteredPosition(position.ToEarthCentered(), time);
                }
                else
                {
                    spatial.SetGeographicPosition(position, time);
                }

                spatial.SetGeographicAcceleration(acceleration, second, time);
                spatial.SetGeographicAngularVelocity(angular, angularOrigin, time);
                spatial.SetGeographicRotation(0, 0, 0, time);
                _realm.Update();
                Assert.That(spatial.Position, Is.EqualTo(position.ToEarthCentered()));
                Assert.That(spatial.Velocity, Is.EqualTo(position.ToEarthCenteredVector(velocity)));
                Assert.That(spatial.Acceleration, Is.EqualTo(second.ToEarthCenteredVector(acceleration)));
                Assert.That(spatial.AngularVelocity, Is.EqualTo(angularOrigin.ToEarthCenteredVector(angular)));
                AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
                AssertRotation(ghost.transform.rotation, Quaternion.identity);

                // Repeat the same timed input after other channels changed their origins.
                // A height-only change cannot affect vector axes or make this packet fresh.
                GeoPosition sameAxes = new GeoPosition(position.LatitudeDegrees, position.LongitudeDegrees, position.HeightMeters + 50);
                spatial.SetGeographicVelocity(velocity, sameAxes, time);
                spatial.SetGeographicPosition(position, time);
                Assert.That(spatial.PositionTime, Is.EqualTo(time));
                Assert.That(spatial.Velocity, Is.EqualTo(position.ToEarthCenteredVector(velocity)));
            }

            GeoPosition invalid = JsonUtility.FromJson<GeoPosition>("{\"_latitudeDegrees\":91,\"_longitudeDegrees\":11,\"_heightMeters\":1000}");
            Assert.Throws<ArgumentOutOfRangeException>(() => spatial.SetGeographicPosition(invalid, new Timestamp(1, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => spatial.SetGeographicVelocity(velocity, invalid, new Timestamp(1, 0)));
            for (int repeat = 0; repeat < 2; repeat++)
            {
                spatial.SetGeographicPosition(first);
                spatial.SetGeographicVelocity(velocity, first);
                spatial.SetGeographicAcceleration(acceleration, second);
                spatial.SetGeographicAngularVelocity(angular, angularOrigin);
            }

            Assert.That(ghost.GetComponent<Extrapolation>(), Is.Null, "Supplying raw motion must not add a prediction component.");
            Assert.That(spatial.PositionTime, Is.Null);
            Assert.That(spatial.HasVelocity && spatial.HasAcceleration && spatial.HasAngularVelocity, Is.True);
            Assert.That(spatial.Velocity, Is.EqualTo(first.ToEarthCenteredVector(velocity)));
            Double3? predictedVelocity = null;
            Extrapolation prediction = ghost.gameObject.AddComponent<Extrapolation>();
            prediction.Strategy = new HalfSpeedExtrapolation { PositionObserved = context => predictedVelocity = context.Velocity };
            _realm.Update();
            Assert.That(predictedVelocity, Is.EqualTo(spatial.Velocity), "Prediction added later must read the already supplied Spatial motion.");
            UnityEngine.Object.DestroyImmediate(prediction);
            Assert.That(spatial.HasVelocity && spatial.HasAcceleration && spatial.HasAngularVelocity, Is.True);
        }

        /// <summary>
        /// Bound WGS84 readers follow another anchor's position and full attitude without shrinking separation or rediscovery.
        /// </summary>
        [Test]
        public void GeographicReaders_FollowAnotherAnchorAndRefreshWithoutRediscovery()
        {
            GameObject rootPrefab = new GameObject("geographic root");
            rootPrefab.SetActive(false);
            Ghost root = rootPrefab.AddComponent<Ghost>();
            rootPrefab.AddComponent<GeographicTrait>();
            GameObject viewPrefab = new GameObject("geographic view");
            viewPrefab.SetActive(false);
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(SpatialKind, root, null, viewPrefab);
            _assets.Add(rootPrefab);
            _assets.Add(viewPrefab);
            _assets.Add(blueprint);
            _realm.RegisterManifestationBlueprint(blueprint);
            _realm.RegisterPresenceInitializer<Ghost>(SpatialKind, (presence, ghost) =>
            {
                ghost.GetComponent<GeographicTrait>().Bind(() => ((GeographicReading)presence.Source).Position);
            });

            GeographicSource targets = new GeographicSource();
            GeographicSource references = new GeographicSource();
            _realm.GetOrCreateAnchor("targets", targets);
            _realm.GetOrCreateAnchor("references", references);
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                FollowedGhost = new Key("references", SpatialKind, "ego"),
                FollowRotation = false,
                MaxDistance = 5000
            };
            _realm.ReferenceFrame = frame;
            GeographicReading targetReading = new GeographicReading
            {
                Position = new GeoPosition(52.520108, 13.405154, 50.125)
            };
            GeographicReading referenceReading = new GeographicReading
            {
                Position = new GeoPosition(52.520008, 13.404954, 40.125)
            };
            Presence target = targets.Arrive("target", targetReading);
            Assert.That(_realm.Manifest(target), Is.Null);
            Assert.That(frame.HasPosition, Is.False);
            Presence reference = references.Arrive("ego", referenceReading);
            _realm.Update();

            // Expected positions come from PROJ, with east/north/up reordered to Unity's east/up/north.
            AssertPosition(target.Root.transform.position, new Vector3(13.575955788316f, 9.999975872273f, 11.127827524704f));
            AssertPosition(reference.Root.transform.position, Vector3.zero);
            Assert.That(frame.IsReferenceAvailable, Is.True);
            View view = _realm.Manifest(target);
            Assert.That(view, Is.Not.Null);
            Double3 storedTarget = target.Root.GetComponent<Spatial>().Position;

            referenceReading.Position = new GeoPosition(52.520058, 13.405054, 45.125);
            _realm.Update();
            AssertPosition(target.Root.transform.position, new Vector3(6.787977894133f, 4.999993968840f, 5.563909085825f));
            Assert.That(target.Root.GetComponent<Spatial>().Position, Is.EqualTo(storedTarget));
            Assert.That(_realm.Manifest(target), Is.SameAs(view));

            reference.Root.GetComponent<Spatial>().SetSourceRotation(Quaternion.AngleAxis(90, Vector3.up));
            frame.FollowRotation = true;
            _realm.Update();
            AssertPosition(target.Root.transform.position, new Vector3(-5.563909085825f, 4.999993968840f, 6.787977894133f));
            AssertPosition(reference.Root.transform.position, Vector3.zero);

            Spatial referenceSpatial = reference.Root.GetComponent<Spatial>();
            // Use heading 90, nose-up pitch 30 and right-wing-down roll 90. Calculate the expected offsets directly.
            referenceSpatial.SetGeographicRotation(450, 30, 90);
            frame.Coordinates = CoordinateSystem.NorthEastDown;
            _realm.Update();
            Vector3 bankedOffset = new Vector3(-0.936132848718f, -5.563909085825f, 8.378558281066f);
            AssertPosition(target.Root.transform.position, bankedOffset);
            AssertRotation(reference.Root.transform.rotation, Quaternion.identity);
            Assert.That(referenceSpatial.RotationSpace, Is.EqualTo(RotationSpace.Geographic));
            Assert.That(frame.RotationSpace, Is.EqualTo(RotationSpace.Geographic));
            Quaternion retainedAttitude = referenceSpatial.Rotation;
            Assert.Throws<ArgumentOutOfRangeException>(() => referenceSpatial.SetGeographicRotation(0, double.NaN, 0));
            Assert.That(referenceSpatial.RotationSpace, Is.EqualTo(RotationSpace.Geographic));
            AssertRotation(referenceSpatial.Rotation, retainedAttitude);
            Assert.That(Vector3.Distance(target.Root.transform.position, reference.Root.transform.position),
                Is.EqualTo(Double3.Distance(storedTarget, referenceSpatial.Position)).Within(0.0001));
            Assert.That(_realm.Manifest(target), Is.SameAs(view));

            // Changing source quaternion axes must not change the meaning of geographic yaw, pitch and roll.
            frame.Coordinates = CoordinateSystem.EastNorthUp;
            _realm.Update();
            AssertPosition(target.Root.transform.position, bankedOffset);
            Assert.That(target.Root.GetComponent<Spatial>().Position, Is.EqualTo(storedTarget));

            frame.Coordinates = CoordinateSystem.Unity;
            referenceSpatial.SetSourceRotation(Quaternion.identity);
            _realm.Update();
            Assert.That(frame.RotationSpace, Is.EqualTo(RotationSpace.Source));
            AssertPosition(target.Root.transform.position, new Vector3(6.787977894133f, 4.999993968840f, 5.563909085825f));
            // Keep these sources alive. The application owns them, and Presence does not keep them alive.
            GC.KeepAlive(targetReading);
            GC.KeepAlive(referenceReading);
        }

        /// <summary>
        /// ECEF channels update independently, can drive the reference, and coexist with local geographic attitudes.
        /// </summary>
        [Test]
        public void EarthCenteredChannels_RetainGlobalAttitudeAndFollowMixedInputs()
        {
            // Rotation alone cannot display a spatial view. It also needs position, which may arrive separately.
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                GeographicPosition = new GeoPosition(0, 0, 0),
                Coordinates = CoordinateSystem.NorthEastDown,
                FollowRotation = false
            };
            _realm.ReferenceFrame = frame;
            TestGhost target = _source.Publish("earth");
            Spatial spatial = target.gameObject.AddComponent<Spatial>();
            Quaternion north = Quaternion.AngleAxis(-90, Vector3.up);
            spatial.SetEarthCenteredRotation(north);
            _realm.Update();
            Assert.That(_realm.Manifest(target), Is.Null);
            spatial.SetEarthCenteredPosition(new Double3(6378137, 0, 0));
            _realm.Update();
            View view = ActiveView(target);
            Assert.That(view, Is.Not.Null);
            AssertRotation(target.transform.rotation, Quaternion.identity);
            Assert.That(spatial.RotationSpace, Is.EqualTo(RotationSpace.EarthCentered));
            Assert.Throws<ArgumentException>(() => spatial.SetEarthCenteredRotation(Quaternion.identity, CoordinateSystem.Unity));

            // Moving the target keeps its ECEF rotation. Moving the reference changes how that rotation appears in Unity.
            spatial.SetEarthCenteredPosition(new GeoPosition(0, 90, 0).ToEarthCentered());
            _realm.Update();
            AssertRotation(spatial.Rotation, north);
            AssertRotation(target.transform.rotation, Quaternion.identity);
            frame.Position = spatial.Position;
            _realm.Update();
            AssertRotation(target.transform.rotation, Quaternion.AngleAxis(90, Vector3.forward));
            Assert.That(ActiveView(target), Is.SameAs(view));

            // The followed ECEF rotation and another target's local rotation must use the same reference.
            TestGhost local = _source.PublishEarthCenteredPosition("local", spatial.Position);
            _source.PublishRotation("local", Quaternion.identity);
            frame.FollowedGhost = target.Key;
            frame.FollowRotation = true;
            _realm.Update();
            Assert.That(frame.RotationSpace, Is.EqualTo(RotationSpace.EarthCentered));
            AssertRotation(target.transform.rotation, Quaternion.identity);
            AssertRotation(local.transform.rotation, Quaternion.AngleAxis(-90, Vector3.forward));
            Assert.That(ActiveView(target), Is.SameAs(view));

            // Switching rotation axes must update the followed reference and both displayed rotations.
            spatial.SetSourceRotation(Quaternion.identity);
            _realm.Update();
            Assert.That(spatial.RotationSpace, Is.EqualTo(RotationSpace.Source));
            Assert.That(frame.RotationSpace, Is.EqualTo(RotationSpace.Source));
            AssertRotation(target.transform.rotation, Quaternion.identity);
            AssertRotation(local.transform.rotation, Quaternion.identity);
        }

        /// <summary>
        /// Parent translation, rotation and scale do not get applied twice to projected world poses.
        /// </summary>
        [Test]
        public void Projection_CompensatesTransformedAnchorParents()
        {
            GameObject parent = new GameObject("transformed anchor parent");
            _assets.Add(parent);
            parent.transform.position = new Vector3(200, -30, 100);
            parent.transform.rotation = Quaternion.Euler(15, 70, 20);
            parent.transform.localScale = new Vector3(2, 3, 4);
            _anchor.Transform.SetParent(parent.transform, false);
            _anchor.Transform.localPosition = new Vector3(5, 6, 7);
            _anchor.Transform.localRotation = Quaternion.Euler(0, 20, 0);
            _realm.ReferenceFrame = new ReferenceFrame
            {
                Position = new Double3(10000, 20000, 30000),
                UnityPosition = new Vector3(1, 2, 3)
            };
            TestGhost ghost = _source.PublishPosition("remote", new Double3(10010, 20020, 30030));
            Quaternion orientation = Quaternion.Euler(20, 40, 60);
            _source.PublishRotation("remote", orientation);
            _realm.Update();

            Assert.That(ghost.transform.parent, Is.SameAs(_anchor.Transform));
            AssertPosition(ghost.transform.position, new Vector3(11, 22, 33), 0.001f);
            AssertRotation(ghost.transform.rotation, orientation);
        }

        /// <summary>
        /// Independent position, orientation and articulation packets do not reset unrelated state.
        /// </summary>
        [Test]
        public void PartialPublications_KeepIndependentSpatialAndArticulationState()
        {
            // Set position first, then send rotation and application data in separate updates.
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1000, 0, 0) };
            Double3 position = new Double3(1005, 2, 3);
            TestGhost ghost = _source.PublishPosition("remote", position);
            Quaternion initialRotation = Quaternion.Euler(0, 15, 0);
            ghost.transform.rotation = initialRotation;
            _realm.Update();
            Assert.That(ghost.GetComponent<Spatial>().HasRotation, Is.False);
            AssertRotation(ghost.transform.rotation, initialRotation);
            Quaternion rotation = Quaternion.Euler(0, 45, 0);
            _source.PublishRotation("remote", rotation);
            _realm.Update();
            _source.PublishArticulation("remote", 27);
            _realm.Update();

            Spatial spatial = ghost.GetComponent<Spatial>();
            Assert.That(spatial.Position, Is.EqualTo(position));
            Assert.That(spatial.HasRotation, Is.True);
            AssertRotation(spatial.Rotation, rotation);
            AssertPosition(ghost.transform.position, new Vector3(5, 2, 3));
            AssertRotation(ghost.transform.rotation, rotation);
            Assert.That(ghost.Articulation, Is.EqualTo(27));

            _source.PublishPosition("remote", new Double3(1006, 2, 3));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(6, 2, 3));
            AssertRotation(ghost.transform.rotation, rotation);
            Assert.That(ghost.Articulation, Is.EqualTo(27));
        }

        /// <summary>
        /// Rotation arriving before position cannot create a visual at an invented location.
        /// </summary>
        /// <param name="useReference">Whether to begin with an explicit reference frame.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void MissingFirstPosition_DefersRequestedViewUntilPositionArrives(bool useReference)
        {
            RegisterView();
            if (useReference)
            {
                _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1000, 0, 0) };
            }
            TestGhost ghost = _source.PublishRotation("remote", Quaternion.Euler(0, 45, 0));
            _realm.Update();
            Spatial spatial = ghost.GetComponent<Spatial>();
            Assert.That(spatial.HasPosition, Is.False);
            Assert.That(spatial.IsInRange, Is.False);
            Assert.That(_realm.Manifest(ghost), Is.Null);
            Assert.That(ghost.IsAvailable, Is.True);

            _source.PublishPosition("remote", new Double3(1010, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(ActiveView(ghost), Is.Not.Null);
            AssertPosition(ghost.transform.position, new Vector3(useReference ? 10 : 1010, 0, 0));
        }

        /// <summary>
        /// Range suppresses presentation while preserving identity, availability and subscriptions.
        /// </summary>
        /// <param name="cancelWhileFar">Whether to cancel the view request while the entity is outside the presentation range.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void RangeChanges_PreserveMembershipAndRespectPresentationRequests(bool cancelWhileFar)
        {
            // Check tracking and view requests separately at, inside and outside the range limit.
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 50 };
            _realm.ReferenceFrame = frame;
            int arrivals = 0;
            int departures = 0;
            using (IDisposable subscription = _realm.Query().Observe(ghost => arrivals++, key => departures++))
            {
                TestGhost ghost = _source.PublishPosition("remote", new Double3(30, 40, 0));
                _realm.Update();
                View firstView = _realm.Manifest(ghost);
                Assert.That(firstView, Is.Not.Null, "The range boundary is inclusive.");
                Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.True);

                _source.PublishPosition("remote", new Double3(30, 40.001, 0));
                _realm.Update();
                Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.False);
                Assert.That(ActiveView(ghost), Is.Null);
                Assert.That(ghost.IsAvailable, Is.True);
                Assert.That(ghost.gameObject.activeInHierarchy, Is.True);
                Assert.That(_realm.Query().Count, Is.EqualTo(1));
                Assert.That(_source.IsActive, Is.True);

                if (cancelWhileFar)
                {
                    _realm.Demanifest(ghost);
                }

                // Move only the reference to bring the same tracked source back into range.
                frame.Position = new Double3(30, 40, 0);
                _realm.Update();
                Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.True);
                View restored = ActiveView(ghost);
                if (cancelWhileFar)
                {
                    Assert.That(restored, Is.Null);
                }
                else
                {
                    Assert.That(restored, Is.Not.Null);
                    Assert.That(restored, Is.Not.SameAs(firstView));
                    Assert.That(restored.Ghost, Is.SameAs(ghost));
                }

                Assert.That(arrivals, Is.EqualTo(1));
                Assert.That(departures, Is.Zero);
                AssertPosition(ghost.transform.position, new Vector3(0, 0.001f, 0));
            }
        }

        /// <summary>Realm-wide distance bypass keeps distant poses raw across Anchors, preserves reference processing, and resumes from fresh filter state at the inclusive boundary.</summary>
        /// <param name="geographic">True for metre distances in ECEF; false for large Cartesian source coordinates.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void RealmProcessingDistance_BypassesDistantMotionAndRecoversAtTheBoundary(bool geographic)
        {
            RegisterView();
            Assert.That(_realm.PoseProcessingDistance, Is.Null);
            _realm.PoseProcessingDistance = 100;
            foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => _realm.PoseProcessingDistance = invalid);
                Assert.That(_realm.PoseProcessingDistance, Is.EqualTo(100));
            }

            // The predicted reference lies outside its own processing radius; following must still process it.
            Double3 origin = geographic ? new GeoPosition(52, 13, 1000).ToEarthCentered() : new Double3(1e12, -1e12, 1e12);
            Timestamp time = new Timestamp(10, 0);
            TestGhost reference = _source.Publish("processing reference");
            Spatial referenceSpatial = reference.gameObject.AddComponent<Spatial>();
            SetDistancePose(referenceSpatial, origin, Quaternion.Euler(10, 25, 30), time, geographic);
            Extrapolation referencePrediction = reference.gameObject.AddComponent<Extrapolation>();
            referencePrediction.MaximumExtrapolation = 0.1f;
            if (geographic)
            {
                referencePrediction.GetComponent<Spatial>().SetEarthCenteredVelocity(new Double3(2000, 0, 0), time);
            }
            else
            {
                referencePrediction.GetComponent<Spatial>().SetCartesianVelocity(new Double3(2000, 0, 0), time);
            }
            referencePrediction.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0.5, 0, 0), sampleTime: time);
            reference.gameObject.AddComponent<Smoothing>().PositionHalfLife = 10;
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = geographic ? ReferenceSpace.Geographic : ReferenceSpace.Cartesian,
                FollowedGhost = reference.Key,
                PredictionTime = 0.1f,
                UnityPosition = new Vector3(50, 20, -30),
                UnityRotation = Quaternion.Euler(5, 40, -10),
                MaxDistance = 2000
            };
            _realm.ReferenceFrame = frame;
            _realm.Update();
            Assert.That(frame.DistanceTo(referenceSpatial.Position), Is.GreaterThan(150));
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertRotation(reference.transform.rotation, frame.UnityRotation);

            TestSource otherSource = new TestSource();
            Anchor otherAnchor = _realm.GetOrCreateAnchor("second processing anchor", otherSource);
            Spatial distant = _source.Publish("distant processing").gameObject.AddComponent<Spatial>();
            Spatial control = otherSource.Publish("shared processing control").gameObject.AddComponent<Spatial>();
            DistanceExtrapolation limitedModel = new DistanceExtrapolation();
            DistanceExtrapolation controlModel = new DistanceExtrapolation();
            Spatial[] poses = { distant, control };
            for (int index = 0; index < poses.Length; index++)
            {
                SetDistancePose(poses[index], frame.Position + new Double3(500, 0, 0), Quaternion.identity, time, geographic);
                Extrapolation prediction = poses[index].gameObject.AddComponent<Extrapolation>();
                prediction.Strategy = index == 0 ? limitedModel : controlModel;
                prediction.PredictionTime = 0.1f;
                prediction.MaximumExtrapolation = 0.1f;
                if (geographic)
                {
                    prediction.GetComponent<Spatial>().SetEarthCenteredVelocity(new Double3(200, 0, 0), time);
                }
                else
                {
                    prediction.GetComponent<Spatial>().SetCartesianVelocity(new Double3(200, 0, 0), time);
                }
                prediction.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, 1), sampleTime: time);
                Smoothing smoothing = poses[index].gameObject.AddComponent<Smoothing>();
                smoothing.PositionHalfLife = 10;
                smoothing.RotationHalfLife = 10;
            }
            _realm.Update();
            Assert.That(distant.TryGetObservedUnityPose(out Pose observed), Is.True);
            AssertPosition(distant.transform.position, observed.position, 0.01f);
            AssertRotation(distant.transform.rotation, observed.rotation);
            Assert.That(limitedModel.Calls, Is.Zero, "Distant entities must not execute the prediction model.");
            AssertPosition(control.transform.position, distant.transform.position, 0.01f);
            Assert.That(controlModel.Calls, Is.Zero, "The Realm policy must also bypass prediction in another Anchor.");
            View view = _realm.Manifest(distant.GetComponent<TestGhost>());
            Assert.That(view, Is.Not.Null);

            // New distant packets bypass both buffered playback and the very slow filters.
            _realm.InterpolationDelay = 0.15f;
            time = new Timestamp(11, 0);
            SetDistancePose(distant, frame.Position + new Double3(700, 0, 0), Quaternion.Euler(30, 80, 10), time, geographic);
            _realm.Update();
            Assert.That(distant.TryGetObservedUnityPose(out observed), Is.True);
            AssertPosition(distant.transform.position, observed.position, 0.01f);
            AssertRotation(distant.transform.rotation, observed.rotation);
            Assert.That(limitedModel.Calls, Is.Zero);
            Assert.That(ActiveView(distant.GetComponent<TestGhost>()), Is.SameAs(view));
            Assert.That(distant.IsInRange, Is.True);

            // The raw point exactly on the boundary is processed even if prediction places it farther out.
            _realm.InterpolationDelay = 0;
            time = new Timestamp(12, 0);
            Double3 boundary = frame.Position + new Double3(100, 0, 0);
            SetDistancePose(distant, boundary, Quaternion.Euler(0, 50, 0), time, geographic);
            for (int pass = 0; pass < 2; pass++)
            {
                _realm.Update();
                Assert.That(limitedModel.Calls, Is.GreaterThan(0));
                Double3 actual = frame.ToSimulationPosition(distant.transform.position);
                double duration = Math.Min(0.1d, Math.Max(0d, _realm.ProjectedTime.Value.ElapsedSince(time)));
                Assert.That(Double3.Distance(actual, boundary + new Double3(200d * duration, 0, 0)), Is.LessThan(0.01));
            }

            // Leaving again immediately discards packet correction, smoothing and settings-transition offsets.
            limitedModel.Calls = 0;
            SetDistancePose(distant, frame.Position + new Double3(100.01, 0, 0), Quaternion.Euler(0, -60, 0), new Timestamp(13, 0), geographic);
            _realm.Update();
            Assert.That(distant.TryGetObservedUnityPose(out observed), Is.True);
            AssertPosition(distant.transform.position, observed.position, 0.01f);
            AssertRotation(distant.transform.rotation, observed.rotation);
            Assert.That(limitedModel.Calls, Is.Zero);
            Assert.That(distant.GetComponent<Extrapolation>().enabled, Is.True);
            Assert.That(distant.GetComponent<Spatial>().HasVelocity, Is.True);
            Assert.That(distant.GetComponent<Smoothing>().PositionHalfLife, Is.EqualTo(10));
            Assert.That(otherAnchor.Realm.PoseProcessingDistance, Is.EqualTo(100));
            AssertPosition(reference.transform.position, frame.UnityPosition);

            _realm.PoseProcessingDistance = null;
            _realm.Update();
            Assert.That(limitedModel.Calls, Is.GreaterThan(0));
            Assert.That(controlModel.Calls, Is.GreaterThan(0));
            double sharedDuration = Math.Min(0.1d, Math.Max(0d, _realm.ProjectedTime.Value.ElapsedSince(new Timestamp(13, 0))));
            Assert.That(Double3.Distance(frame.ToSimulationPosition(distant.transform.position),
                distant.Position + new Double3(200d * sharedDuration, 0, 0)), Is.LessThan(0.01));

            if (!geographic)
            {
                // Without a reference, source zero defines the radius rather than the Anchor scene transform.
                _realm.PoseProcessingDistance = 100;
                _realm.ReferenceFrame = null;
                limitedModel.Calls = 0;
                _realm.Update();
                Assert.That(limitedModel.Calls, Is.Zero);
                Assert.That(distant.TryGetObservedUnityPose(out observed), Is.True);
                AssertPosition(distant.transform.position, observed.position);
            }
            _realm.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _realm.PoseProcessingDistance = 100);
        }

        /// <summary>
        /// A distant entity remains tracked without writing enormous coordinates into its Transform.
        /// </summary>
        /// <param name="useReference">Whether to begin with an explicit reference frame.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void FarEntity_RetainsFiniteNearbyTransformUntilInRange(bool useReference)
        {
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 500 };
            if (useReference)
            {
                _realm.ReferenceFrame = frame;
            }

            TestGhost ghost = _source.PublishPosition("remote", new Double3(1e100, 0, 0));
            Vector3 retainedPosition = new Vector3(1, 2, 3);
            ghost.transform.position = retainedPosition;
            _realm.Update();

            AssertPosition(ghost.transform.position, retainedPosition);
            Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.False);
            Assert.That(_realm.Manifest(ghost), Is.Null);
            Assert.That(_realm.Query().Count, Is.EqualTo(1));

            frame.Position = new Double3(1e100, 0, 0);
            _realm.ReferenceFrame = frame;
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero);
            Assert.That(ActiveView(ghost), Is.Not.Null);
        }

        /// <summary>
        /// Following a key waits for its first valid position and resolves a later source in the same update.
        /// </summary>
        [Test]
        public void FollowedGhost_WaitsForPositionThenResolvesDuringPublicationUpdate()
        {
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = new Key("simulation", SpatialKind, "reference") };
            _realm.ReferenceFrame = frame;
            TestGhost remote = _source.PublishPosition("remote", new Double3(1020, 0, 0));
            _source.PublishRotation("reference", Quaternion.identity);
            _realm.Update();
            Assert.That(frame.HasPosition, Is.False);
            Assert.That(frame.IsReferenceAvailable, Is.False);
            Assert.That(_realm.Manifest(remote), Is.Null);

            _source.NextUpdate = () => _source.PublishPosition("reference", new Double3(1000, 0, 0));
            _realm.Update();
            Assert.That(frame.HasPosition, Is.True);
            Assert.That(frame.IsReferenceAvailable, Is.True);
            AssertPosition(remote.transform.position, new Vector3(20, 0, 0));
            Assert.That(ActiveView(remote), Is.Not.Null);
        }

        /// <summary>
        /// Loss freezes the cached reference pose; recreation of the same identity restores following.
        /// </summary>
        [Test]
        public void FollowedGhost_LossFreezesPoseAndIdentityRecreationReacquiresIt()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                FollowedGhost = new Key("simulation", SpatialKind, "reference"),
                UnityPosition = new Vector3(2, 0, 3)
            };
            _realm.ReferenceFrame = frame;
            TestGhost reference = _source.PublishPosition("reference", new Double3(1000, 0, 0));
            _source.PublishRotation("reference", Quaternion.Euler(0, 90, 0));
            TestGhost remote = _source.PublishPosition("remote", new Double3(1000, 0, 20));
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.True);
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertRotation(reference.transform.rotation, frame.UnityRotation);
            Vector3 retainedPosition = remote.transform.position;
            Quaternion retainedRotation = remote.transform.rotation;

            _source.RemoveEntity("reference");
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            Assert.That(frame.HasPosition, Is.True);
            AssertPosition(remote.transform.position, retainedPosition);
            AssertRotation(remote.transform.rotation, retainedRotation);

            TestGhost replacement = _source.PublishPosition("reference", new Double3(1000, 0, 2));
            _source.PublishRotation("reference", Quaternion.identity);
            _realm.Update();
            Assert.That(replacement, Is.Not.SameAs(reference));
            Assert.That(frame.IsReferenceAvailable, Is.True);
            AssertPosition(remote.transform.position, frame.UnityPosition + new Vector3(0, 0, 18));
        }

        /// <summary>
        /// Root and view activation observe the final reference after all sources have published.
        /// </summary>
        [Test]
        public void Projection_PrecedesRootAndViewActivationAcrossSources()
        {
            RegisterView(true);
            TestSource referenceSource = new TestSource();
            _anchor.AddDetector(referenceSource);
            _realm.ReferenceFrame = new ReferenceFrame
            {
                FollowedGhost = new Key("simulation", SpatialKind, "reference")
            };
            TestGhost remote = null;
            EnableProbe rootProbe = null;
            _source.NextUpdate = () =>
            {
                remote = _source.PublishPosition("remote", new Double3(1000, 0, 20));
                rootProbe = remote.gameObject.AddComponent<EnableProbe>();
                _realm.Manifest(remote);
            };
            referenceSource.NextUpdate = () => referenceSource.PublishPosition("reference", new Double3(1000, 0, 0));
            _realm.Update();

            Assert.That(rootProbe.EnableCount, Is.EqualTo(1));
            AssertPosition(rootProbe.PositionOnEnable, new Vector3(0, 0, 20));
            View view = ActiveView(remote);
            Assert.That(view, Is.Not.Null);
            EnableProbe viewProbe = view.GetComponent<EnableProbe>();
            Assert.That(viewProbe.EnableCount, Is.EqualTo(1));
            AssertPosition(viewProbe.PositionOnEnable, new Vector3(0, 0, 20));
        }

        /// <summary>
        /// Spatial suppression preserves application-disabled geometry, captures newly added inactive children, survives hierarchy toggles and restores owned states on recovery, disable or removal.
        /// </summary>
        [Test]
        public void RangeSuppression_RestoresOriginalRendererAndColliderStates()
        {
            // Start with some renderers and colliders enabled and others disabled. Only restore those hidden by Emas.
            RegisterView();
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 10 };
            TestGhost ghost = _source.PublishPosition("remote", new Double3(5, 0, 0));
            MeshRenderer visibleRenderer = ghost.gameObject.AddComponent<MeshRenderer>();
            BoxCollider activeCollider = ghost.gameObject.AddComponent<BoxCollider>();
            GameObject child = new GameObject("deliberately disabled geometry");
            child.transform.SetParent(ghost.transform, false);
            MeshRenderer disabledRenderer = child.AddComponent<MeshRenderer>();
            BoxCollider disabledCollider = child.AddComponent<BoxCollider>();
            disabledRenderer.enabled = false;
            disabledCollider.enabled = false;
            _realm.Update();
            Assert.That(_realm.Manifest(ghost), Is.Not.Null);

            _source.PublishPosition("remote", new Double3(50, 0, 0));
            _realm.Update();
            Assert.That(visibleRenderer.enabled, Is.False);
            Assert.That(activeCollider.enabled, Is.False);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
            Assert.That(ghost.gameObject.activeInHierarchy, Is.True);

            // Also hide components added while outside range, even on inactive children.
            GameObject lateChild = new GameObject("geometry added while suppressed");
            lateChild.transform.SetParent(ghost.transform, false);
            lateChild.SetActive(false);
            MeshRenderer lateRenderer = lateChild.AddComponent<MeshRenderer>();
            BoxCollider lateCollider = lateChild.AddComponent<BoxCollider>();
            _realm.Update();
            Assert.That(lateRenderer.enabled, Is.False);
            Assert.That(lateCollider.enabled, Is.False);

            ghost.gameObject.SetActive(false);
            Assert.That(visibleRenderer.enabled, Is.False, "A hierarchy toggle does not release spatial suppression.");
            ghost.gameObject.SetActive(true);
            Assert.That(lateRenderer.enabled, Is.False);
            UnityEngine.Object.DestroyImmediate(lateCollider);
            _realm.Update();

            // On return to range, restore only components that Emas disabled.
            _source.PublishPosition("remote", new Double3(6, 0, 0));
            _realm.Update();
            Assert.That(visibleRenderer.enabled, Is.True);
            Assert.That(activeCollider.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
            Assert.That(lateRenderer.enabled, Is.True, "Recovery restores owned component state even on inactive children.");
            Assert.That(ActiveView(ghost), Is.Not.Null);

            _source.PublishPosition("remote", new Double3(50, 0, 0));
            _realm.Update();
            // Removing the range reference restores components hidden by Emas. Keep user-disabled components disabled.
            _realm.ReferenceFrame = null;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(50, 0, 0));
            Assert.That(visibleRenderer.enabled, Is.True);
            Assert.That(activeCollider.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
            Assert.That(ActiveView(ghost), Is.Not.Null);

            _realm.ReferenceFrame = new ReferenceFrame { MaxDistance = 10 };
            _realm.Update();
            Spatial spatial = ghost.GetComponent<Spatial>();
            // Disabling or destroying Spatial restores components it hid. Enabling it checks the range again.
            spatial.enabled = false;
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(visibleRenderer.enabled, Is.True);
            Assert.That(activeCollider.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            spatial.enabled = true;
            _realm.Update();
            Assert.That(visibleRenderer.enabled, Is.False);
            UnityEngine.Object.DestroyImmediate(spatial);
            Assert.That(visibleRenderer.enabled, Is.True);
            Assert.That(activeCollider.enabled, Is.True);
            Assert.That(lateRenderer.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
            _realm.Update();
            Assert.That(ActiveView(ghost), Is.Not.Null);
        }

        /// <summary>Position and rotation smooth at independent rates without velocity or game time, and changing either control preserves the other channel's history.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator Smoothing_WithoutVelocityFiltersIndependentChannelsAndCanBeResetOrDisabled()
        {
            // Start both filters at the first sample. Reject invalid half-lives and keep the previous settings.
            TestGhost ghost = _source.PublishPosition("smooth", new Double3(1e9, 0, 0));
            Spatial spatial = ghost.GetComponent<Spatial>();
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 2;
            smoothing.RotationHalfLife = 0.5f;
            Quaternion initialRotation = Quaternion.Euler(0, 350, 0);
            spatial.SetSourceRotation(initialRotation);
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1e9, 0, 0) };
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero);
            AssertRotation(ghost.transform.rotation, initialRotation);
            Assert.That(ghost.GetComponent<Extrapolation>(), Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.PositionHalfLife = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.PositionHalfLife = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.RotationHalfLife = float.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.RotationHalfLife = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.RotationHalfLife = -1);
            Assert.That(smoothing.PositionHalfLife, Is.EqualTo(2));
            Assert.That(smoothing.RotationHalfLife, Is.EqualTo(0.5f));

            float timeScale = Time.timeScale;
            try
            {
                // Smoothing must keep using real time when game time is paused. Rotation must take the shortest path.
                Time.timeScale = 0;
                double positionStarted = Time.realtimeSinceStartupAsDouble;
                Double3 input = new Double3(1e9 + 10, 2, 0);
                Quaternion targetRotation = Quaternion.Euler(0, 10, 0);
                spatial.SetCartesianPosition(input);
                spatial.SetSourceRotation(targetRotation);
                spatial.Detach(); // A detached attachment reader can publish this state on every update.
                yield return AdvanceSmoothing(0.08);
                Assert.That(ghost.transform.position.x, Is.GreaterThan(0).And.LessThan(10));
                Assert.That(ghost.transform.position.y, Is.GreaterThan(0).And.LessThan(2));
                Assert.That(Quaternion.Angle(initialRotation, ghost.transform.rotation), Is.GreaterThan(0).And.LessThan(20));
                Assert.That(ghost.transform.eulerAngles.y, Is.GreaterThan(350).Or.LessThan(10), "Rotation crosses zero along the shortest arc.");
                double positionProgress = ghost.transform.position.x / 10d;
                double expectedPositionProgress = 1d - Math.Pow(0.5d,
                    (Time.realtimeSinceStartupAsDouble - positionStarted) / smoothing.PositionHalfLife);
                Assert.That(positionProgress, Is.EqualTo(expectedPositionProgress).Within(0.002),
                    "Half-life halves the remaining correction error independently of game time.");
                double expectedRotationProgress = 1d - Math.Pow(1d - positionProgress,
                    smoothing.PositionHalfLife / smoothing.RotationHalfLife);
                Assert.That(Quaternion.Angle(initialRotation, ghost.transform.rotation),
                    Is.EqualTo(20d * expectedRotationProgress).Within(0.05), "Each channel uses its own time constant.");
                Assert.That(spatial.Position, Is.EqualTo(input));
                AssertRotation(spatial.Rotation, targetRotation);

                // Changing position or rotation smoothing must keep the other filter's state.
                smoothing.RotationHalfLife = 0;
                _realm.Update();
                yield return AdvanceSmoothing(0.3);
                AssertRotation(ghost.transform.rotation, targetRotation);
                Assert.That(ghost.transform.position.x, Is.LessThan(10), "Changing rotation smoothing keeps position history.");
                smoothing.RotationHalfLife = 2;
                _realm.Update();
                Quaternion nextRotation = Quaternion.Euler(0, 90, 0);
                spatial.SetSourceRotation(nextRotation);
                smoothing.PositionHalfLife = 0;
                _realm.Update();
                yield return AdvanceSmoothing(0.3);
                AssertPosition(ghost.transform.position, new Vector3(10, 2, 0));
                Assert.That(Quaternion.Angle(targetRotation, ghost.transform.rotation), Is.GreaterThan(0).And.LessThan(80),
                    "Disabling position smoothing keeps rotation smoothing active.");

                // Resetting must clear the remaining smoothing offset and show the current input immediately.
                spatial.ResetPresentation();
                _realm.Update();
                AssertPosition(ghost.transform.position, new Vector3(10, 2, 0));
                AssertRotation(ghost.transform.rotation, nextRotation);
                spatial.SetCartesianPosition(new Double3(1e9 + 20, 2, 0));
                smoothing.PositionHalfLife = 0;
                _realm.Update();
                AssertPosition(ghost.transform.position, new Vector3(20, 2, 0));
            }
            finally
            {
                Time.timeScale = timeScale;
            }
        }

        /// <summary>SDK motion avoids smoothing lag on new observations even with extrapolation disabled; held samples never extrapolate and opposite corrections remain filtered.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator Smoothing_VelocityPredictsMotionWithoutRejectingCorrections()
        {
            // Accept a new position behind the current one, even with a strong forward velocity.
            _realm.ReferenceFrame = new ReferenceFrame { Coordinates = CoordinateSystem.NorthEastDown, Position = default(Double3) };
            TestGhost ghost = _source.PublishPosition("smooth", new Double3(10, 0, 0));
            Spatial spatial = ghost.GetComponent<Spatial>();
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.2f;
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.1f;
            spatial.SetCartesianVelocity(new Double3(8, 0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0, 0, 10), 0.02f);
            spatial.SetCartesianPosition(new Double3(-10, 2, 0));
            yield return AdvanceSmoothing(0.04);
            Assert.That(ghost.transform.position.z, Is.LessThan(10), "A correction opposite strong velocity is accepted.");
            Assert.That(ghost.transform.position.x, Is.GreaterThan(0));
            Assert.That(spatial.Position, Is.EqualTo(new Double3(-10, 2, 0)));
            Assert.That(spatial.HasVelocity, Is.True);
            Assert.That(spatial.Velocity, Is.EqualTo(new Double3(8, 0, 0)));

            // Stop predicting acceleration at the time limit, even if the same position is sent repeatedly.
            spatial.ResetPresentation();
            spatial.SetCartesianPosition(default(Double3));
            spatial.SetCartesianVelocity(new Double3(8, 0, 0));
            spatial.SetCartesianAcceleration(new Double3(4, 0, 0));
            _realm.Update();
            yield return AdvanceSmoothing(0.3);
            Assert.That(ghost.transform.position.z, Is.EqualTo(0.82f).Within(0.02f), "Extrapolation is capped independently of smoothing, including acceleration.");
            float stoppedExtrapolation = ghost.transform.position.z;
            double cachedUntil = Time.realtimeSinceStartupAsDouble + 0.08;
            do
            {
                yield return null;
                spatial.SetCartesianPosition(default(Double3));
                _realm.Update();
            }
            while (Time.realtimeSinceStartupAsDouble < cachedUntil);
            Assert.That(ghost.transform.position.z, Is.EqualTo(stoppedExtrapolation).Within(0.01f));
            Assert.That(spatial.Position, Is.EqualTo(default(Double3)), "Extrapolation never changes the SDK input.");
            Assert.That(spatial.HasAcceleration, Is.True);
            Assert.That(spatial.Acceleration, Is.EqualTo(new Double3(4, 0, 0)));
            spatial.ClearVelocity();
            yield return AdvanceSmoothing(0.04);
            Assert.That(ghost.transform.position.z, Is.LessThan(stoppedExtrapolation), "Acceleration alone does not predict travel.");
            spatial.ClearAcceleration();
            Assert.That(spatial.HasAcceleration, Is.False);
            spatial.SetCartesianVelocity(new Double3(8, 0, 0));
            UnityEngine.Object.DestroyImmediate(extrapolation);
            Assert.That(ghost.GetComponent<Extrapolation>(), Is.Null);
            Assert.That(spatial.Velocity, Is.EqualTo(new Double3(8, 0, 0)), "Removing a processing component must retain Spatial inputs.");
            yield return AssertMotionWithoutExtrapolation(ghost, spatial, smoothing);

            spatial.ClearVelocity();
            float before = ghost.transform.position.z;
            spatial.SetCartesianPosition(new Double3(-8, 0, 0));
            yield return AdvanceSmoothing(0.04);
            Assert.That(spatial.HasVelocity, Is.False);
            Assert.That(ghost.transform.position.z, Is.LessThan(before));
            spatial.SetCartesianVelocity(new Double3(8, 0, 0));
            smoothing.enabled = false;
            _realm.Update();
            yield return AdvanceSmoothing(0.3);
            AssertPosition(ghost.transform.position, new Vector3(0, 0, -8));
        }

        /// <summary>A geographic reference shares independently smoothed channels with its root; changing reference rotation treatment preserves coherent targets and attached parts through its transition.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator Smoothing_GeographicReferenceAndAttachmentsSharePresentationPose()
        {
            // The followed root, a separate target and an attached part must all use the same smoothed reference.
            GeoPosition origin = new GeoPosition(52.520008, 13.404954, 40);
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                FollowedGhost = new Key("simulation", SpatialKind, "reference")
            };
            _realm.ReferenceFrame = frame;
            TestGhost reference = _source.PublishEarthCenteredPosition("reference", origin.ToEarthCentered());
            Spatial spatial = reference.GetComponent<Spatial>();
            Smoothing smoothing = reference.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 2;
            smoothing.RotationHalfLife = 2;
            spatial.SetGeographicRotation(0, 0, 0);
            reference.gameObject.AddComponent<Extrapolation>();
            spatial.SetEarthCenteredVelocity(new Double3(0, 0, 0));
            TestGhost target = _source.PublishEarthCenteredPosition("target", new GeoPosition(52.520108, 13.404954, 40).ToEarthCentered());
            target.gameObject.AddComponent<Smoothing>().PositionHalfLife = 2;
            TestGhost part = _source.Publish("part");
            Spatial partSpatial = part.gameObject.AddComponent<Spatial>();
            Smoothing partSmoothing = part.gameObject.AddComponent<Smoothing>();
            partSmoothing.PositionHalfLife = 2;
            partSmoothing.RotationHalfLife = 2;
            DistanceExtrapolation partModel = new DistanceExtrapolation();
            part.gameObject.AddComponent<Extrapolation>().Strategy = partModel;
            Timestamp ignoredPartTime = new Timestamp(999999, 0);
            partSpatial.SetEarthCenteredPosition(origin.ToEarthCentered() + new Double3(200, 0, 0), ignoredPartTime);
            partSpatial.SetSourceRotation(Quaternion.Euler(90, 0, 0), ignoredPartTime);
            partSpatial.SetEarthCenteredVelocity(new Double3(1000000, 0, 0), ignoredPartTime);
            partSpatial.SetBodyAngularVelocity(new Double3(10, 20, 30), CoordinateSystem.Unity, ignoredPartTime);
            Vector3 offset = new Vector3(2, 0, 0);
            partSpatial.Attach(reference.Key, offset);
            _realm.Update();
            Assert.That(_realm.ProjectedTime, Is.Null, "An attached part's unused absolute timestamps must not drive the Realm clock.");

            Double3 input = new GeoPosition(52.520058, 13.404954, 40).ToEarthCentered();
            spatial.SetEarthCenteredPosition(input);
            spatial.SetGeographicRotation(90, 0, 0);
            yield return AdvanceSmoothing(0.08);
            Assert.That(Double3.Distance(frame.Position, origin.ToEarthCentered()), Is.GreaterThan(0));
            Assert.That(Double3.Distance(frame.Position, input), Is.GreaterThan(0));
            Assert.That(spatial.Position, Is.EqualTo(input));
            Assert.That(Quaternion.Angle(Quaternion.identity, frame.Rotation), Is.GreaterThan(0).And.LessThan(90));
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertRotation(reference.transform.rotation, frame.UnityRotation);
            Assert.That(frame.TryToUnityPosition(target.GetComponent<Spatial>().Position, out Vector3 targetPosition), Is.True);
            AssertPosition(target.transform.position, targetPosition);
            AssertPosition(part.transform.position, reference.transform.position + reference.transform.rotation * offset);

            // Change one filter while the other continues smoothing.
            smoothing.RotationHalfLife = 0;
            spatial.SetGeographicRotation(180, 0, 0);
            _realm.Update();
            yield return AdvanceSmoothing(0.3);
            AssertRotation(frame.Rotation, spatial.Rotation);
            Assert.That(Double3.Distance(frame.Position, input), Is.GreaterThan(0), "Changing reference rotation keeps position smoothing history.");
            Assert.That(frame.TryToUnityPosition(target.GetComponent<Spatial>().Position, out targetPosition), Is.True);
            AssertPosition(target.transform.position, targetPosition);
            AssertPosition(part.transform.position, reference.transform.position + reference.transform.rotation * offset);

            frame.FollowRotation = false;
            smoothing.RotationHalfLife = 2;
            _realm.Update();
            spatial.SetGeographicRotation(270, 0, 0);
            yield return AdvanceSmoothing(0.04);
            ReferenceFrame cardinalFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, Position = frame.Position };
            Assert.That(cardinalFrame.TryToUnityPosition(target.GetComponent<Spatial>().Position, out targetPosition), Is.True);
            AssertPosition(target.transform.position, targetPosition, 0.001f);
            frame.FollowRotation = true;

            frame.UnityPosition = new Vector3(100, 2, 3);
            frame.UnityRotation = Quaternion.Euler(10, 20, 30);
            _realm.Update();
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertRotation(reference.transform.rotation, frame.UnityRotation);
            AssertPosition(part.transform.position, reference.transform.position + reference.transform.rotation * offset);

            Assert.That(partModel.Calls, Is.Zero, "Attached parts must not evaluate prediction or packet-correction previews.");
            Assert.That(partSpatial.HasVelocity && partSpatial.HasAngularVelocity, Is.True);
            partSpatial.ClearVelocity();
            partSpatial.ClearAngularVelocity();
            partSpatial.SetEarthCenteredPosition(input);
            partSpatial.Detach();
            _realm.Update();
            Assert.That(frame.TryToUnityPosition(input, out Vector3 releasePosition), Is.True);
            AssertPosition(part.transform.position, releasePosition);
            Assert.That(partModel.Calls, Is.GreaterThan(0), "Detaching resumes motion processing from current Spatial inputs.");
        }

        /// <summary>Views requested by arrival observers use one finalized predicted pose for the followed reference, parent and attached children; standalone requests still project fresh input.</summary>
        [Test]
        public void Manifest_FromArrivalObserversPreservesSharedAttachmentPresentation()
        {
            // Request views in arrival callbacks. All roots must already be at their final positions for this update.
            RegisterView(observeEnable: true);
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = new Key("simulation", SpatialKind, "reference") };
            _realm.ReferenceFrame = frame;
            TestGhost reference = null;
            TestGhost parent = null;
            TestGhost left = null;
            TestGhost right = null;
            Vector3 leftOffset = new Vector3(-0.085f, -0.09f, 0.02f);
            Vector3 rightOffset = new Vector3(0.085f, -0.09f, 0.02f);
            int arrivals = 0;
            bool presentationChanged = false;
            bool sourceRequestReturnedView = false;
            using (_realm.Query().OnAvailable(ghost =>
            {
                Double3 referencePosition = frame.Position;
                Vector3 parentPosition = parent.transform.position;
                Vector3 leftPosition = left.transform.position;
                Vector3 rightPosition = right.transform.position;
                _realm.Manifest(ghost);
                presentationChanged |= frame.Position != referencePosition
                    || !parent.transform.position.Equals(parentPosition)
                    || !left.transform.position.Equals(leftPosition)
                    || !right.transform.position.Equals(rightPosition);
                arrivals++;
            }))
            {
                _source.NextUpdate = () =>
                {
                    reference = _source.PublishPosition("reference", new Double3(1000, 0, 0));
                    reference.gameObject.AddComponent<Extrapolation>().GetComponent<Spatial>().SetCartesianVelocity(new Double3(2, 0, 0));
                    reference.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.08f;
                    left = _source.Publish("left");
                    left.gameObject.AddComponent<Spatial>().Attach(new Key("simulation", SpatialKind, "parent"), leftOffset);
                    right = _source.Publish("right");
                    right.gameObject.AddComponent<Spatial>().Attach(new Key("simulation", SpatialKind, "parent"), rightOffset);
                    parent = _source.PublishPosition("parent", new Double3(1010, 0, 0));
                    parent.GetComponent<Spatial>().SetSourceRotation(Quaternion.Euler(10, 35, 7));
                    parent.gameObject.AddComponent<Extrapolation>().GetComponent<Spatial>().SetCartesianVelocity(new Double3(8, 0, 0));
                    Smoothing smoothing = parent.gameObject.AddComponent<Smoothing>();
                    smoothing.PositionHalfLife = 0.08f;
                    smoothing.RotationHalfLife = 0.04f;
                    sourceRequestReturnedView = _realm.Manifest(parent) != null;
                };
                _realm.Update();
            }

            Assert.That(sourceRequestReturnedView, Is.False, "Source-phase requests still wait for complete projection.");
            Assert.That(arrivals, Is.EqualTo(4));
            Assert.That(presentationChanged, Is.False,
                "Creating views during update notifications must not advance individual roots beyond their shared projection.");
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertPosition(left.transform.position, parent.transform.position + parent.transform.rotation * leftOffset);
            AssertPosition(right.transform.position, parent.transform.position + parent.transform.rotation * rightOffset);
            foreach (TestGhost ghost in new[] { reference, parent, left, right })
            {
                Assert.That(ActiveView(ghost), Is.Not.Null);
                AssertPosition(ActiveView(ghost).GetComponent<EnableProbe>().PositionOnEnable, ghost.transform.position);
            }

            View existingView = ActiveView(parent);
            parent.GetComponent<Extrapolation>().enabled = false;
            parent.GetComponent<Smoothing>().enabled = false;
            Spatial parentSpatial = parent.GetComponent<Spatial>();
            parentSpatial.SetCartesianPosition(new Double3(1020, 0, 0));
            parentSpatial.ResetPresentation(); // This standalone request deliberately teleports to the fresh pose.
            Assert.That(_realm.Manifest(parent), Is.SameAs(existingView));
            Assert.That(frame.TryToUnityPosition(parentSpatial.Position, out Vector3 immediatePosition), Is.True);
            AssertPosition(parent.transform.position, immediatePosition);
        }

        /// <summary>Presentation-only range culling retains filter history; out-of-range source observations and re-enabled roots recover from fresh inputs.</summary>
        [Test]
        public void Smoothing_RangeRecoveryAndReenableDiscardOldHistory()
        {
            // Moving the reference can put the smoothed pose outside range while the raw position stays inside.
            ReferenceFrame frame = new ReferenceFrame { Position = default(Double3), MaxDistance = 10 };
            _realm.ReferenceFrame = frame;
            TestGhost ghost = _source.PublishPosition("smooth", new Double3(5, 0, 0));
            Spatial spatial = ghost.GetComponent<Spatial>();
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 10;
            _realm.Update();

            frame.Position = new Double3(20, 0, 0);
            spatial.SetCartesianPosition(new Double3(20, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.False,
                "The raw observation is eligible while the slower presentation remains outside the new frame's range.");
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.False,
                "Culling only the filtered pose must not reset history and snap it into range on the next update.");
            frame.MaxDistance = 100;
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(ghost.transform.position.x, Is.LessThan(-10),
                "Widening the range must reveal the retained filtered pose, not a reset at the latest raw position.");
            Assert.That(spatial.Position, Is.EqualTo(new Double3(20, 0, 0)));

            // When raw input moves outside range, clear the filter. Returning to range must show the new input immediately.
            frame.Position = default(Double3);
            frame.MaxDistance = 10;
            spatial.SetCartesianPosition(new Double3(100, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.False);
            spatial.SetCartesianPosition(new Double3(7, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.True);
            AssertPosition(ghost.transform.position, new Vector3(7, 0, 0));

            // Re-enabling must start from current source input, not the old displayed pose.
            spatial.enabled = false;
            ghost.transform.position = Vector3.zero;
            spatial.SetCartesianPosition(new Double3(8, 0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero);
            spatial.enabled = true;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(8, 0, 0));
        }

        /// <summary>Singer acceleration follows an independently integrated decay curve, retains angular motion and raw observations, and approaches constant velocity/acceleration without numerical cancellation.</summary>
        [Test]
        public void Singer_DecaysAccelerationAndPreservesStableLimitingCases()
        {
            // Advance the clock past the prediction limit so the expected prediction time stays fixed.
            Double3 origin = new Double3(1e9, 0, 0);
            _realm.ReferenceFrame = new ReferenceFrame { Position = origin };
            TestGhost ghost = _source.Publish("Singer mean");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            SingerExtrapolation strategy = new SingerExtrapolation { ManeuverTimeConstant = 0.2f };
            extrapolation.Strategy = strategy;
            extrapolation.MaximumExtrapolation = 0.4f;
            Timestamp time = new Timestamp(1000, 0);
            Double3 velocity = new Double3(8, 3, -1);
            Double3 acceleration = new Double3(4, -2, 1);
            Quaternion attitude = Quaternion.Euler(12, 25, -15);
            spatial.SetCartesianPosition(origin, time);
            spatial.SetSourceRotation(attitude, time);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(velocity, time);
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(acceleration, time);
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI / 2, 0), CoordinateSystem.Unity, time);
            _source.Publish("Singer mean clock").gameObject.AddComponent<Spatial>().SetCartesianPosition(origin, new Timestamp(1002, 0));
            _realm.Update();

            // Sum acceleration in many small steps to check the strategy's formula independently.
            double horizon = extrapolation.MaximumExtrapolation;
            const int steps = 4000;
            double step = horizon / steps;
            double weight = 0;
            for (int index = 0; index < steps; index++)
            {
                double seconds = (index + 0.5d) * step;
                weight += (horizon - seconds) * Math.Exp(-seconds / strategy.ManeuverTimeConstant) * step;
            }

            Double3 expected = velocity * horizon + acceleration * weight;
            AssertPosition(ghost.transform.position, new Vector3((float)expected.X, (float)expected.Y, (float)expected.Z), 0.0001f);
            AssertRotation(ghost.transform.rotation, attitude * Quaternion.Euler(0, (float)(90 * horizon), 0));
            Vector3 held = ghost.transform.position;
            _realm.Update();
            AssertPosition(ghost.transform.position, held);
            Assert.That(spatial.Position, Is.EqualTo(origin));
            Assert.That(spatial.PositionTime, Is.EqualTo(time));
            Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(acceleration));

            // Check that the same input approaches constant acceleration and constant velocity at the two limits.
            strategy.ManeuverTimeConstant = float.MaxValue;
            _realm.Update();
            expected = velocity * horizon + acceleration * (0.5d * horizon * horizon);
            AssertPosition(ghost.transform.position, new Vector3((float)expected.X, (float)expected.Y, (float)expected.Z), 0.0001f);
            strategy.ManeuverTimeConstant = float.Epsilon;
            _realm.Update();
            expected = velocity * horizon;
            AssertPosition(ghost.transform.position, new Vector3((float)expected.X, (float)expected.Y, (float)expected.Z), 0.0001f);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.ManeuverTimeConstant = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.ManeuverTimeConstant = float.NaN);
            Assert.That(strategy.ManeuverTimeConstant, Is.EqualTo(float.Epsilon));
        }

        /// <summary>Singer uses only motion available at the buffered geographic position time; clearing optional channels restores velocity-only motion or a held pose.</summary>
        [Test]
        public void Singer_RespectsIndependentChannelsAndOptionalMotionInGeographicPlayback()
        {
            // Keep a future acceleration sample, but do not use it to predict from an older position.
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            _realm.ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
            _realm.InterpolationDelay = 0.1f;
            TestGhost ghost = _source.Publish("Singer channels");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.Strategy = new SingerExtrapolation { ManeuverTimeConstant = 0.1f };
            extrapolation.MaximumExtrapolation = 0.2f;
            Timestamp time = new Timestamp(1000, 0);
            Timestamp later = new Timestamp(1002, 0);
            spatial.SetGeographicPosition(origin, time);
            extrapolation.GetComponent<Spatial>().SetGeographicVelocity(new Double3(10, 0, 0), origin, time);
            extrapolation.GetComponent<Spatial>().SetGeographicAcceleration(new Double3(4, 0, 0), origin, later);
            _source.Publish("Singer channels clock").gameObject.AddComponent<Spatial>().SetGeographicPosition(origin, new Timestamp(1003, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(2, 0, 0), 0.001f);

            spatial.SetGeographicPosition(origin, later);
            extrapolation.GetComponent<Spatial>().SetGeographicVelocity(new Double3(10, 0, 0), origin, later);
            _realm.Update();
            Assert.That(ghost.transform.position.x, Is.InRange(2.045f, 2.046f));
            Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(origin.ToEarthCenteredVector(new Double3(4, 0, 0))));
            extrapolation.GetComponent<Spatial>().ClearAcceleration();
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(2, 0, 0), 0.001f);
            extrapolation.GetComponent<Spatial>().ClearVelocity();
            extrapolation.GetComponent<Spatial>().SetGeographicAcceleration(new Double3(4, 0, 0), origin);
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
            Assert.That(extrapolation.GetComponent<Spatial>().HasAcceleration, Is.True);
            Assert.That(extrapolation.GetComponent<Spatial>().HasVelocity, Is.False);
        }

        /// <summary>Rotating-body second-order motion agrees with an independent trajectory integral across Cartesian handedness, geographic attitude and ECEF body/world rates.</summary>
        /// <param name="representation">Zero for Cartesian NED, one for local geographic attitude, two for body-to-ECEF with world rates.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CoordinatedTurn_IntegratesBodyAccelerationAcrossCoordinateRepresentations(int representation)
        {
            // Send the same turning motion using three different source coordinate settings.
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = representation == 0 ? ReferenceSpace.Cartesian : ReferenceSpace.Geographic,
                Position = representation == 0 ? new Double3(1e9, 0, 0) : origin.ToEarthCentered(),
                Coordinates = CoordinateSystem.NorthEastDown
            };
            _realm.ReferenceFrame = frame;
            _realm.InterpolationDelay = representation == 1 ? 0.1f : 0;
            TestGhost ghost = _source.Publish("body second order");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.Strategy = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0 };
            extrapolation.MaximumExtrapolation = 0.4f;
            Quaternion attitude = Quaternion.Euler(15, 20, -30);
            Vector3 rate = new Vector3(0.1f, 0.6f, 0.2f);
            Vector3 bodyVelocity = new Vector3(3, 2, 120);
            Vector3 bodyAcceleration = new Vector3(0, 0, 2);
            Vector3 velocity = attitude * bodyVelocity;
            Vector3 acceleration = attitude * (bodyAcceleration + Vector3.Cross(rate, bodyVelocity));
            Timestamp time = new Timestamp(1000, 0);
            spatial.SetCartesianPosition(frame.Position, time);
            if (representation == 2)
            {
                spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(attitude), sampleTime: time);
                Vector3 worldRate = attitude * rate;
                extrapolation.GetComponent<Spatial>().SetGeographicAngularVelocity(new Double3(-worldRate.x, -worldRate.z, -worldRate.y), origin, time);
            }
            else
            {
                spatial.SetSourceRotation(frame.ToSimulationRotation(attitude), time);
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(rate.x, rate.y, rate.z), CoordinateSystem.Unity, time);
            }

            Double3 sourceVelocity = frame.ToSimulationPosition(velocity) - frame.Position;
            Double3 sourceAcceleration = frame.ToSimulationPosition(acceleration) - frame.Position;
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(sourceVelocity, time);
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(sourceAcceleration, time);
            _source.Publish("body clock").gameObject.AddComponent<Spatial>().SetCartesianPosition(frame.Position, new Timestamp(1002, 0));
            _realm.Update();

            // Sum body velocity in many small steps to get an independent expected path.
            const int steps = 4000;
            double step = extrapolation.MaximumExtrapolation / (double)steps;
            Double3 expected = default(Double3);
            for (int index = 0; index < steps; index++)
            {
                double seconds = (index + 0.5d) * step;
                Quaternion rotation = attitude * Quaternion.AngleAxis((float)(rate.magnitude * seconds * 180d / Math.PI), rate.normalized);
                Vector3 current = rotation * (bodyVelocity + bodyAcceleration * (float)seconds);
                expected += new Double3(current.x, current.y, current.z) * step;
            }

            AssertPosition(ghost.transform.position, new Vector3((float)expected.X, (float)expected.Y, (float)expected.Z), 0.004f);
            Quaternion expectedRotation = attitude * Quaternion.AngleAxis(rate.magnitude * extrapolation.MaximumExtrapolation * Mathf.Rad2Deg, rate.normalized);
            Assert.That(Quaternion.Angle(ghost.transform.rotation, expectedRotation), Is.LessThan(0.08f));
            Assert.That(spatial.Position, Is.EqualTo(frame.Position));
            Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(sourceAcceleration));
        }

        /// <summary>Missing rates use a coordinated bank turn; supplied zero overrides bank, straight second-order motion has a stable zero-rate limit, and extreme motion recovers without corrupting raw input.</summary>
        [Test]
        public void CoordinatedTurn_BankFallbackRespectsZeroOverridesAndRecovery()
        {
            // Start without rotation rates so the strategy must estimate the turn from bank and speed.
            TestGhost ghost = _source.Publish("bank turn");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            CoordinatedTurnExtrapolation strategy = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0, Gravity = 19.6133f };
            extrapolation.Strategy = strategy;
            extrapolation.MaximumExtrapolation = 0.4f;
            Timestamp time = new Timestamp(1000, 0);
            Quaternion bank = Quaternion.Euler(0, 0, -45);
            spatial.SetCartesianPosition(default(Double3), time);
            spatial.SetSourceRotation(bank, time);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(0, 0, 100), time);
            _source.Publish("bank clock").gameObject.AddComponent<Spatial>().SetCartesianPosition(default(Double3), new Timestamp(1002, 0));
            _realm.Update();
            double horizon = extrapolation.MaximumExtrapolation;
            double rate = strategy.Gravity / 100d;
            AssertPosition(ghost.transform.position, new Vector3((float)(100d * (1d - Math.Cos(rate * horizon)) / rate), 0,
                (float)(100d * Math.Sin(rate * horizon) / rate)), 0.001f);
            AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, (float)(rate * horizon * 180d / Math.PI), 0) * bank);

            // A supplied zero turn rate overrides the bank estimate and gives straight motion with acceleration.
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, time);
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(2, 3, 4), time);
            spatial.ResetPresentation();
            _realm.Update();
            Vector3 straight = new Vector3((float)(horizon * horizon), (float)(1.5d * horizon * horizon), (float)(100d * horizon + 2d * horizon * horizon));
            AssertPosition(ghost.transform.position, straight, 0.001f);
            AssertRotation(ghost.transform.rotation, bank);

            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 1e-8, 0), CoordinateSystem.Unity);
            _realm.Update();
            AssertPosition(ghost.transform.position, straight, 0.001f);
            // Very large motion must fall back safely, then recover using the valid source pose.
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(double.MaxValue, double.MaxValue, 0), CoordinateSystem.Unity);
            extrapolation.MaximumExtrapolation = 1;
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero);
            AssertRotation(ghost.transform.rotation, bank);
            extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
            strategy.UseBankAngleWhenRatesUnavailable = false;
            extrapolation.MaximumExtrapolation = 0.4f;
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, straight, 0.001f);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.Gravity = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.PacketSmoothingDuration = float.NaN);
            Assert.That(strategy.Gravity, Is.EqualTo(19.6133f));
        }

        /// <summary>Future-only attitude cannot bend an older position, while a new matching position enables body-frame coordinated turns.</summary>
        [Test]
        public void CoordinatedTurn_UsesOnlyAttitudeAvailableAtThePositionTime()
        {
            GeoPosition origin = new GeoPosition(48, 11, 1200);
            _realm.ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
            TestGhost ghost = _source.Publish("independent aircraft channels");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.Strategy = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0 };
            extrapolation.MaximumExtrapolation = 0.1f;
            spatial.SetGeographicPosition(origin, new Timestamp(1000, 0));
            spatial.SetGeographicRotation(0, 0, 45, new Timestamp(1001, 0));
            extrapolation.GetComponent<Spatial>().SetGeographicVelocity(new Double3(0, 100, 0), origin, new Timestamp(1000, 0));
            _source.Publish("independent aircraft clock").gameObject.AddComponent<Spatial>()
                .SetGeographicPosition(origin, new Timestamp(1002, 0));
            _realm.Update();
            Assert.That(ghost.transform.position.x, Is.EqualTo(0).Within(0.001f));
            Assert.That(ghost.transform.position.z, Is.EqualTo(10).Within(0.001f));
            spatial.SetGeographicPosition(origin, new Timestamp(1001, 0));
            _realm.Update();
            Assert.That(ghost.transform.position.x, Is.GreaterThan(0.04f));
            Assert.That(ghost.transform.position.z, Is.EqualTo(10).Within(0.001f));
        }

        /// <summary>Cubic packet correction retains presentation at arrival, follows a cubic curve, rebases interrupted position/quaternion corrections, ignores stale packets and resets per root with immediate, buffered or untimed input.</summary>
        /// <returns>An iterator that observes packet correction and exact convergence on unscaled time.</returns>
        [UnityTest]
        public IEnumerator CoordinatedTurn_CubicPacketCorrectionRebasesAndRemainsPerRoot()
        {
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                for (int mode = 0; mode < 3; mode++)
                {
                    yield return AssertCubicPackets(mode);
                }
            }
            finally
            {
                Time.timeScale = previousScale;
            }
        }

        /// <summary>Packets received every presentation frame still correct a stopped aircraft's position and attitude, with or without additional filtering and with timed, buffered or untimed input.</summary>
        /// <returns>An iterator that continuously publishes the stopped pose while packet correction converges.</returns>
        [UnityTest]
        public IEnumerator CoordinatedTurn_ContinuousPacketsConvergeAfterMotionStops()
        {
            // Compare roots with and without Smoothing, using timestamps, delayed playback and arrival times.
            for (int mode = 0; mode < 3; mode++)
            {
                _realm.InterpolationDelay = mode == 1 ? 0.05f : 0;
                TestGhost[] ghosts =
                {
                    _source.Publish("continuous packets " + mode),
                    _source.Publish("filtered continuous packets " + mode)
                };
                Timestamp? initialTime = mode == 2 ? (Timestamp?)null : new Timestamp(1000, 0);
                foreach (TestGhost ghost in ghosts)
                {
                    Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
                    spatial.SetCartesianPosition(default(Double3), initialTime);
                    spatial.SetSourceRotation(Quaternion.Euler(0, 0, -30), initialTime);
                    Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
                    extrapolation.Strategy = new CoordinatedTurnExtrapolation();
                    extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(0, 0, 100), initialTime);
                    extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0.2d, 0), CoordinateSystem.Unity, initialTime);
                }

                Smoothing smoothing = ghosts[1].gameObject.AddComponent<Smoothing>();
                smoothing.PositionHalfLife = 0.08f;
                smoothing.RotationHalfLife = 0.04f;
                _realm.Update();
                // Send another stationary packet each frame while the previous correction is still fading.
                double started = Time.realtimeSinceStartupAsDouble;
                Quaternion stoppedRotation = Quaternion.Euler(10, 60, -30);
                do
                {
                    yield return null;
                    double elapsed = Time.realtimeSinceStartupAsDouble - started;
                    Timestamp? time = mode == 2 ? (Timestamp?)null : Timestamp.FromSeconds(1000d + elapsed);
                    foreach (TestGhost ghost in ghosts)
                    {
                        Spatial spatial = ghost.GetComponent<Spatial>();
                        spatial.SetCartesianPosition(new Double3(10, 0, 0), time);
                        spatial.SetSourceRotation(stoppedRotation, time);
                        Extrapolation extrapolation = ghost.GetComponent<Extrapolation>();
                        extrapolation.GetComponent<Spatial>().SetCartesianVelocity(default(Double3), time);
                        extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, time);
                    }

                    _realm.Update();
                }
                while (Time.realtimeSinceStartupAsDouble - started < 0.9d);

                foreach (TestGhost ghost in ghosts)
                {
                    AssertPosition(ghost.transform.position, new Vector3(10, 0, 0), 0.15f);
                    Assert.That(Quaternion.Angle(ghost.transform.rotation, stoppedRotation), Is.LessThan(1f));
                    Assert.That(ghost.GetComponent<Spatial>().Position, Is.EqualTo(new Double3(10, 0, 0)));
                }
            }
        }

        /// <summary>Consumers can switch built-in and application strategies on held observations without replacing traits or losing motion; immediate, buffered and untimed presentation converge through live transitions.</summary>
        /// <returns>An iterator that lets extrapolation caps and live-setting transitions complete.</returns>
        [UnityTest]
        public IEnumerator ExtrapolationStrategies_SwitchAtRuntimeWithoutLosingObservations()
        {
            // Keep input poses and motion fixed. Change only the strategy and its settings-change blend.
            for (int mode = 0; mode < 3; mode++)
            {
                _realm.InterpolationDelay = mode == 1 ? 0.1f : 0;
                TestGhost ghost = _source.Publish("strategies-" + mode);
                Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
                Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
                Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
                smoothing.PositionHalfLife = 0.02f;
                smoothing.RotationHalfLife = 0.02f;
                extrapolation.MaximumExtrapolation = 0.1f;
                Timestamp? time = mode == 2 ? (Timestamp?)null : new Timestamp(1000, 0);
                spatial.SetCartesianPosition(default(Double3), time);
                spatial.SetSourceRotation(Quaternion.identity, time);
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(8, 0, 0), time);
                extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(4, 0, 0), time);
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI / 2, 0), CoordinateSystem.Unity, time);
                TestGhost witness = _source.Publish("strategy-clock-" + mode);
                witness.gameObject.AddComponent<Spatial>()
                    .SetCartesianPosition(default(Double3), new Timestamp(1002 + mode, 0));
                _realm.Update();
                yield return AdvanceSmoothing(0.4d);
                Assert.That(extrapolation.Strategy, Is.TypeOf<ConstantAccelerationExtrapolation>());
                AssertPosition(ghost.transform.position, new Vector3(0.82f, 0, 0), 0.005f);
                AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, 9, 0));

                IExtrapolationStrategy[] strategies =
                {
                    new ConstantVelocityExtrapolation(), new SingerExtrapolation { ManeuverTimeConstant = 0.05f }, new HalfSpeedExtrapolation(),
                    new NoExtrapolation(), new ConstantAccelerationExtrapolation()
                };
                float[] distances = { 0.8f, 0.8113534f, 0.4f, 0, 0.82f };
                float[] angles = { 9, 9, 4.5f, 0, 9 };
                for (int index = 0; index < strategies.Length; index++)
                {
                    Vector3 before = ghost.transform.position;
                    extrapolation.Strategy = strategies[index];
                    _realm.Update();
                    AssertPosition(ghost.transform.position, before, 0.005f);
                    Assert.That(extrapolation.Strategy, Is.SameAs(strategies[index]));
                    yield return AdvanceSmoothing(0.4d);
                    AssertPosition(ghost.transform.position, new Vector3(distances[index], 0, 0), 0.005f);
                    AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, angles[index], 0));
                    Assert.That(spatial.Position, Is.EqualTo(default(Double3)));
                    Assert.That(spatial.PositionTime, Is.EqualTo(time));
                    Assert.That(extrapolation.GetComponent<Spatial>().Velocity, Is.EqualTo(new Double3(8, 0, 0)));
                    Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(new Double3(4, 0, 0)));
                }

                Assert.Throws<ArgumentNullException>(() => extrapolation.Strategy = null);
                Assert.That(extrapolation.Strategy, Is.TypeOf<ConstantAccelerationExtrapolation>());
            }
        }

        /// <summary>Application strategies receive estimated motion while hold extrapolation preserves interpolation of recorded poses.</summary>
        [Test]
        public void ExtrapolationStrategies_ControlEstimatedMotionWithoutChangingInterpolation()
        {
            // Use two timestamped poses to estimate motion. Check the application strategy at the prediction limit.
            TestGhost ghost = _source.Publish("estimated-strategy");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            ExtrapolationContext linear = default(ExtrapolationContext);
            ExtrapolationContext angular = default(ExtrapolationContext);
            extrapolation.Strategy = new HalfSpeedExtrapolation
            {
                PositionObserved = context => linear = context,
                RotationObserved = context => angular = context
            };
            extrapolation.MaximumExtrapolation = 0.1f;
            spatial.SetCartesianPosition(default(Double3), new Timestamp(1000, 0));
            spatial.SetSourceRotation(Quaternion.identity, new Timestamp(1000, 0));
            _realm.Update();
            spatial.SetCartesianPosition(new Double3(8, 0, 0), new Timestamp(1001, 0));
            spatial.SetSourceRotation(Quaternion.Euler(0, 90, 0), new Timestamp(1001, 0));
            Spatial clock = _source.Publish("estimated-clock").gameObject.AddComponent<Spatial>();
            clock.SetCartesianPosition(default(Double3), new Timestamp(1002, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(8.4f, 0, 0), 0.001f);
            AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, 94.5f, 0));
            Assert.That(linear.VelocityIsEstimated, Is.True);
            Assert.That(linear.Velocity, Is.EqualTo(new Double3(8, 0, 0)));
            Assert.That(angular.AngularVelocityIsEstimated, Is.True);
            Assert.That(angular.AngularVelocity.Value.Y, Is.EqualTo(Math.PI / 2).Within(0.00001));
            Assert.That(linear.Acceleration, Is.Null);

            // Supplied zero rates override estimates. Clearing them restores estimates from the stored poses.
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(default(Double3), new Timestamp(1001, 0));
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, new Timestamp(1001, 0));
            _realm.Update();
            Assert.That(linear.VelocityIsEstimated, Is.False);
            Assert.That(linear.Velocity, Is.EqualTo(default(Double3)));
            Assert.That(angular.AngularVelocityIsEstimated, Is.False);
            Assert.That(angular.AngularVelocity, Is.EqualTo(default(Double3)));
            extrapolation.GetComponent<Spatial>().ClearVelocity();
            extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
            _realm.Update();
            Assert.That(linear.VelocityIsEstimated, Is.True);
            Assert.That(angular.AngularVelocityIsEstimated, Is.True);

            // NoExtrapolation must still interpolate between stored poses.
            extrapolation.Strategy = new NoExtrapolation();
            _realm.InterpolationDelay = 0.5f;
            spatial.ResetPresentation();
            spatial.SetCartesianPosition(new Double3(16, 0, 0), new Timestamp(1002, 0));
            spatial.SetSourceRotation(Quaternion.Euler(0, 180, 0), new Timestamp(1002, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(12, 0, 0), 0.1f);
            AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, 135, 0));
        }

        /// <summary>One full-pose strategy call supplies prediction, packet correction, smoothing and live tuning for each eligible Ghost.</summary>
        [Test]
        public void ExtrapolationStrategies_RunOncePerGhostPerProjection()
        {
            Spatial spatial = _source.Publish("single prediction").gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = spatial.gameObject.AddComponent<Extrapolation>();
            Smoothing smoothing = spatial.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.2f;
            smoothing.RotationHalfLife = 0.2f;
            DistanceExtrapolation model = new DistanceExtrapolation();
            extrapolation.Strategy = model;
            extrapolation.PredictionTime = 0.2f;
            extrapolation.MaximumExtrapolation = 1;
            Timestamp positionTime = new Timestamp(1000, 0);
            Timestamp rotationTime = new Timestamp(1000, 100000000);
            spatial.SetCartesianPosition(new Double3(10, 0, 0), positionTime);
            spatial.SetSourceRotation(Quaternion.identity, rotationTime);
            spatial.SetCartesianVelocity(new Double3(5, 0, 0), positionTime);
            spatial.SetBodyAngularVelocity(new Double3(0, 1, 0), CoordinateSystem.Unity, positionTime);

            for (int pass = 0; pass < 3; pass++)
            {
                model.Calls = 0;
                _realm.Update();
                Assert.That(model.Calls, Is.EqualTo(1), "Both pose channels and both filters must share one strategy result.");
                Assert.That(model.Request.PositionContext.ObservationTime, Is.EqualTo(positionTime));
                Assert.That(model.Request.RotationContext.ObservationTime, Is.EqualTo(rotationTime));
                Assert.That(model.Request.PositionSeconds, Is.InRange(0.2d, (double)extrapolation.MaximumExtrapolation));
                Assert.That(model.Request.RotationSeconds, Is.InRange(0.2d, (double)extrapolation.MaximumExtrapolation));
                Assert.That(model.Result.Position.X, Is.EqualTo(10 + 5 * model.Request.PositionSeconds).Within(0.0001d));
                Assert.That(model.Result.Velocity, Is.EqualTo(new Double3(5, 0, 0)));
                Assert.That(model.Result.AngularVelocity.Y, Is.EqualTo(1).Within(0.0001d));
                // Editing these settings used to invoke old-settings and tangent previews as well.
                extrapolation.MaximumExtrapolation = 0.8f - pass * 0.1f;
                smoothing.PositionHalfLife += 0.1f;
            }

            DistanceExtrapolation replacement = new DistanceExtrapolation();
            extrapolation.Strategy = replacement;
            model.Calls = 0;
            _realm.Update();
            Assert.That(model.Calls, Is.Zero, "Live tuning must not call the old strategy again.");
            Assert.That(replacement.Calls, Is.EqualTo(1));

            // Following must reuse the reference prediction when placing that same root.
            _realm.ReferenceFrame = new ReferenceFrame { FollowedGhost = spatial.GetComponent<TestGhost>().Key };
            Spatial peer = _source.Publish("single prediction peer").gameObject.AddComponent<Spatial>();
            peer.SetCartesianPosition(new Double3(40, 0, 0), positionTime);
            DistanceExtrapolation peerModel = new DistanceExtrapolation();
            peer.gameObject.AddComponent<Extrapolation>().Strategy = peerModel;
            replacement.Calls = 0;
            _realm.Update();
            Assert.That(replacement.Calls, Is.EqualTo(1), "Reference capture and root placement must reuse one result.");
            Assert.That(peerModel.Calls, Is.EqualTo(1), "Peers get their own single result on the shared timeline.");
            Assert.That(replacement.Request.PositionSeconds - replacement.Request.RotationSeconds,
                Is.EqualTo(0.1d).Within(0.00001d), "Independent sample times must reach the same reference target.");

            replacement.Calls = 0;
            extrapolation.enabled = false;
            _realm.Update();
            Assert.That(replacement.Calls, Is.Zero);
        }

        /// <summary>Both channel contexts are captured before the single strategy call; new input is observed on the next projection.</summary>
        [Test]
        public void ExtrapolationStrategies_CaptureBothChannelsBeforeCallingTheModel()
        {
            TestGhost ghost = _source.Publish("changing-strategy");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            Timestamp time = new Timestamp(1000, 0);
            spatial.SetCartesianPosition(default(Double3), time);
            spatial.SetSourceRotation(Quaternion.identity, time);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(10, 0, 0), time);
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 1, 0), CoordinateSystem.Unity, time);
            extrapolation.PredictionTime = 0.1f;
            bool changed = false;
            ExtrapolationContext angular = default(ExtrapolationContext);
            extrapolation.Strategy = new HalfSpeedExtrapolation
            {
                PositionObserved = context =>
                {
                    if (!changed)
                    {
                        changed = true;
                        // Replace timed motion from inside application model code.
                        extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
                        extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0.5, 0), CoordinateSystem.Unity, time);
                    }
                },
                RotationObserved = context => angular = context
            };

            _realm.Update();
            Assert.That(changed, Is.True);
            Assert.That(angular.AngularVelocity, Is.EqualTo(new Double3(0, 1, 0)));
            _realm.Update();
            Assert.That(angular.AngularVelocity, Is.EqualTo(new Double3(0, 0.5, 0)));
        }

        /// <summary>Application models receive the full pose and independently timed motion sampled at the channel being extrapolated, including unavailable future acceleration and coordinate metadata.</summary>
        [Test]
        public void ExtrapolationContext_AlignsAllMotionToEachPoseChannel()
        {
            // Give position and rotation different times, with motion samples before and after each.
            _realm.ReferenceFrame = new ReferenceFrame { Coordinates = CoordinateSystem.NorthEastDown };
            TestGhost ghost = _source.Publish("context");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            ExtrapolationContext linear = default(ExtrapolationContext);
            ExtrapolationContext angular = default(ExtrapolationContext);
            extrapolation.Strategy = new HalfSpeedExtrapolation
            {
                PositionObserved = context => linear = context,
                RotationObserved = context => angular = context
            };
            Timestamp positionTime = new Timestamp(1002, 0);
            Timestamp rotationTime = new Timestamp(1001, 0);
            Double3 position = new Double3(100, 20, 30);
            Quaternion rotation = Quaternion.Euler(5, 30, 20);
            spatial.SetCartesianPosition(position, positionTime);
            spatial.SetSourceRotation(rotation, rotationTime);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(8, 0, 0), new Timestamp(1000, 0));
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(12, 0, 0), positionTime);
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(4, 0, 0), positionTime);
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI / 2, 0), CoordinateSystem.Unity, rotationTime);
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI, 0), CoordinateSystem.Unity, new Timestamp(1003, 0));
            _source.Publish("context-clock").gameObject.AddComponent<Spatial>()
                .SetCartesianPosition(default(Double3), new Timestamp(1005, 0));
            _realm.Update();

            foreach (ExtrapolationContext context in new[] { linear, angular })
            {
                Assert.That(context.Position, Is.EqualTo(position));
                AssertRotation(context.Rotation.Value, rotation);
                Assert.That(context.PositionTime, Is.EqualTo(positionTime));
                Assert.That(context.RotationTime, Is.EqualTo(rotationTime));
                Assert.That(context.LatestVelocityTime, Is.EqualTo(positionTime));
                Assert.That(context.LatestAccelerationTime, Is.EqualTo(positionTime));
                Assert.That(context.LatestAngularVelocityTime, Is.EqualTo(new Timestamp(1003, 0)));
                Assert.That(context.VelocityIsEstimated, Is.False);
                Assert.That(context.AngularVelocityIsEstimated, Is.False);
                Assert.That(context.AngularVelocityIsWorld, Is.False);
                Assert.That(context.RotationSpace, Is.EqualTo(RotationSpace.Source));
                Assert.That(context.Space, Is.EqualTo(ReferenceSpace.Cartesian));
                Assert.That(context.SourceCoordinates, Is.EqualTo(CoordinateSystem.NorthEastDown));
                Assert.That(context.EarthCenteredBodyAxes, Is.Null);
            }

            Assert.That(linear.ObservationTime, Is.EqualTo(positionTime));
            Assert.That(linear.Velocity, Is.EqualTo(new Double3(12, 0, 0)));
            Assert.That(linear.Acceleration, Is.EqualTo(new Double3(4, 0, 0)));
            Assert.That(linear.AngularVelocity.Value.Y, Is.EqualTo(Math.PI * 0.75).Within(0.00001));
            Assert.That(angular.ObservationTime, Is.EqualTo(rotationTime));
            Assert.That(angular.Velocity, Is.EqualTo(new Double3(10, 0, 0)));
            Assert.That(angular.Acceleration, Is.Null, "Later acceleration must not describe an older attitude.");
            Assert.That(angular.AngularVelocity.Value.Y, Is.EqualTo(Math.PI / 2).Within(0.00001));

            extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
            extrapolation.Strategy = new VelocityHeadingExtrapolation();
            _realm.ReferenceFrame = null;
            spatial.ResetPresentation();
            _realm.Update();
            AssertRotation(ghost.transform.rotation, Quaternion.LookRotation(Vector3.right));
            Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.False,
                "A model can use linear motion and raw attitude to predict orientation without a supplied or estimated angular rate.");
        }

        /// <summary>Rereading a cached untimed attitude preserves bounded reference prediction between SDK packets; a small new attitude refreshes its age and keeps distant targets aligned.</summary>
        /// <returns>An iterator that presents a held bank observation at approximately 60 Hz.</returns>
        [UnityTest]
        public IEnumerator UntimedExtrapolation_CachedReferenceAttitudeKeepsItsObservationAge()
        {
            // Read the same SDK cache every display frame while the aircraft continues banking.
            TestGhost reference = _source.PublishPosition("cached reference", new Double3(1e9, 0, 0));
            Spatial spatial = reference.GetRequired<Spatial>();
            Extrapolation extrapolation = reference.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.1f;
            extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(4d * Math.PI, 0, 0));
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = reference.Key };
            _realm.ReferenceFrame = frame;
            Vector3 offset = new Vector3(3000, 700, 5000);
            TestGhost target = _source.PublishPosition("distant target", spatial.Position + new Double3(offset.x, offset.y, offset.z));
            double receivedBefore = Time.realtimeSinceStartupAsDouble;
            spatial.SetSourceRotation(Quaternion.identity);
            double receivedAfter = Time.realtimeSinceStartupAsDouble;
            _realm.Update();

            double after;
            do
            {
                yield return new WaitForSecondsRealtime(1f / 60f);
                spatial.SetSourceRotation(Quaternion.identity);
                double before = Time.realtimeSinceStartupAsDouble;
                _realm.Update();
                after = Time.realtimeSinceStartupAsDouble;
                double age = (before + after - receivedBefore - receivedAfter) * 0.5d;
                Quaternion expected = Quaternion.AngleAxis((float)(-720d * Math.Min(age, extrapolation.MaximumExtrapolation)), Vector3.forward);
                float tolerance = 0.1f + 720f * (float)(after - before + receivedAfter - receivedBefore);
                Assert.That(Quaternion.Angle(frame.Rotation, expected), Is.LessThan(tolerance),
                    "Repeated cache reads must not restart angular prediction from zero age.");
                Assert.That(Vector3.Angle(target.transform.position, Quaternion.Inverse(expected) * offset), Is.LessThan(tolerance));
                AssertRotation(reference.transform.rotation, frame.UnityRotation);
            }
            while (after - receivedAfter < 0.14d);

            // Even a change below Unity's approximate quaternion equality threshold is a new observation.
            Quaternion correction = Quaternion.AngleAxis(0.02f, Vector3.right);
            double correctedAt = Time.realtimeSinceStartupAsDouble;
            spatial.SetSourceRotation(correction);
            _realm.Update();
            float correctionTolerance = 0.1f + 720f * (float)(Time.realtimeSinceStartupAsDouble - correctedAt);
            Assert.That(Quaternion.Angle(frame.Rotation, correction), Is.LessThan(correctionTolerance),
                "A changed untimed attitude must refresh its own age without retaining the old capped lead.");
            Assert.That(spatial.RotationTime, Is.Null);

            // Changing the attitude representation is fresh input even when its quaternion components match.
            spatial.SetSourceRotation(Quaternion.identity);
            yield return new WaitForSecondsRealtime(0.04f);
            _realm.Update();
            Assert.That(Quaternion.Angle(frame.Rotation, Quaternion.identity), Is.GreaterThan(10));
            frame.Space = ReferenceSpace.Geographic;
            correctedAt = Time.realtimeSinceStartupAsDouble;
            spatial.SetGeographicRotation(0, 0, 0);
            _realm.Update();
            correctionTolerance = 0.1f + 720f * (float)(Time.realtimeSinceStartupAsDouble - correctedAt);
            Assert.That(Quaternion.Angle(frame.Rotation, Quaternion.identity), Is.LessThan(correctionTolerance));
        }

        /// <summary>Timestamped entities use one estimated source clock, reject older pose/motion packets and refresh stationary sample age; behavior traits can be disabled independently.</summary>
        [Test]
        public void TimestampedExtrapolation_AlignsPacketAgesAndRejectsOlderChannels()
        {
            // Use another root's newer timestamp to set the shared clock past this root's prediction limit.
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1e9, 0, 0) };
            Timestamp older = new Timestamp(1700000000L, 250000000U);
            Timestamp latest = new Timestamp(1700000000L, 900000000U);
            TestGhost delayed = _source.Publish("delayed");
            Spatial spatial = delayed.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = delayed.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.4f;
            spatial.SetCartesianPosition(new Double3(1e9 + 10, 0, 0), older);
            spatial.SetSourceRotation(Quaternion.identity, older);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(8, 0, 0), older);
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(4, 0, 0), older);
            TestGhost current = _source.Publish("current");
            Spatial currentSpatial = current.gameObject.AddComponent<Spatial>();
            currentSpatial.SetCartesianPosition(new Double3(1e9 + 100, 0, 0), latest);
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(13.52f, 0, 0), 0.001f);
            AssertPosition(current.transform.position, new Vector3(100, 0, 0));
            Assert.That(spatial.Position, Is.EqualTo(new Double3(1e9 + 10, 0, 0)));
            Assert.That(delayed.Traits, Does.Contain(extrapolation));

            Timestamp stale = new Timestamp(1700000000L, 100000000U);
            spatial.SetCartesianPosition(new Double3(1e9 - 100, 0, 0), stale);
            spatial.SetSourceRotation(Quaternion.Euler(0, 90, 0), stale);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(-80, 0, 0), stale);
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(-40, 0, 0), stale);
            spatial.SetCartesianPosition(new Double3(1e9 - 100, 0, 0), older);
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(13.52f, 0, 0), 0.001f);
            AssertRotation(delayed.transform.rotation, Quaternion.identity);
            Assert.That(spatial.PositionTime, Is.EqualTo(older));
            Assert.That(spatial.RotationTime, Is.EqualTo(older));
            Assert.That(extrapolation.GetComponent<Spatial>().Velocity, Is.EqualTo(new Double3(8, 0, 0)));

            spatial.SetCartesianPosition(spatial.Position, latest);
            _realm.Update();
            Assert.That(spatial.PositionTime, Is.EqualTo(latest));
            Assert.That(delayed.transform.position.x, Is.EqualTo(10).Within(0.1), "A fresh stationary observation refreshes age.");
            extrapolation.enabled = false;
            spatial.ResetPresentation(); // Establish immediate raw-pose handling for the remaining timestamp checks.
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(10, 0, 0));
            Smoothing smoothing = delayed.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0;
            spatial.SetCartesianPosition(new Double3(1e9 + 20, 0, 0), new Timestamp(1700000001L, 0));
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(20, 0, 0));
            smoothing.enabled = false;
            spatial.SetCartesianPosition(new Double3(1e9 + 30, 0, 0), new Timestamp(1700000001L, 100000000U));
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(30, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => extrapolation.MaximumExtrapolation = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => extrapolation.MaximumExtrapolation = -1);
        }

        /// <summary>Accepted burst observations interpolate pose and preserve supplied maneuver motion with smoothing active; gap motion uses the latest pose's timeline, stale packets are ignored and restarted clocks discard history.</summary>
        /// <returns>An iterator that crosses buffered velocity observations and independently timed motion inside a extrapolation gap.</returns>
        [UnityTest]
        public IEnumerator BufferedTimeline_InterpolatesBurstChannelsAndResetsWithTheSdkClock()
        {
            // Send several poses before rendering. Playback uses history while raw properties keep the newest input.
            Assert.That(_realm.InterpolationDelay, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => _realm.InterpolationDelay = -0.01f);
            Assert.Throws<ArgumentOutOfRangeException>(() => _realm.InterpolationDelay = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => _realm.InterpolationDelay = float.PositiveInfinity);
            _realm.InterpolationDelay = 5;
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1e9, 0, 0) };
            TestGhost ghost = _source.Publish("buffered");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            ghost.gameObject.AddComponent<Extrapolation>().enabled = false;
            Timestamp first = new Timestamp(1000, 0);
            Timestamp latest = new Timestamp(1010, 0);
            Quaternion firstRotation = Quaternion.Euler(0, 350, 0);
            Quaternion latestRotation = Quaternion.Euler(0, 10, 0);
            spatial.SetCartesianPosition(new Double3(1e9, 0, 0), first);
            spatial.SetSourceRotation(firstRotation, first);
            double publishedAt = Time.realtimeSinceStartupAsDouble;
            spatial.SetCartesianPosition(new Double3(1e9 + 1000, 0, 0), latest);
            spatial.SetSourceRotation(latestRotation, latest);
            _realm.Update();
            AssertBufferedCartesianPose(spatial, 500, publishedAt);
            Assert.That(spatial.Position, Is.EqualTo(new Double3(1e9 + 1000, 0, 0)));
            AssertRotation(spatial.Rotation, latestRotation);

            // Reject old and duplicate samples. Playback must stay correct when another entity advances the shared clock.
            spatial.SetCartesianPosition(new Double3(1e9 - 1000, 0, 0), new Timestamp(1005, 0));
            spatial.SetSourceRotation(Quaternion.Euler(0, 180, 0), latest);
            TestGhost clock = _source.Publish("clock");
            Spatial clockSpatial = clock.gameObject.AddComponent<Spatial>();
            publishedAt = Time.realtimeSinceStartupAsDouble;
            clockSpatial.SetCartesianPosition(new Double3(1e9, 0, 0), new Timestamp(1012, 0));
            _realm.Update();
            AssertBufferedCartesianPose(spatial, 700, publishedAt);
            Assert.That(spatial.Position, Is.EqualTo(new Double3(1e9 + 1000, 0, 0)));
            Assert.That(spatial.PositionTime, Is.EqualTo(latest));
            Assert.That(spatial.RotationTime, Is.EqualTo(latest));
            AssertRotation(spatial.Rotation, latestRotation);

            // Reset SDK time, clear old samples and accept restarted timestamps immediately.
            _realm.ResetSpatialTime();
            Timestamp restarted = new Timestamp(0, 0);
            spatial.SetCartesianPosition(new Double3(1e9 + 50, 0, 0), restarted);
            spatial.SetSourceRotation(Quaternion.Euler(0, 90, 0), restarted);
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(50, 0, 0));
            AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, 90, 0));
            Assert.That(spatial.PositionTime, Is.EqualTo(restarted));
            Assert.That(spatial.RotationTime, Is.EqualTo(restarted));
            yield return AssertBufferedManeuverPreservesMotion();
            yield return AssertBufferedAccelerationPreservesVelocityTimeline();
        }

        /// <summary>A moving geographic reference, independent peer and attached part share buffered time; exhausted buffers hold or extrapolate both pose channels only to the configured extrapolation limit.</summary>
        [Test]
        public void BufferedTimeline_GeographicPeersShareTimeAndGapsRespectExtrapolationLimits()
        {
            _realm.InterpolationDelay = 1;
            TestGhost reference = _source.Publish("reference");
            Spatial referenceSpatial = reference.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = reference.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.05f;
            TestGhost peer = _source.Publish("peer");
            Spatial peerSpatial = peer.gameObject.AddComponent<Spatial>();
            peer.gameObject.AddComponent<Extrapolation>().enabled = false;
            TestGhost part = _source.Publish("part");
            Spatial partSpatial = part.gameObject.AddComponent<Spatial>();
            Vector3 peerOffset = new Vector3(8, 0, 3);
            Vector3 partOffset = new Vector3(-1, 0.5f, 2);
            partSpatial.Attach(reference.Key, partOffset);
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                FollowedGhost = reference.Key,
                UnityPosition = new Vector3(7, 2, -3),
                UnityRotation = Quaternion.Euler(0, 15, 0),
                MaxDistance = 30
            };
            _realm.ReferenceFrame = frame;
            GeoPosition first = new GeoPosition(0, 0, 40);
            GeoPosition latest = new GeoPosition(0, 200.0 / (6378137.0 + 40) * 180 / Math.PI, 40);
            PublishBufferedGeographicPair(referenceSpatial, peerSpatial, first, 0, peerOffset, new Timestamp(1000, 0));
            PublishBufferedGeographicPair(referenceSpatial, peerSpatial, latest, 2, peerOffset, new Timestamp(1002, 0));
            Double3 velocity = new Double3(100, 0, 0);
            extrapolation.GetComponent<Spatial>().SetGeographicVelocity(velocity, latest, new Timestamp(1002, 0));
            extrapolation.GetComponent<Spatial>().SetGeographicAcceleration(new Double3(4000, 0, 0), latest, new Timestamp(1003, 0));
            _realm.Update();
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertRotation(reference.transform.rotation, frame.UnityRotation);
            // The separate target follows a short straight segment. Approximating its 2-degree turn adds less than 2 mm of error.
            AssertPosition(peer.transform.position, frame.UnityPosition + frame.UnityRotation * peerOffset, 0.01f);
            AssertRotation(peer.transform.rotation, frame.UnityRotation);
            AssertPosition(part.transform.position, reference.transform.position + reference.transform.rotation * partOffset);
            Assert.That(referenceSpatial.IsInRange, Is.True,
                "The buffered origin must stay visible when travel since its playback time exceeds the range limit.");
            Assert.That(peerSpatial.IsInRange, Is.True);
            Assert.That(Double3.Distance(frame.Position, first.ToEarthCentered()), Is.InRange(90, 150));
            Assert.That(Quaternion.Angle(Quaternion.identity, frame.Rotation), Is.InRange(0.9f, 1.5f));

            TestGhost clock = _source.Publish("newer-clock");
            clock.gameObject.AddComponent<Spatial>().SetGeographicPosition(latest, new Timestamp(1010, 0));
            _realm.Update();
            Double3 capped = latest.ToEarthCentered() + latest.ToEarthCenteredVector(velocity) * extrapolation.MaximumExtrapolation;
            Assert.That(Double3.Distance(frame.Position, capped), Is.LessThan(0.001));
            Assert.That(extrapolation.GetComponent<Spatial>().HasAcceleration, Is.True,
                "Accepted future acceleration must remain raw input without changing an earlier capped playback pose.");
            AssertRotation(frame.Rotation, Quaternion.Euler(0, 2.05f, 0));
            Assert.That(frame.TryToUnityPosition(peerSpatial.Position, out Vector3 heldPeer), Is.True);
            AssertPosition(peer.transform.position, heldPeer, 0.001f);
            AssertPosition(reference.transform.position, frame.UnityPosition);
            AssertPosition(part.transform.position, reference.transform.position + reference.transform.rotation * partOffset);
            Assert.That(referenceSpatial.Position, Is.EqualTo(latest.ToEarthCentered()));
            Assert.That(referenceSpatial.PositionTime, Is.EqualTo(new Timestamp(1002, 0)));
            Assert.That(referenceSpatial.RotationTime, Is.EqualTo(new Timestamp(1002, 0)));
        }

        /// <summary>Late coherent observations retain their supplied motion after the extrapolation cap, with immediate and buffered playback, while a held packet cannot continue advancing the filter.</summary>
        /// <returns>An iterator that delivers a 560-knot stream at 60 Hz while requesting 90 Hz presentation.</returns>
        [UnityTest]
        public IEnumerator TimestampedExtrapolation_LatePacketsPreserveMotionAfterTheExtrapolationCap()
        {
            int targetFrameRate = Application.targetFrameRate;
            int verticalSync = QualitySettings.vSyncCount;
            try
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 90;
                _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1e9, 0, 0) };
                TestGhost ghost = _source.Publish("late moving entity");
                Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
                Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
                extrapolation.MaximumExtrapolation = 0.05f;
                ghost.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.3f;
                Spatial clock = _source.Publish("fresh clock witness").gameObject.AddComponent<Spatial>();
                foreach (float delay in new[] { 0f, 0.1f })
                {
                    yield return AssertLatePacketsPreserveCappedMotion(spatial, extrapolation, clock, delay);
                }
            }
            finally
            {
                Application.targetFrameRate = targetFrameRate;
                QualitySettings.vSyncCount = verticalSync;
            }
        }

        /// <summary>Enabled extrapolation clamps small and saved zero limits to 7 ms for both pose channels, while disabling retains motion data without predicting.</summary>
        [Test]
        public void Extrapolation_MinimumLimitPredictsUntilTheTraitIsDisabled()
        {
            TestGhost ghost = _source.Publish("minimum prediction");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation motion = ghost.gameObject.AddComponent<Extrapolation>();
            Timestamp sample = new Timestamp(1000, 0);
            spatial.SetCartesianPosition(default(Double3), sample);
            spatial.SetSourceRotation(Quaternion.identity, sample);
            motion.GetComponent<Spatial>().SetCartesianVelocity(new Double3(100, 0, 0), sample);
            motion.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 1, 0), CoordinateSystem.Unity, sample);
            TestGhost clock = _source.Publish("minimum prediction clock");
            clock.gameObject.AddComponent<Spatial>().SetCartesianPosition(default(Double3), new Timestamp(1001, 0));

            // Known sample age exceeds every requested cap. Each fresh presentation
            // must therefore use exactly the minimum limit for position and attitude.
            foreach (float limit in new[] { 0f, 0.001f, 0.007f })
            {
                motion.MaximumExtrapolation = limit;
                Assert.That(motion.MaximumExtrapolation, Is.EqualTo(0.007f));
                spatial.ResetPresentation();
                _realm.Update();
                AssertPosition(ghost.transform.position, new Vector3(0.7f, 0, 0), 0.001f);
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(0.007f * Mathf.Rad2Deg, Vector3.up));
            }

            // Runtime deserialization can bypass the setter and editor validation.
            JsonUtility.FromJsonOverwrite("{\"_maximumExtrapolation\":0}", motion);
            Assert.That(motion.MaximumExtrapolation, Is.EqualTo(0.007f));
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.MaximumExtrapolation = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.MaximumExtrapolation = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => motion.MaximumExtrapolation = float.PositiveInfinity);
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0.7f, 0, 0), 0.001f);

            motion.enabled = false;
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero);
            AssertRotation(ghost.transform.rotation, Quaternion.identity);
            Assert.That(motion.GetComponent<Spatial>().HasVelocity && motion.GetComponent<Spatial>().HasAngularVelocity, Is.True);
            motion.enabled = true;
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0.7f, 0, 0), 0.001f);
        }

        /// <summary>Extrapolation and shared-delay tuning preserve the displayed pose, settle to the new timeline and keep a moving stream advancing through repeated edits without changing raw observations.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator TimestampedExtrapolation_LiveTuningRemainsContinuousWithoutSmoothing()
        {
            const double origin = 1e9;
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(origin, 0, 0) };
            TestGhost ghost = _source.Publish("tuned");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.15f;
            Timestamp observationTime = new Timestamp(1000, 0);
            Double3 observation = new Double3(origin, 0, 0);
            spatial.SetCartesianPosition(observation, observationTime);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(100, 0, 0), observationTime);
            TestGhost clock = _source.Publish("current-clock");
            Spatial clockSpatial = clock.gameObject.AddComponent<Spatial>();
            clockSpatial.SetCartesianPosition(observation, new Timestamp(1001, 0));
            _realm.Update();
            Assert.That(ghost.GetComponent<Smoothing>(), Is.Null);
            AssertPosition(ghost.transform.position, new Vector3(15, 0, 0), 0.001f);

            void ChangeAndAssertContinuity(Action change)
            {
                float before = ghost.transform.position.x;
                change();
                _realm.Update();
                Assert.That(ghost.transform.position.x, Is.EqualTo(before).Within(0.05),
                    "Changing treatment of the same observation must not introduce a position jump.");
            }

            ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = 0.03f);
            yield return AdvanceSmoothing(0.3);
            AssertPosition(ghost.transform.position, new Vector3(3, 0, 0), 0.001f);
            ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = 0);
            yield return AdvanceSmoothing(0.3);
            AssertPosition(ghost.transform.position, new Vector3(0.7f, 0, 0), 0.001f);

            ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = 0.15f);
            yield return AdvanceSmoothing(0.08);
            Assert.That(ghost.transform.position.x, Is.GreaterThan(0).And.LessThan(15));
            ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = 0.03f);
            yield return AdvanceSmoothing(0.04);
            ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = 0.09f);
            yield return AdvanceSmoothing(0.3);
            AssertPosition(ghost.transform.position, new Vector3(9, 0, 0), 0.001f);
            ChangeAndAssertContinuity(() => extrapolation.enabled = false);
            yield return AdvanceSmoothing(0.3);
            AssertPosition(ghost.transform.position, Vector3.zero);
            ChangeAndAssertContinuity(() => extrapolation.enabled = true);
            yield return AdvanceSmoothing(0.3);
            AssertPosition(ghost.transform.position, new Vector3(9, 0, 0), 0.001f);
            Assert.That(spatial.Position, Is.EqualTo(observation));
            Assert.That(spatial.PositionTime, Is.EqualTo(observationTime));
            Assert.That(extrapolation.GetComponent<Spatial>().Velocity, Is.EqualTo(new Double3(100, 0, 0)));

            _realm.ResetSpatialTime();
            void PublishMovingObservation(double seconds)
            {
                Timestamp sampleTime = Timestamp.FromSeconds(1002 + seconds);
                spatial.SetCartesianPosition(new Double3(origin + 100 * seconds, 0, 0), sampleTime);
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(100, 0, 0), sampleTime);
                // A second source observation establishes that this entity's packets are 200 ms old.
                clockSpatial.SetCartesianPosition(observation, Timestamp.FromSeconds(1002.2 + seconds));
            }

            PublishMovingObservation(0);
            _realm.Update();
            double started = Time.realtimeSinceStartupAsDouble;
            float initialPresentation = ghost.transform.position.x;
            double elapsed;
            float[] horizons = { 0.03f, 0.15f, 0.06f };
            int edit = 0;
            do
            {
                yield return null;
                elapsed = Time.realtimeSinceStartupAsDouble - started;
                PublishMovingObservation(elapsed);
                _realm.Update();
                float horizon = horizons[edit % horizons.Length];
                ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = horizon);
                edit++;
            }
            while (elapsed < 0.35);

            Assert.That(ghost.transform.position.x - initialPresentation,
                Is.InRange(100 * elapsed - 16, 100 * elapsed + 16),
                "Repeated slider edits must not freeze motion or accumulate error beyond the bounded extrapolation range.");
            Double3 lastObservation = spatial.Position;
            Timestamp? lastObservationTime = spatial.PositionTime;
            ChangeAndAssertContinuity(() => extrapolation.MaximumExtrapolation = 0.03f);
            yield return AdvanceSmoothing(0.3);
            Assert.That(ghost.transform.position.x, Is.EqualTo(lastObservation.X - origin + 3).Within(0.001),
                "The transition fully settles to the new estimate without a residual tuning offset.");
            Assert.That(spatial.Position, Is.EqualTo(lastObservation));
            Assert.That(spatial.PositionTime, Is.EqualTo(lastObservationTime));

            // Set a known playback pose so this check does not depend on how much older motion is still stored.
            spatial.ResetPresentation();
            _realm.Update();
            Assert.That(ghost.transform.position.x, Is.EqualTo(lastObservation.X - origin + 3).Within(0.001));
            ChangeAndAssertContinuity(() => _realm.InterpolationDelay = 2);
            yield return AdvanceSmoothing(0.3);
            Assert.That(ghost.transform.position.x, Is.EqualTo(lastObservation.X - origin).Within(0.001));
            ChangeAndAssertContinuity(() => _realm.InterpolationDelay = 0);
            yield return AdvanceSmoothing(0.3);
            Assert.That(ghost.transform.position.x, Is.EqualTo(lastObservation.X - origin + 3).Within(0.001));
            Assert.That(spatial.Position, Is.EqualTo(lastObservation));
            Assert.That(spatial.PositionTime, Is.EqualTo(lastObservationTime));
        }

        /// <summary>Coherent SDK maneuvers revise extrapolation lead immediately while position noise stays filtered, with immediate and buffered playback and a rotating followed reference.</summary>
        [Test]
        public void TimestampedExtrapolation_ManeuversPreserveMotionWithoutSmoothingTheExtrapolationLead()
        {
            TestGhost reference = _source.Publish("maneuvering reference");
            Spatial spatial = reference.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = reference.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.1f;
            reference.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.3f;
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = reference.Key };
            _realm.ReferenceFrame = frame;
            Spatial witness = _source.Publish("current clock").gameObject.AddComponent<Spatial>();
            TestGhost target = _source.PublishPosition("stationary target", new Double3(30, 0, 0));
            foreach (float delay in new[] { 0f, 0.1f })
            {
                _realm.ResetSpatialTime();
                _realm.InterpolationDelay = delay;
                spatial.SetCartesianPosition(default(Double3), new Timestamp(0, 0));
                spatial.SetSourceRotation(Quaternion.identity, new Timestamp(0, 0));
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(100, 0, 0), new Timestamp(0, 0));
                extrapolation.GetComponent<Spatial>().ClearAcceleration();
                witness.SetCartesianPosition(default(Double3), new Timestamp(1, 0));
                _realm.Update();
                Assert.That(frame.Position.X, Is.EqualTo(10).Within(0.001));

                // These positions and velocities describe the same period of constant acceleration.
                // The new acceleration changes predicted movement without changing the position measurement error.
                Timestamp turnTime = new Timestamp(0, 20000000);
                Double3 position = new Double3(1.8, 0.2, 0);
                Double3 velocity = new Double3(80, 20, 0);
                Double3 acceleration = new Double3(-1000, 1000, 0);
                spatial.SetCartesianPosition(position, turnTime);
                spatial.SetSourceRotation(Quaternion.Euler(25, 0, 35), turnTime);
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(velocity, turnTime);
                extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(acceleration, turnTime);
                _realm.Update();
                double horizon = extrapolation.MaximumExtrapolation;
                Double3 expected = position + velocity * horizon + acceleration * (0.5 * horizon * horizon);
                Assert.That(Double3.Distance(frame.Position, expected), Is.LessThan(0.001),
                    "A known maneuver must not acquire the position filter's half-life lag.");
                Assert.That(frame.TryToUnityPosition(target.GetComponent<Spatial>().Position, out Vector3 expectedTarget), Is.True);
                AssertPosition(target.transform.position, expectedTarget, 0.001f);
                AssertPosition(reference.transform.position, frame.UnityPosition);
                Assert.That(spatial.Position, Is.EqualTo(position));

                Timestamp nextTime = new Timestamp(0, 40000000);
                Double3 nextVelocity = new Double3(60, 40, 0);
                Double3 cleanPosition = new Double3(3.2, 0.8, 0);
                spatial.SetCartesianPosition(cleanPosition + new Double3(0, 0, 2), nextTime);
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(nextVelocity, nextTime);
                extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(acceleration, nextTime);
                _realm.Update();
                Assert.That(frame.Position.Z, Is.LessThan(1), "Position noise must still be smoothed.");
                expected = cleanPosition + nextVelocity * horizon + acceleration * (0.5 * horizon * horizon);
                Assert.That(frame.Position.X, Is.EqualTo(expected.X).Within(0.001));
                Assert.That(frame.Position.Y, Is.EqualTo(expected.Y).Within(0.001));
            }
        }

        /// <summary>Independent timed motion is sampled at the position observation, and orientation gaps share the extrapolation cap with immediate or buffered playback.</summary>
        [Test]
        public void TimestampedExtrapolation_IndependentMotionAndOrientationShareThePoseTimeline()
        {
            // Later velocity can help interpolate earlier motion. Do not use acceleration with no sample at or before the pose.
            TestGhost ghost = _source.Publish("independent channels");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.05f;
            Spatial witness = _source.Publish("current clock").gameObject.AddComponent<Spatial>();
            foreach (float delay in new[] { 0f, 0.01f })
            {
                _realm.ResetSpatialTime();
                _realm.InterpolationDelay = delay;
                spatial.SetCartesianPosition(default(Double3), new Timestamp(2, 0));
                spatial.SetSourceRotation(Quaternion.identity, new Timestamp(1, 0));
                spatial.SetSourceRotation(Quaternion.AngleAxis(40, new Vector3(1, 0, 1).normalized), new Timestamp(2, 0));
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(default(Double3), new Timestamp(1, 0));
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(200, 0, 0), new Timestamp(3, 0));
                extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(new Double3(4000, 0, 0), new Timestamp(2, 10000000));
                witness.SetCartesianPosition(default(Double3), new Timestamp(4, 0));
                _realm.Update();
                AssertPosition(ghost.transform.position, new Vector3(5, 0, 0), 0.001f);
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(42, new Vector3(1, 0, 1).normalized));
                Assert.That(extrapolation.GetComponent<Spatial>().Velocity, Is.EqualTo(new Double3(200, 0, 0)), "Later input remains accepted without replacing earlier motion.");
                Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(new Double3(4000, 0, 0)));

                extrapolation.enabled = false;
                spatial.ResetPresentation();
                _realm.Update();
                AssertPosition(ghost.transform.position, Vector3.zero);
                AssertRotation(ghost.transform.rotation, spatial.Rotation);
                extrapolation.enabled = true;
            }
        }

        /// <summary>Rapid pitch, roll and combined maneuvers preserve near, far and off-axis target bearings with fresh or delayed packets; disabled prediction and reversed rates expose reference-angle errors.</summary>
        /// <returns>An iterator that compares three-dimensional motion strategies during continuous high-rate flight with changing vertical speed.</returns>
        [UnityTest]
        public IEnumerator TimestampedExtrapolation_HighRateReferenceMatchesIndependentTargetBearing()
        {
            // Turn around each axis to expose wrong signs. Then turn around all axes together to check their interaction.
            Double3[] bodyRates =
            {
                new Double3(0, Math.PI, 0),
                new Double3(4d * Math.PI, 0, 0),
                new Double3(2d * Math.PI, Math.PI, 0.5d * Math.PI)
            };
            IExtrapolationStrategy[] strategies =
            {
                new ConstantVelocityExtrapolation(), new ConstantAccelerationExtrapolation(),
                new SingerExtrapolation(), new CoordinatedTurnExtrapolation(), new SplineExtrapolation(), new ConstantTurnAccelerationExtrapolation()
            };
            foreach (IExtrapolationStrategy strategy in strategies)
            {
                foreach (Double3 rate in bodyRates)
                {
                    yield return AssertHighRateReference(strategy, rate);
                }
            }
        }

        /// <summary>Unix creation times expose the cached calibrated median packet age and compensate peers on one timeline, while other streams, clock changes and resets remain isolated.</summary>
        /// <returns>An iterator that delivers independently delayed simulation observations.</returns>
        [UnityTest]
        public IEnumerator ProjectedTime_UsesFollowedClockAcrossDelayedPeersAndRecoversAfterReset()
        {
            double started = Time.realtimeSinceStartupAsDouble - 0.2d;
            DateTime utcStarted = DateTime.UtcNow.AddSeconds(-0.2d);
            Spatial reference = _source.Publish("clock reference").gameObject.AddComponent<Spatial>();
            Spatial peer = _source.Publish("clock peer").gameObject.AddComponent<Spatial>();
            Spatial witness = _source.Publish("unrelated clock").gameObject.AddComponent<Spatial>();
            Extrapolation ownshipPrediction = reference.gameObject.AddComponent<Extrapolation>();
            Extrapolation peerPrediction = peer.gameObject.AddComponent<Extrapolation>();
            ownshipPrediction.MaximumExtrapolation = 0.5f;
            peerPrediction.MaximumExtrapolation = 0.5f;
            peerPrediction.PredictionTime = 0.4f;
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = reference.GetComponent<Ghost>().Key };
            _realm.ReferenceFrame = frame;
            Assert.That(frame.TimestampClock, Is.EqualTo(TimestampClock.Automatic));
            Assert.That(frame.PredictionTime, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.PredictionTime = -0.1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.PredictionTime = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.PredictionTime = float.PositiveInfinity);
            Assert.That(frame.PredictionTime, Is.Zero);
            Assert.That(frame.ClockOffset, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.ClockOffset = double.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.ClockOffset = double.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.ClockOffset = double.NegativeInfinity);
            Assert.That(frame.ClockOffset, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TimestampClock = (TimestampClock)999);
            Assert.That(frame.TimestampClock, Is.EqualTo(TimestampClock.Automatic));

            Assert.That(_realm.ProjectedTime, Is.Null);
            Assert.That(_realm.EstimatedPacketAge, Is.Null);

            for (int index = 0; index < 35; index++)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - started;
                double ownshipTime = elapsed - (index % 7 == 6 ? 0.044d : 0.04d);
                double peerTime = elapsed - 0.09d;
                Timestamp ownshipStamp = UnixTime(utcStarted.AddSeconds(ownshipTime));
                Timestamp peerStamp = UnixTime(utcStarted.AddSeconds(peerTime));
                reference.SetCartesianPosition(new Double3(10d * ownshipTime, 0, 0), ownshipStamp);
                ownshipPrediction.GetComponent<Spatial>().SetCartesianVelocity(new Double3(10, 0, 0), ownshipStamp);
                peer.SetCartesianPosition(new Double3(100d + 10d * peerTime, 0, 0), peerStamp);
                peerPrediction.GetComponent<Spatial>().SetCartesianVelocity(new Double3(10, 0, 0), peerStamp);
                witness.SetCartesianPosition(default(Double3), UnixTime(utcStarted.AddSeconds(elapsed + 10)));
                double before = Time.realtimeSinceStartupAsDouble;
                _realm.Update();
                double after = Time.realtimeSinceStartupAsDouble;
                if (index > 15)
                {
                    Assert.That(_realm.EstimatedPacketAge.Value, Is.EqualTo(0.04d).Within(0.0025d),
                        "The diagnostic must report the reference's median receipt age, excluding peer ages, manual lead and isolated slow packets.");
                    double projected = _realm.ProjectedTime.Value.ElapsedSince(UnixTime(utcStarted));
                    Assert.That(projected, Is.EqualTo((before + after) * 0.5d - started)
                        .Within(0.002d + after - before), "The median rejects isolated receipt-offset outliers and ignores the unrelated clock.");
                    Assert.That(peer.transform.position.x, Is.EqualTo(100).Within(0.01),
                        "Both aircraft must reach one simulation time despite different packet ages and peer forward settings.");
                }

                double? cachedAge = _realm.EstimatedPacketAge;
                yield return new WaitForSecondsRealtime(0.01f);
                Assert.That(_realm.EstimatedPacketAge, Is.EqualTo(cachedAge),
                    "Reading the cached diagnostic between projection passes must not advance packet age.");
            }

            // Correct either sign once for every entity, even when no new packet arrives.
            // Negative edits must not be held by the old timeline floor.
            Timestamp originalReferenceTime = reference.PositionTime.Value;
            foreach (double offset in new[] { 0.08d, -0.025d, 0d })
            {
                frame.ClockOffset = offset;
                Timestamp before = UnixTime(DateTime.UtcNow);
                _realm.Update();
                Timestamp after = UnixTime(DateTime.UtcNow);
                double adjusted = _realm.ProjectedTime.Value.ElapsedSince(before);
                Assert.That(adjusted, Is.InRange(offset - 0.001d, offset + after.ElapsedSince(before) + 0.001d));
                double projected = _realm.ProjectedTime.Value.ElapsedSince(UnixTime(utcStarted));
                Assert.That(_realm.ReferenceFrame.Position.X, Is.EqualTo(10d * projected).Within(0.01d));
                Assert.That(peer.transform.position.x, Is.EqualTo(100).Within(0.01d));
                Assert.That(reference.PositionTime, Is.EqualTo(originalReferenceTime), "Calibration must retain raw source timestamps.");
                Assert.That(_realm.EstimatedPacketAge.Value, Is.EqualTo(0.044d + offset).Within(0.003d),
                    "A calibration reset must use the cached reference's receipt age and apply clock correction once.");
            }

            // The reference's forward interval applies to all peers. Each entity still enforces its own cap.
            frame.PredictionTime = 0.12f;
            reference.ResetPresentation();
            peer.ResetPresentation();
            _realm.Update();
            Assert.That(peer.transform.position.x, Is.EqualTo(100).Within(0.02));
            peerPrediction.MaximumExtrapolation = 0.01f;
            peer.ResetPresentation();
            _realm.Update();
            Double3 capped = _realm.ReferenceFrame.ToSimulationPosition(peer.transform.position);
            Assert.That(capped.X, Is.EqualTo(peer.Position.X + 0.1d).Within(0.001));

            // Changing the followed root discards its predecessor's median, including repeated cached observations.
            frame.TimestampClock = TimestampClock.Unix;
            _realm.ReferenceFrame.FollowedGhost = peer.GetComponent<Ghost>().Key;
            _realm.Update();
            Assert.That(_realm.ProjectedTime.Value.ElapsedSince(peer.PositionTime.Value), Is.InRange(0.399d, 0.6d),
                "The new followed Ghost's positive fallback must become the Realm's manual override.");
            Assert.That(_realm.EstimatedPacketAge.Value, Is.EqualTo(0.09d).Within(0.003d));
            _realm.ResetSpatialTime();
            Assert.That(_realm.ProjectedTime, Is.Null);
            Assert.That(_realm.EstimatedPacketAge, Is.Null);
            peerPrediction.PredictionTime = 0;
            frame.TimestampClock = TimestampClock.Simulation;
            frame.ClockOffset = 0.25d;
            frame.PredictionTime = 0.4f;
            peer.SetCartesianPosition(new Double3(4, 0, 0), new Timestamp(5, 123456789));
            _realm.Update();
            Assert.That(_realm.EstimatedPacketAge, Is.Null, "Simulation-relative clocks cannot measure absolute delivery age.");
            Assert.That(_realm.ProjectedTime.Value.Seconds, Is.EqualTo(5));
            Assert.That(_realm.ProjectedTime.Value.Nanoseconds, Is.GreaterThanOrEqualTo(523456000));
            Assert.That(_realm.ProjectedTime.Value.ElapsedSince(new Timestamp(5, 123456789)), Is.EqualTo(0.4d).Within(0.01d),
                "Simulation-relative alignment already absorbs clock origins and must ignore UTC calibration.");
            UnityEngine.Object.DestroyImmediate(peerPrediction);
            _realm.Update();
            Assert.That(_realm.ProjectedTime.Value.ElapsedSince(new Timestamp(5, 123456789)), Is.EqualTo(0.4d).Within(0.01d),
                "Reference timing must work without a followed Extrapolation trait.");
            _realm.Dispose();
            Assert.That(_realm.ProjectedTime, Is.Null);
            Assert.That(_realm.EstimatedPacketAge, Is.Null);
        }

        /// <summary>The followed Ghost switches the whole Realm between manual receipt-based prediction and automatic Unix timing, including lower overrides, newer peer histories and per-entity caps.</summary>
        /// <returns>An iterator that changes timing modes while delivering delayed reference and peer observations.</returns>
        [UnityTest]
        public IEnumerator FollowedPredictionTime_ReplacesAutomaticTimingAndZeroRestoresIt()
        {
            Timestamp origin = UnixTime(DateTime.UtcNow.AddSeconds(-10));
            Spatial reference = _source.Publish("manual reference").gameObject.AddComponent<Spatial>();
            Spatial peer = _source.Publish("manual peer").gameObject.AddComponent<Spatial>();
            Extrapolation referencePrediction = reference.gameObject.AddComponent<Extrapolation>();
            Extrapolation peerPrediction = peer.gameObject.AddComponent<Extrapolation>();
            referencePrediction.MaximumExtrapolation = 0.5f;
            peerPrediction.MaximumExtrapolation = 0.5f;
            peerPrediction.PredictionTime = 0.4f;
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = reference.GetComponent<Ghost>().Key, FollowRotation = false };
            _realm.ReferenceFrame = frame;

            void Supply(Spatial spatial, Extrapolation prediction, Timestamp time, double separation)
            {
                double seconds = time.ElapsedSince(origin);
                spatial.SetCartesianPosition(new Double3(separation + 100 * seconds, 0, 0), time);
                spatial.SetSourceRotation(Quaternion.AngleAxis((float)(30 * seconds), Vector3.up), time);
                prediction.GetComponent<Spatial>().SetCartesianVelocity(new Double3(100, 0, 0), time);
                prediction.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI / 6, 0), CoordinateSystem.Unity, time);
            }

            foreach (float interval in new[] { 0.06f, 0.02f, 0f, 0.3f, 0f })
            {
                referencePrediction.PredictionTime = interval;
                // The override must replace both automatic age and an optional code lead.
                frame.PredictionTime = interval > 0 ? 0.4f : 0;
                DateTime created = DateTime.UtcNow.AddSeconds(-0.2d);
                Timestamp sample = UnixTime(created);
                double receivedBefore = Time.realtimeSinceStartupAsDouble;
                Supply(reference, referencePrediction, sample, 0);
                // A newer peer must sample its history when manual time precedes its latest packet.
                Supply(peer, peerPrediction, UnixTime(created.AddSeconds(-0.1d)), 50);
                Supply(peer, peerPrediction, UnixTime(created.AddSeconds(0.15d)), 50);
                reference.ResetPresentation();
                Timestamp utcBefore = UnixTime(DateTime.UtcNow);
                _realm.Update();
                double receivedAfter = Time.realtimeSinceStartupAsDouble;
                Timestamp utcAfter = UnixTime(DateTime.UtcNow);
                Timestamp projected = _realm.ProjectedTime.Value;
                if (interval > 0)
                {
                    Assert.That(projected.ElapsedSince(sample),
                        Is.InRange(interval - 0.001d, interval + receivedAfter - receivedBefore + 0.001d),
                        "A manual interval must replace automatic compensation, including when it is smaller than packet age.");
                }
                else
                {
                    Assert.That(projected.ElapsedSince(utcBefore), Is.InRange(-0.002d, utcAfter.ElapsedSince(utcBefore) + 0.002d),
                        "Returning the followed override to zero must restore automatic UTC timing.");
                }

                double target = projected.ElapsedSince(origin);
                Assert.That(frame.Position.X, Is.EqualTo(100 * target).Within(0.02d));
                Assert.That(peer.transform.position.x, Is.EqualTo(50).Within(0.03d),
                    "A peer's own override must not split the shared timeline, even when its latest observation is newer than the manual target.");
                AssertRotation(peer.transform.rotation, Quaternion.AngleAxis((float)(30 * target), Vector3.up));
                Assert.That(_realm.EstimatedPacketAge.Value, Is.EqualTo(0.2d).Within(0.003d),
                    "Manual timing retains the measured packet age without applying it to the target.");
                yield return new WaitForSecondsRealtime(0.01f);
            }

            referencePrediction.PredictionTime = 0.3f;
            peerPrediction.MaximumExtrapolation = 0.007f;
            reference.ResetPresentation();
            peer.ResetPresentation();
            _realm.Update();
            Double3 capped = frame.ToSimulationPosition(peer.transform.position);
            Assert.That(capped.X, Is.EqualTo(peer.Position.X + 0.7d).Within(0.02d));

            referencePrediction.enabled = false;
            Timestamp beforeDisable = UnixTime(DateTime.UtcNow);
            _realm.Update();
            Timestamp afterDisable = UnixTime(DateTime.UtcNow);
            Assert.That(_realm.ProjectedTime.Value.ElapsedSince(beforeDisable),
                Is.InRange(-0.002d, afterDisable.ElapsedSince(beforeDisable) + 0.002d),
                "Disabling the followed prediction trait must restore automatic clock timing.");
        }

        /// <summary>Motion-assisted angular smoothing follows constant turns without extra lead, while half-life filtering still reduces packet correction steps.</summary>
        /// <returns>An iterator that compares corrected motion with and without half-life filters.</returns>
        [UnityTest]
        public IEnumerator Smoothing_SharedPredictionTracksTurnsAndFiltersPacketCorrections()
        {
            foreach (IExtrapolationStrategy strategy in new IExtrapolationStrategy[]
                { new ConstantVelocityExtrapolation(), new ConstantAccelerationExtrapolation(), new SingerExtrapolation(), new CoordinatedTurnExtrapolation(), new SplineExtrapolation(), new ConstantTurnAccelerationExtrapolation(), new ConstantTurnClimbAccelerationExtrapolation() })
            {
                using (Realm realm = new Realm())
                {
                    TestSource source = new TestSource();
                    realm.GetOrCreateAnchor("motion", source);
                    Spatial reference = source.Publish("turn reference").gameObject.AddComponent<Spatial>();
                    Extrapolation referencePrediction = reference.gameObject.AddComponent<Extrapolation>();
                    referencePrediction.Strategy = strategy;
                    Assert.That(referencePrediction.PredictionTime, Is.Zero);
                    referencePrediction.MaximumExtrapolation = 0.3f;
                    Smoothing referenceSmoothing = reference.gameObject.AddComponent<Smoothing>();
                    referenceSmoothing.RotationHalfLife = 0.25f;
                    Spatial[] peers = new Spatial[2];
                    Extrapolation[] predictions = new Extrapolation[2];
                    for (int index = 0; index < peers.Length; index++)
                    {
                        peers[index] = source.Publish("peer " + index).gameObject.AddComponent<Spatial>();
                        predictions[index] = peers[index].gameObject.AddComponent<Extrapolation>();
                        predictions[index].Strategy = strategy;
                        predictions[index].MaximumExtrapolation = 0.3f;
                    }
                    peers[1].gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.25f;
                    realm.ReferenceFrame = new ReferenceFrame { FollowedGhost = reference.GetComponent<Ghost>().Key };
                    double started = Time.realtimeSinceStartupAsDouble - 0.2d;
                    DateTime utcStarted = DateTime.UtcNow.AddSeconds(-0.2d);
                    double worstAngle = 0;
                    double[] correction = new double[2];
                    double elapsed;
                    do
                    {
                        elapsed = Time.realtimeSinceStartupAsDouble - started;
                        double captured = elapsed - 0.06d;
                        Timestamp stamp = UnixTime(utcStarted.AddSeconds(captured));
                        reference.SetCartesianPosition(default(Double3), stamp);
                        reference.SetSourceRotation(Quaternion.AngleAxis((float)(100d * captured), Vector3.forward), stamp);
                        referencePrediction.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, 100d * Math.PI / 180d), CoordinateSystem.Unity, stamp);
                        for (int index = 0; index < peers.Length; index++)
                        {
                            double peerTime = captured - 0.04d;
                            Timestamp peerStamp = UnixTime(utcStarted.AddSeconds(peerTime));
                            peers[index].SetCartesianPosition(new Double3(500d + 20d * peerTime + (elapsed > 0.5d ? 10 : 0), 0, 2000), peerStamp);
                            predictions[index].GetComponent<Spatial>().SetCartesianVelocity(new Double3(20, 0, 0), peerStamp);
                        }
                        realm.Update();
                        double projected = realm.ProjectedTime.Value.ElapsedSince(UnixTime(utcStarted));
                        if (elapsed > 0.35d)
                        {
                            worstAngle = Math.Max(worstAngle, Quaternion.Angle(realm.ReferenceFrame.Rotation,
                                Quaternion.AngleAxis((float)(100d * projected), Vector3.forward)));
                        }
                        if (elapsed > 0.56d)
                        {
                            for (int index = 0; index < peers.Length; index++)
                            {
                                correction[index] = realm.ReferenceFrame.ToSimulationPosition(peers[index].transform.position).X - (500d + 20d * projected);
                            }
                            break;
                        }
                        yield return new WaitForSecondsRealtime(1f / 90f);
                    }
                    while (elapsed < 0.8d);

                    TestContext.WriteLine(strategy.GetType().Name + "; Unix 60/100 ms packet ages, no manual lead, 250 ms rotation half-life: max turn error="
                        + worstAngle + " deg; correction without/with Smoothing=" + correction[0] + "/" + correction[1] + " m");
                    Assert.That(worstAngle, Is.LessThan(0.35d), strategy.GetType().Name + " must not require more lead to cancel smoothing lag.");
                    Assert.That(correction[0], Is.GreaterThan(1d));
                    Assert.That(correction[1], Is.GreaterThan(0d).And.LessThan(correction[0] * 0.7d),
                        strategy.GetType().Name + " must filter correction error even when cubic packet correction is active.");
                }
            }
        }

        /// <summary>Pose-derived angular motion converges during steady turns while attitude corrections still pass through the half-life filter.</summary>
        /// <returns>An iterator that supplies delayed Unix poses without SDK angular rates.</returns>
        [UnityTest]
        public IEnumerator Smoothing_EstimatedAngularMotionRetainsCorrectionFiltering()
        {
            Spatial reference = _source.Publish("estimated turn").gameObject.AddComponent<Spatial>();
            Extrapolation prediction = reference.gameObject.AddComponent<Extrapolation>();
            prediction.MaximumExtrapolation = 0.3f;
            Smoothing smoothing = reference.gameObject.AddComponent<Smoothing>();
            smoothing.RotationHalfLife = 0.2f;
            _realm.ReferenceFrame = new ReferenceFrame { FollowedGhost = reference.GetComponent<Ghost>().Key };
            double started = Time.realtimeSinceStartupAsDouble;
            DateTime utcStarted = DateTime.UtcNow;
            double worstSteadyError = 0;
            double correction = 0;
            double elapsed;
            do
            {
                elapsed = Time.realtimeSinceStartupAsDouble - started;
                double captured = elapsed - 0.06d;
                Timestamp stamp = UnixTime(utcStarted.AddSeconds(captured));
                reference.SetCartesianPosition(default(Double3), stamp);
                reference.SetSourceRotation(Quaternion.AngleAxis((float)(90d * captured + (elapsed > 1.3d ? 5 : 0)), Vector3.forward), stamp);
                _realm.Update();
                double projected = _realm.ProjectedTime.Value.ElapsedSince(UnixTime(utcStarted));
                double angle = Quaternion.Angle(_realm.ReferenceFrame.Rotation, Quaternion.AngleAxis((float)(90d * projected), Vector3.forward));
                if (elapsed > 1.1d && elapsed < 1.3d)
                {
                    worstSteadyError = Math.Max(worstSteadyError, angle);
                }
                if (elapsed > 1.36d)
                {
                    correction = angle;
                    break;
                }
                yield return new WaitForSecondsRealtime(1f / 90f);
            }
            while (elapsed < 1.6d);

            TestContext.WriteLine("Estimated Unix turn; zero manual lead: steady error=" + worstSteadyError
                + " deg; accepted 5-degree correction after 60 ms=" + correction + " deg");
            Assert.That(worstSteadyError, Is.LessThan(0.35d));
            Assert.That(correction, Is.GreaterThan(0.1d).And.LessThan(4.5d),
                "Estimating rates from noisy poses must not bypass angular correction filtering.");
        }

        /// <summary>All moving strategies apply requested forward time during a constant bank with uniform packet latency, count known age once, and respect buffering, caps and disabling.</summary>
        /// <returns>An iterator that delivers delayed geographic attitudes at approximately 60 Hz.</returns>
        [UnityTest]
        public IEnumerator TimestampedExtrapolation_ConstantLatencyBankMatchesCurrentTargetBearing()
        {
            IExtrapolationStrategy[] strategies =
            {
                new ConstantVelocityExtrapolation(), new ConstantAccelerationExtrapolation(),
                new SingerExtrapolation(), new CoordinatedTurnExtrapolation(), new SplineExtrapolation(), new ConstantTurnAccelerationExtrapolation(), new ConstantTurnClimbAccelerationExtrapolation()
            };
            foreach (IExtrapolationStrategy strategy in strategies)
            {
                // Both streams are delayed equally. No fresh witness is allowed to reveal the hidden latency.
                GeoPosition origin = new GeoPosition(52, 13, 1000);
                Realm[] realms = { new Realm(), new Realm() };
                TestSource[] sources = { new TestSource(), new TestSource() };
                Spatial[] references = new Spatial[2];
                Spatial[] targets = new Spatial[2];
                Extrapolation[,] predictions = new Extrapolation[2, 2];

                double[] maximumBearingError = new double[2];
                double[] maximumReferenceError = new double[2];
                double[] maximumLinearError = new double[2];
                try
                {
                    for (int variant = 0; variant < realms.Length; variant++)
                    {
                        realms[variant].GetOrCreateAnchor("simulation", sources[variant]);
                        TestGhost reference = sources[variant].PublishEarthCenteredPosition("delayed ownship", origin.ToEarthCentered());
                        references[variant] = reference.GetRequired<Spatial>();
                        targets[variant] = sources[variant].Publish("moving target").gameObject.AddComponent<Spatial>();
                        predictions[variant, 0] = reference.gameObject.AddComponent<Extrapolation>();
                        predictions[variant, 1] = targets[variant].gameObject.AddComponent<Extrapolation>();
                        for (int channel = 0; channel < 2; channel++)
                        {
                            predictions[variant, channel].MaximumExtrapolation = 0.2f;
                            predictions[variant, channel].Strategy = strategy;
                            Assert.That(predictions[variant, channel].PredictionTime, Is.Zero);
                            predictions[variant, channel].PredictionTime = variant == 0 ? 0 : 0.1f;
                        }
                        realms[variant].ReferenceFrame = new ReferenceFrame { Space = ReferenceSpace.Geographic, FollowedGhost = reference.Key, PredictionTime = variant == 0 ? 0 : 0.1f };
                    }

                    Assert.Throws<ArgumentOutOfRangeException>(() => predictions[1, 0].PredictionTime = -0.1f);
                    Assert.Throws<ArgumentOutOfRangeException>(() => predictions[1, 0].PredictionTime = float.NaN);
                    Assert.Throws<ArgumentOutOfRangeException>(() => predictions[1, 0].PredictionTime = float.PositiveInfinity);
                    Assert.That(predictions[1, 0].PredictionTime, Is.EqualTo(0.1f));
                    double started = Time.realtimeSinceStartupAsDouble - 0.2d;
                    double elapsed;
                    do
                    {
                        elapsed = Time.realtimeSinceStartupAsDouble - started;
                        double captured = elapsed - 0.1d;
                        Timestamp sampleTime = Timestamp.FromSeconds(1000d + captured);
                        for (int variant = 0; variant < realms.Length; variant++)
                        {
                            references[variant].SetGeographicRotation(0, 0, 180d * captured, sampleTime);
                            predictions[variant, 0].GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(Math.PI, 0, 0), sampleTime: sampleTime);
                            targets[variant].SetEarthCenteredPosition(origin.ToEarthCentered()
                                + origin.ToEarthCenteredVector(new Double3(500d + 20d * captured, 2000, 0)), sampleTime);
                            predictions[variant, 1].GetComponent<Spatial>().SetEarthCenteredVelocity(origin.ToEarthCenteredVector(new Double3(20, 0, 0)), sampleTime);
                            double before = Time.realtimeSinceStartupAsDouble;
                            realms[variant].Update();
                            double after = Time.realtimeSinceStartupAsDouble;
                            double now = (before + after) * 0.5d - started;
                            double bank = Math.PI * now;
                            Vector3 expected = new Vector3((float)((500d + 20d * now) * Math.Cos(bank)),
                                (float)((500d + 20d * now) * Math.Sin(bank)), 2000);
                            Quaternion expectedRotation = Quaternion.AngleAxis((float)(-180d * now), Vector3.forward);
                            Double3 expectedPosition = origin.ToEarthCentered()
                                + origin.ToEarthCenteredVector(new Double3(500d + 20d * now, 2000, 0));
                            if (elapsed > 0.35d)
                            {
                                double timing = after - before;
                                maximumBearingError[variant] = Math.Max(maximumBearingError[variant],
                                    Vector3.Angle(expected, targets[variant].transform.position) - 180d * timing);
                                maximumReferenceError[variant] = Math.Max(maximumReferenceError[variant],
                                    Quaternion.Angle(expectedRotation, realms[variant].ReferenceFrame.Rotation) - 180d * timing);
                                maximumLinearError[variant] = Math.Max(maximumLinearError[variant],
                                    Double3.Distance(expectedPosition, realms[variant].ReferenceFrame.ToSimulationPosition(targets[variant].transform.position)) - 20d * timing);
                            }
                        }
                        yield return new WaitForSecondsRealtime(1f / 60f);
                    }
                    while (elapsed < 0.7d);

                    TestContext.WriteLine(strategy.GetType().Name + "; uniform 100 ms latency, 180 deg/s bank: ordinary/forward prediction bearing="
                        + maximumBearingError[0] + "/" + maximumBearingError[1] + " deg; reference="
                        + maximumReferenceError[0] + "/" + maximumReferenceError[1] + " deg; position="
                        + maximumLinearError[0] + "/" + maximumLinearError[1] + " m");
                    Assert.That(maximumReferenceError[0], Is.GreaterThan(15d));
                    Assert.That(maximumBearingError[0], Is.GreaterThan(3d));
                    Assert.That(maximumLinearError[0], Is.GreaterThan(1.5d));
                    Assert.That(maximumReferenceError[1], Is.LessThan(0.25d));
                    Assert.That(maximumBearingError[1], Is.LessThan(0.25d));
                    Assert.That(maximumLinearError[1], Is.LessThan(0.05d));

                    // A playback delay consumes lead first. The remaining 100 ms still compensates this stream.
                    for (int channel = 0; channel < 2; channel++)
                    {
                        predictions[1, channel].PredictionTime = 0.15f;
                    }
                    realms[1].ReferenceFrame.PredictionTime = 0.15f;
                    realms[1].InterpolationDelay = 0.05f;
                    references[1].ResetPresentation();
                    targets[1].ResetPresentation();
                    double bufferedBefore = Time.realtimeSinceStartupAsDouble;
                    realms[1].Update();
                    double bufferedAfter = Time.realtimeSinceStartupAsDouble;
                    Quaternion current = Quaternion.AngleAxis((float)(-180d * ((bufferedBefore + bufferedAfter) * 0.5d - started)), Vector3.forward);
                    Assert.That(Quaternion.Angle(current, realms[1].ReferenceFrame.Rotation),
                        Is.LessThan(0.25d + 180d * (bufferedAfter - bufferedBefore)));

                    // Compensation must respect the horizon and stop entirely when its trait is disabled.
                    for (int channel = 0; channel < 2; channel++)
                    {
                        predictions[1, channel].MaximumExtrapolation = 0.04f;
                    }
                    references[1].ResetPresentation();
                    targets[1].ResetPresentation();
                    realms[1].Update();
                    Assert.That(Quaternion.Angle(references[1].Rotation, realms[1].ReferenceFrame.Rotation), Is.EqualTo(7.2d).Within(0.05d));
                    Assert.That(Double3.Distance(targets[1].Position, realms[1].ReferenceFrame.ToSimulationPosition(targets[1].transform.position)),
                        Is.EqualTo(0.8d).Within(0.01d));
                    realms[1].InterpolationDelay = 0;
                    for (int channel = 0; channel < 2; channel++)
                    {
                        predictions[1, channel].enabled = false;
                    }
                    references[1].ResetPresentation();
                    targets[1].ResetPresentation();
                    realms[1].Update();
                    AssertRotation(realms[1].ReferenceFrame.Rotation, references[1].Rotation);
                    Assert.That(Double3.Distance(targets[1].Position,
                        realms[1].ReferenceFrame.ToSimulationPosition(targets[1].transform.position)), Is.LessThan(0.01d));

                    // A packet at SDK time 0.2 with 0.2 seconds of forward prediction targets time 0.4.
                    realms[1].ResetSpatialTime();
                    Timestamp packetTime = new Timestamp(0, 200000000);
                    for (int channel = 0; channel < 2; channel++)
                    {
                        predictions[1, channel].enabled = true;
                        predictions[1, channel].MaximumExtrapolation = 0.5f;
                        predictions[1, channel].PredictionTime = 0.2f;
                    }
                    realms[1].ReferenceFrame.PredictionTime = 0.2f;
                    double packetReceivedBefore = Time.realtimeSinceStartupAsDouble;
                    references[1].SetGeographicRotation(0, 0, 36, packetTime);
                    double packetReceivedAfter = Time.realtimeSinceStartupAsDouble;
                    predictions[1, 0].GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(Math.PI, 0, 0), sampleTime: packetTime);
                    targets[1].SetEarthCenteredPosition(origin.ToEarthCentered()
                        + origin.ToEarthCenteredVector(new Double3(2, 2000, 0)), packetTime);
                    predictions[1, 1].GetComponent<Spatial>().SetEarthCenteredVelocity(origin.ToEarthCenteredVector(new Double3(10, 0, 0)), packetTime);
                    // Let time pass after receipt. Prediction must include that elapsed
                    // time as well as the requested interval, even before Update starts.
                    yield return new WaitForSecondsRealtime(0.01f);
                    // Knowing that delivery is at SDK time 0.3 must not add that 0.1 seconds twice.
                    sources[1].PublishEarthCenteredPosition("delivery clock", origin.ToEarthCentered()).GetRequired<Spatial>()
                        .SetEarthCenteredPosition(origin.ToEarthCentered(), new Timestamp(0, 300000000));
                    double packetBefore = Time.realtimeSinceStartupAsDouble;
                    realms[1].Update();
                    double packetAfter = Time.realtimeSinceStartupAsDouble;
                    // Bound receipt and projection with public clock reads. Measuring only
                    // Update omits data-setting time and can reject correct clock progress.
                    double minimumTarget = 0.4d + packetBefore - packetReceivedAfter;
                    double maximumTarget = 0.4d + packetAfter - packetReceivedBefore;
                    double target = (minimumTarget + maximumTarget) * 0.5d;
                    double uncertainty = (maximumTarget - minimumTarget) * 0.5d;
                    Assert.That(Quaternion.Angle(realms[1].ReferenceFrame.Rotation,
                        Quaternion.AngleAxis((float)(-180d * target), Vector3.forward)),
                        Is.LessThan(0.25d + 180d * uncertainty),
                        strategy.GetType().Name + "; projected=" + realms[1].ProjectedTime
                        + "; expected=" + minimumTarget + " to " + maximumTarget);
                    Double3 expectedPacketPosition = origin.ToEarthCentered()
                        + origin.ToEarthCenteredVector(new Double3(2d + 10d * (target - 0.2d), 2000, 0));
                    Assert.That(Double3.Distance(expectedPacketPosition, realms[1].ReferenceFrame.ToSimulationPosition(targets[1].transform.position)),
                        Is.LessThan(0.02d + 10d * uncertainty));
                }
                finally
                {
                    foreach (Realm realm in realms)
                    {
                        realm.Dispose();
                    }
                }
            }
        }

        /// <summary>One timed attitude and supplied aircraft body rates predict pitch and roll with the same horizon in Cartesian, geographic and body-to-ECEF rotation conventions.</summary>
        [Test]
        public void BodyAngularVelocity_PredictsSingleAttitudesAcrossRotationConventions()
        {
            // One rotation and body pitch rate must give the same predicted rotation with either Cartesian axis setting.
            TestGhost ghost = _source.Publish("angular input");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.2f;
            Spatial witness = _source.Publish("current angular clock").gameObject.AddComponent<Spatial>();
            Timestamp sampleTime = new Timestamp(2, 0);
            Quaternion attitude = Quaternion.Euler(15, 25, -20);
            foreach (CoordinateSystem coordinates in new[] { CoordinateSystem.Unity, CoordinateSystem.NorthEastDown })
            {
                _realm.ResetSpatialTime();
                ReferenceFrame frame = new ReferenceFrame { Coordinates = coordinates, Position = default(Double3) };
                _realm.ReferenceFrame = frame;
                spatial.SetCartesianPosition(default(Double3), sampleTime);
                spatial.SetSourceRotation(frame.ToSimulationRotation(attitude), sampleTime);
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI / 2, 0), sampleTime: sampleTime);
                witness.SetCartesianPosition(default(Double3), new Timestamp(3, 0));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, attitude * Quaternion.AngleAxis(-18, Vector3.right));
                Assert.That(extrapolation.GetComponent<Spatial>().AngularVelocity, Is.EqualTo(new Double3(-Math.PI / 2, 0, 0)));
                Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.True);
                AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
            }

            // The same body roll must work for both geographic and ECEF rotations.
            GeoPosition origin = new GeoPosition(52, 13, 40);
            foreach (bool earthCentered in new[] { false, true })
            {
                _realm.ResetSpatialTime();
                ReferenceFrame frame = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = origin };
                _realm.ReferenceFrame = frame;
                spatial.SetGeographicPosition(origin, sampleTime);
                if (earthCentered)
                {
                    spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(attitude), sampleTime: sampleTime);
                }
                else
                {
                    spatial.SetGeographicRotation(25, 15, -20, sampleTime);
                    attitude = spatial.Rotation;
                }

                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(Math.PI / 2, 0, 0), sampleTime: sampleTime);
                witness.SetGeographicPosition(origin, new Timestamp(3, 0));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, attitude * Quaternion.AngleAxis(-18, Vector3.forward));
                AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
            }
        }

        /// <summary>ENU world rates use their explicit tangent origin, act before a non-level attitude in every geographic rotation convention, and keep body/world samples separate across frame changes.</summary>
        [Test]
        public void WorldAngularVelocity_UsesEnuAndEcefBeforeTheAttitudeAcrossRotationConventions()
        {
            TestGhost ghost = _source.Publish("world angular input");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.2f;
            Spatial witness = _source.Publish("world angular clock").gameObject.AddComponent<Spatial>();
            GeoPosition position = new GeoPosition(0, 0, 40);
            GeoPosition sdkOrigin = new GeoPosition(0, 90, 0);
            Double3 enuRate = new Double3(0.1, 0.2, 0.3);
            Double3 earthRate = new Double3(-0.1, 0.3, 0.2);
            Quaternion attitude = Quaternion.AngleAxis(70, Vector3.up)
                * Quaternion.AngleAxis(-35, Vector3.right) * Quaternion.AngleAxis(-40, Vector3.forward);
            // At (0, 0), ECEF angular rate (-0.1, 0.3, 0.2) becomes Unity rate (-0.3, 0.1, -0.2).
            Vector3 worldAxis = new Vector3(-0.3f, 0.1f, -0.2f);
            Quaternion displacement = Quaternion.AngleAxis(worldAxis.magnitude * 0.2f * Mathf.Rad2Deg, worldAxis);
            Quaternion expected = displacement * attitude;
            Assert.That(Quaternion.Angle(expected, attitude * displacement), Is.GreaterThan(1f),
                "The non-level attitude must distinguish world composition from body composition.");
            Timestamp sampleTime = new Timestamp(2, 0);
            foreach (RotationSpace space in new[] { RotationSpace.Source, RotationSpace.Geographic, RotationSpace.EarthCentered })
            {
                _realm.ResetSpatialTime();
                ReferenceFrame frame = new ReferenceFrame
                {
                    Space = ReferenceSpace.Geographic,
                    Coordinates = CoordinateSystem.NorthEastDown,
                    GeographicPosition = position
                };
                _realm.ReferenceFrame = frame;
                // Send motion before pose to check that reading order does not tie motion to an old rotation.
                extrapolation.GetComponent<Spatial>().SetGeographicAngularVelocity(enuRate, sdkOrigin, sampleTime);
                spatial.SetGeographicPosition(position, sampleTime);
                if (space == RotationSpace.Source)
                {
                    spatial.SetSourceRotation(frame.ToSimulationRotation(attitude), sampleTime);
                }
                else if (space == RotationSpace.Geographic)
                {
                    spatial.SetGeographicRotation(70, 35, 40, sampleTime);
                }
                else
                {
                    spatial.SetEarthCenteredRotation(frame.ToEarthCenteredRotation(attitude, CoordinateSystem.EastNorthUp),
                        CoordinateSystem.EastNorthUp, sampleTime);
                }

                witness.SetGeographicPosition(position, new Timestamp(3, 0));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, expected);
                Assert.That(Double3.Distance(extrapolation.GetComponent<Spatial>().AngularVelocity, earthRate), Is.LessThan(1e-12));
                Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.True);
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(100, 0, 0), sampleTime: sampleTime);
                _realm.Update();
                AssertRotation(ghost.transform.rotation, expected);
                Assert.That(Double3.Distance(extrapolation.GetComponent<Spatial>().AngularVelocity, earthRate), Is.LessThan(1e-12),
                    "A duplicate sample cannot change the accepted rate's reference frame.");
                extrapolation.GetComponent<Spatial>().SetEarthCenteredAngularVelocity(earthRate, new Timestamp(2, 100000000));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, expected);
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    extrapolation.GetComponent<Spatial>().SetGeographicAngularVelocity(new Double3(double.NaN, 0, 0), sdkOrigin));
                Assert.That(Double3.Distance(extrapolation.GetComponent<Spatial>().AngularVelocity, earthRate), Is.LessThan(1e-12));

                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(Math.PI / 2, 0, 0), sampleTime: new Timestamp(2, 200000000));
                _realm.Update();
                Assert.That(Quaternion.Angle(ghost.transform.rotation, attitude), Is.LessThan(0.05f),
                    "A new body-only history must not interpolate with the former world-rate history.");
                extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
                Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.False);
            }
        }

        /// <summary>Angular samples use the rotation's own time with immediate or buffered playback, reject stale rates, allow zero overrides and clear back to rotation-history estimation after recovery.</summary>
        [Test]
        public void BodyAngularVelocity_UsesRotationTimeAndRecoversAfterClearingAndClockReset()
        {
            // Check live and delayed playback when rotation and angular rates have separate timestamps.
            TestGhost ghost = _source.Publish("angular ordering");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.05f;
            Spatial witness = _source.Publish("current angular clock").gameObject.AddComponent<Spatial>();
            foreach (float delay in new[] { 0f, 0.1f })
            {
                _realm.ResetSpatialTime();
                _realm.InterpolationDelay = delay;
                spatial.SetCartesianPosition(default(Double3), new Timestamp(2, 0));
                // Keep a later zero rate as raw input, but predict using the rate available at the rotation time.
                spatial.SetSourceRotation(Quaternion.identity, new Timestamp(1, 0));
                Quaternion rotation = Quaternion.AngleAxis(40, Vector3.forward);
                spatial.SetSourceRotation(rotation, new Timestamp(2, 0));
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, Math.PI), sampleTime: new Timestamp(1, 0));
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), sampleTime: new Timestamp(3, 0));
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(100, 0, 0), sampleTime: new Timestamp(2, 0));
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(100, 0, 0), sampleTime: new Timestamp(3, 0));
                witness.SetCartesianPosition(default(Double3), new Timestamp(4, 0));
                _realm.Update();
                Assert.That(extrapolation.GetComponent<Spatial>().AngularVelocity, Is.EqualTo(default(Double3)), "Older and duplicate samples cannot replace the accepted raw rate.");
                AssertRotation(ghost.transform.rotation, rotation * Quaternion.AngleAxis(4.5f, Vector3.up));
                AssertRotation(spatial.Rotation, rotation);

                // Clearing supplied rates restores the estimate from rotations. Later-only rates must not override it.
                extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
                _realm.Update();
                Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.False);
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(42, Vector3.forward));
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), sampleTime: new Timestamp(2, 10000000));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(42, Vector3.forward));
                extrapolation.GetComponent<Spatial>().ClearAngularVelocity();
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), sampleTime: new Timestamp(2, 0));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, rotation);
                Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.True, "Supplied zero motion overrides the observed arc.");
                Assert.Throws<ArgumentException>(() => extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(1, 0, 0), default(CoordinateSystem)));
                Assert.That(extrapolation.GetComponent<Spatial>().AngularVelocity, Is.EqualTo(default(Double3)), "Invalid axes leave accepted motion intact.");

                // Rates without timestamps apply immediately. Disabling prediction then shows the raw rotation.
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, Math.PI / 2, 0), CoordinateSystem.Unity);
                _realm.Update();
                AssertRotation(ghost.transform.rotation, rotation * Quaternion.AngleAxis(4.5f, Vector3.up));
                extrapolation.enabled = false;
                spatial.ResetPresentation();
                _realm.Update();
                AssertRotation(ghost.transform.rotation, rotation);
                extrapolation.enabled = true;

                // Clock reset accepts restarted timestamps. Filter reset keeps supplied rates.
                _realm.ResetSpatialTime();
                spatial.SetCartesianPosition(default(Double3), new Timestamp(0, 0));
                spatial.SetSourceRotation(Quaternion.identity, new Timestamp(0, 0));
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, Math.PI / 2), sampleTime: new Timestamp(0, 0));
                witness.SetCartesianPosition(default(Double3), new Timestamp(1, 0));
                _realm.Update();
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(4.5f, Vector3.up));
                extrapolation.Reset();
                _realm.Update();
                Assert.That(extrapolation.GetComponent<Spatial>().HasAngularVelocity, Is.True, "Presentation resets retain supplied angular motion.");
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(4.5f, Vector3.up));
                // If predicted rotation is too large to calculate, keep the raw rotation. Recover when usable rates return.
                extrapolation.MaximumExtrapolation = 2;
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(1e308, 1e308, 1e308));
                witness.SetCartesianPosition(default(Double3), new Timestamp(4, 0));
                spatial.ResetPresentation();
                _realm.Update();
                AssertRotation(ghost.transform.rotation, Quaternion.identity);
                extrapolation.MaximumExtrapolation = 0.05f;
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(new Double3(0, 0, Math.PI / 2));
                spatial.ResetPresentation();
                _realm.Update();
                AssertRotation(ghost.transform.rotation, Quaternion.AngleAxis(4.5f, Vector3.up));
            }
        }

        /// <summary>Extrapolation derives velocity from timed position intervals, uses the same predicted reference for every root, and clock reset accepts restarted SDK timestamps without stale estimates.</summary>
        [Test]
        public void TimestampedExtrapolation_EstimatesMotionAndRecoversFromClockRestart()
        {
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = new Key("simulation", SpatialKind, "reference") };
            _realm.ReferenceFrame = frame;
            TestGhost reference = _source.Publish("reference");
            Spatial spatial = reference.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = reference.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.25f;
            spatial.SetCartesianPosition(new Double3(1e9, 0, 0), new Timestamp(1000, 0));
            TestGhost target = _source.Publish("target");
            Spatial targetSpatial = target.gameObject.AddComponent<Spatial>();
            targetSpatial.SetCartesianPosition(new Double3(1e9 + 20, 0, 0), new Timestamp(1002, 0));
            _realm.Update();
            spatial.SetCartesianPosition(new Double3(1e9 + 10, 0, 0), new Timestamp(1001, 0));
            _realm.Update();
            Assert.That(extrapolation.GetComponent<Spatial>().HasVelocity, Is.False, "SDK velocity is optional.");
            Assert.That(frame.Position.X, Is.EqualTo(1e9 + 12.5).Within(0.001));
            AssertPosition(reference.transform.position, Vector3.zero);
            AssertPosition(target.transform.position, new Vector3(7.5f, 0, 0), 0.001f);

            _realm.ResetSpatialTime();
            spatial.SetCartesianPosition(new Double3(1e9 + 4, 0, 0), new Timestamp(0, 0));
            targetSpatial.SetCartesianPosition(new Double3(1e9 + 20, 0, 0), new Timestamp(0, 0));
            _realm.Update();
            Assert.That(spatial.PositionTime, Is.EqualTo(new Timestamp(0, 0)));
            Assert.That(frame.Position.X, Is.EqualTo(1e9 + 4).Within(0.001));
            AssertPosition(reference.transform.position, Vector3.zero);
            AssertPosition(target.transform.position, new Vector3(16, 0, 0), 0.001f);
        }

        /// <summary>Coherent timestamped motion stays continuous through delayed, held and reordered packets, with or without smoothing, while a followed origin preserves relative placement.</summary>
        /// <returns>An iterator that advances the scenario through Unity frames.</returns>
        [UnityTest]
        public IEnumerator TimestampedExtrapolation_DelayedStreamPreservesMotionAndRelativePlacement()
        {
            double[] delays = { 0.035, 0.015, 0.07, 0.025, 0.05, 0.02 };
            Double3 velocity = new Double3(8, 0, 0);
            for (int variant = 0; variant < 2; variant++)
            {
                using (Realm realm = new Realm())
                {
                    TestSource source = new TestSource();
                    realm.GetOrCreateAnchor("simulation", source);
                    TestGhost reference = source.Publish("reference");
                    TestGhost target = source.Publish("target");
                    Spatial referenceSpatial = reference.gameObject.AddComponent<Spatial>();
                    Spatial targetSpatial = target.gameObject.AddComponent<Spatial>();
                    Extrapolation referenceExtrapolation = reference.gameObject.AddComponent<Extrapolation>();
                    Extrapolation targetExtrapolation = target.gameObject.AddComponent<Extrapolation>();
                    referenceExtrapolation.MaximumExtrapolation = 0.5f;
                    targetExtrapolation.MaximumExtrapolation = 0.5f;
                    if (variant == 1)
                    {
                        reference.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.08f;
                        target.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.08f;
                    }

                    ReferenceFrame frame = new ReferenceFrame { FollowedGhost = reference.Key };
                    realm.ReferenceFrame = frame;
                    double started = Time.realtimeSinceStartupAsDouble;

                    void Deliver(double observedSeconds, Timestamp sampleTime)
                    {
                        double position = 1e9 + velocity.X * observedSeconds;
                        referenceSpatial.SetCartesianPosition(new Double3(position, 0, 0), sampleTime);
                        targetSpatial.SetCartesianPosition(new Double3(position + 20, 0, 0), sampleTime);
                        referenceExtrapolation.GetComponent<Spatial>().SetCartesianVelocity(velocity, sampleTime);
                        targetExtrapolation.GetComponent<Spatial>().SetCartesianVelocity(velocity, sampleTime);
                    }

                    Deliver(0, Timestamp.FromSeconds(0));
                    realm.Update();
                    double previousPosition = frame.Position.X;
                    Timestamp? previousSampleTime = referenceSpatial.PositionTime;
                    bool movedWhileHeld = false;

                    void UpdateAndAssert()
                    {
                        bool held = referenceSpatial.PositionTime.Equals(previousSampleTime);
                        realm.Update();
                        Assert.That(frame.Position.X, Is.GreaterThanOrEqualTo(previousPosition - 0.0001),
                            "Coherent forward motion must not rewind when a delayed packet arrives (smoothing variant " + variant + ").");
                        AssertPosition(reference.transform.position, frame.UnityPosition);
                        AssertPosition(target.transform.position, new Vector3(20, 0, 0), 0.001f);
                        movedWhileHeld |= held && frame.Position.X > previousPosition + 0.0001;
                        previousPosition = frame.Position.X;
                        previousSampleTime = referenceSpatial.PositionTime;
                    }

                    double reorderedSeconds = 0;
                    Timestamp reorderedTime = default;
                    for (int packet = 0; packet < delays.Length; packet++)
                    {
                        // Record the observation time before adding delivery delay. Do not stamp it with a future time or arrival time.
                        double observedSeconds = Time.realtimeSinceStartupAsDouble - started;
                        Timestamp sampleTime = Timestamp.FromSeconds(observedSeconds);
                        double deliveryTime = Time.realtimeSinceStartupAsDouble + delays[packet];
                        do
                        {
                            yield return null;
                            UpdateAndAssert();
                        }
                        while (Time.realtimeSinceStartupAsDouble < deliveryTime);

                        if (packet == 0)
                        {
                            // Delay this original observation until newer packets have been accepted.
                            reorderedSeconds = observedSeconds;
                            reorderedTime = sampleTime;
                            continue;
                        }

                        Deliver(observedSeconds, sampleTime);
                        UpdateAndAssert();
                        if (packet == 2)
                        {
                            Deliver(reorderedSeconds, reorderedTime);
                            Assert.That(referenceSpatial.PositionTime, Is.EqualTo(sampleTime));
                            Assert.That(targetSpatial.PositionTime, Is.EqualTo(sampleTime));
                            UpdateAndAssert();
                        }
                    }

                    Assert.That(movedWhileHeld, Is.True,
                        "Extrapolation must continue advancing between source observations in both smoothing configurations.");
                }
            }
        }

        /// <summary>Compares supplied and estimated high-rate flight with reversed-rate and disabled-prediction controls across target offsets.</summary>
        /// <param name="strategy">The immutable prediction model shared by the isolated Realms.</param>
        /// <param name="bodyRate">Constant forward/right/down body rates in radians per second.</param>
        /// <returns>An iterator that observes fresh packets followed by packets delayed by 35 milliseconds.</returns>
        private IEnumerator AssertHighRateReference(IExtrapolationStrategy strategy, Double3 bodyRate)
        {
            // Isolate supplied rates, reversed rates, disabled prediction and estimated rates with identical observations.
            Realm[] realms = { new Realm(), new Realm(), new Realm(), new Realm() };
            Spatial[] references = new Spatial[realms.Length];
            Extrapolation[] extrapolations = new Extrapolation[realms.Length];
            Spatial[] clocks = new Spatial[realms.Length];
            Vector3[] worldTargets =
            {
                new Vector3(1000, 700, 1000),
                new Vector3(3000, 700, 1000),
                new Vector3(30000, 7000, 10000),
                new Vector3(-3000, 700, 1000),
                new Vector3(0, 0, 1000)
            };
            TestGhost[,] targets = new TestGhost[realms.Length, worldTargets.Length];
            // Calculate a path with constant body rotation rates and the expected target direction without using Emas.
            Vector3 angular = new Vector3((float)-bodyRate.Y, (float)bodyRate.Z, (float)-bodyRate.X);
            double radiansPerSecond = angular.magnitude;
            Vector3 axis = angular.normalized;
            Vector3 initialVelocity = new Vector3(0, 0, 343);
            Double3 reversed = bodyRate.Y != 0
                ? new Double3(bodyRate.X, -bodyRate.Y, bodyRate.Z)
                : new Double3(-bodyRate.X, bodyRate.Y, bodyRate.Z);
            CoordinateSystem axes = CoordinateSystem.NorthEastDown;
            ReferenceFrame conversion = new ReferenceFrame { Coordinates = axes, Position = default(Double3), FollowRotation = false };
            double[,] maximumErrors = new double[realms.Length, 2];
            double[,] maximumExcess = new double[realms.Length, 2];
            double maximumReferenceLead = 0;
            double maximumTargetPoseDifference = 0;
            double maximumWrongRateNearError = 0;
            double maximumWrongRateFarError = 0;
            try
            {
                // Measure supplied rates without Smoothing, and retain default motion-assisted filtering coverage with estimated rates.
                for (int variant = 0; variant < realms.Length; variant++)
                {
                    TestSource source = new TestSource();
                    realms[variant].GetOrCreateAnchor("simulation", source);
                    TestGhost reference = source.Publish("rapid ownship");
                    references[variant] = reference.gameObject.AddComponent<Spatial>();
                    extrapolations[variant] = reference.gameObject.AddComponent<Extrapolation>();
                    extrapolations[variant].Strategy = strategy;
                    extrapolations[variant].enabled = variant != 2;
                    if (variant == 3)
                    {
                        reference.gameObject.AddComponent<Smoothing>();
                    }
                    realms[variant].ReferenceFrame = new ReferenceFrame { Coordinates = axes, FollowedGhost = reference.Key };
                    for (int target = 0; target < worldTargets.Length; target++)
                    {
                        Vector3 point = worldTargets[target];
                        targets[variant, target] = source.Publish("stationary target " + target);
                        targets[variant, target].gameObject.AddComponent<Spatial>().SetCartesianPosition(
                            new Double3(point.z, point.x, -point.y));
                    }
                    clocks[variant] = source.Publish("current source clock").gameObject.AddComponent<Spatial>();
                }

                double started = Time.realtimeSinceStartupAsDouble - 0.1d;
                double elapsed;
                do
                {
                    elapsed = Time.realtimeSinceStartupAsDouble - started;
                    // Fresh packets check velocity at the start of prediction. Delayed packets expose wrong signs over a known delay.
                    int phase = elapsed < 0.55d ? 0 : 1;
                    double observed = elapsed - (phase == 0 ? 0 : 0.035d);
                    double angle = radiansPerSecond * observed;
                    Vector3 position = initialVelocity * (float)observed
                        + Vector3.Cross(axis, initialVelocity) * (float)((1d - Math.Cos(angle)) / radiansPerSecond)
                        + Vector3.Cross(axis, Vector3.Cross(axis, initialVelocity))
                            * (float)(observed - Math.Sin(angle) / radiansPerSecond);
                    Vector3 velocity = RotateTrajectory(initialVelocity, axis, angle);
                    Vector3 acceleration = Vector3.Cross(angular, velocity);
                    // Send the flight in north/east/down source axes with forward/right/down body rates.
                    Quaternion rotation = conversion.ToSimulationRotation(Quaternion.AngleAxis((float)(angle * Mathf.Rad2Deg), axis));
                    Timestamp sampleTime = Timestamp.FromSeconds(1000d + observed);
                    for (int variant = 0; variant < realms.Length; variant++)
                    {
                        references[variant].SetCartesianPosition(new Double3(position.z, position.x, -position.y), sampleTime);
                        references[variant].SetSourceRotation(rotation, sampleTime);
                        extrapolations[variant].GetComponent<Spatial>().SetCartesianVelocity(new Double3(velocity.z, velocity.x, -velocity.y), sampleTime);
                        extrapolations[variant].GetComponent<Spatial>().SetCartesianAcceleration(new Double3(acceleration.z, acceleration.x, -acceleration.y), sampleTime);
                        if (variant != 3)
                        {
                            extrapolations[variant].GetComponent<Spatial>().SetBodyAngularVelocity(variant == 1 ? reversed : bodyRate, sampleTime: sampleTime);
                        }
                        double before = Time.realtimeSinceStartupAsDouble;
                        // A current timestamp from another root fixes the observation age at 35 ms,
                        // without needing to know the total delivery delay estimated by the runtime clock.
                        clocks[variant].SetCartesianPosition(default(Double3), Timestamp.FromSeconds(1000d + before - started));
                        realms[variant].Update();
                        double after = Time.realtimeSinceStartupAsDouble;
                        double now = (before + after) * 0.5d - started;
                        // Calculate the expected pose at the measured display time, not the older packet time.
                        double currentAngle = radiansPerSecond * now;
                        Vector3 currentPosition = initialVelocity * (float)now
                            + Vector3.Cross(axis, initialVelocity) * (float)((1d - Math.Cos(currentAngle)) / radiansPerSecond)
                            + Vector3.Cross(axis, Vector3.Cross(axis, initialVelocity))
                                * (float)(now - Math.Sin(currentAngle) / radiansPerSecond);
                        for (int target = 0; target < worldTargets.Length; target++)
                        {
                            Vector3 expected = RotateTrajectory(worldTargets[target] - currentPosition, axis, -currentAngle);
                            TestGhost targetGhost = targets[variant, target];
                            double error = Vector3.Angle(expected, targetGhost.transform.position);
                            // Estimated rates need a second attitude first. Let the coordinated-turn
                            // packet correction settle after that initial rate becomes available.
                            double warmup = variant == 3 ? 0.4d : 0.25d;
                            if ((phase == 0 && elapsed > warmup) || elapsed > 0.7d)
                            {
                                maximumErrors[variant, phase] = Math.Max(maximumErrors[variant, phase], error);
                                // Allow for rotation during the measured update, which matters at high roll rates.
                                maximumExcess[variant, phase] = Math.Max(maximumExcess[variant, phase],
                                    error - radiansPerSecond * Mathf.Rad2Deg * (after - before));
                                if (variant == 1 && phase == 1)
                                {
                                    double displacement = Vector3.Distance(expected, targetGhost.transform.position);
                                    if (target == 1)
                                    {
                                        maximumWrongRateNearError = Math.Max(maximumWrongRateNearError, displacement);
                                    }
                                    else if (target == 2)
                                    {
                                        maximumWrongRateFarError = Math.Max(maximumWrongRateFarError, displacement);
                                    }
                                }
                            }

                            // Target diagnostics share the presented reference. They cannot expose its angle error.
                            Assert.That(targetGhost.GetRequired<Spatial>().TryGetObservedUnityPose(out Pose observedPose), Is.True);
                            maximumTargetPoseDifference = Math.Max(maximumTargetPoseDifference,
                                Vector3.Distance(observedPose.position, targetGhost.transform.position));
                        }

                        if (variant == 0 && phase == 1)
                        {
                            Assert.That(references[variant].TryGetObservedUnityPose(out Pose observedReference), Is.True);
                            maximumReferenceLead = Math.Max(maximumReferenceLead,
                                Quaternion.Angle(observedReference.rotation, references[variant].transform.rotation));
                        }
                    }

                    // Bound capture frequency independently of the headless runner's rendering speed.
                    yield return new WaitForSecondsRealtime(1f / 120f);
                }
                while (elapsed < 1d);

                // Report the controls as well as correct tracking so a sign error is easy to identify.
                TestContext.WriteLine(strategy.GetType().Name + "; FRD rad/s=" + bodyRate
                    + "; correct bearing error fresh/delayed=" + maximumErrors[0, 0] + "/" + maximumErrors[0, 1]
                    + "; reversed sign=" + maximumErrors[1, 0] + "/" + maximumErrors[1, 1]
                    + "; disabled=" + maximumErrors[2, 0] + "/" + maximumErrors[2, 1]
                    + "; estimated=" + maximumErrors[3, 0] + "/" + maximumErrors[3, 1] + " deg"
                    + "; reference lead=" + maximumReferenceLead + " deg; target observed/presented gap=" + maximumTargetPoseDifference
                    + "; reversed-rate near/far displacement=" + maximumWrongRateNearError + "/" + maximumWrongRateFarError);
                Assert.That(maximumExcess[0, 0], Is.LessThan(0.25d), "Fresh-packet high-rate bearing must match independent flight geometry.");
                Assert.That(maximumExcess[0, 1], Is.LessThan(0.25d), "Delayed-packet high-rate bearing must match independent flight geometry.");
                Assert.That(maximumExcess[3, 0], Is.LessThan(0.25d), "Estimated rates must preserve fresh-packet high-rate bearings.");
                Assert.That(maximumExcess[3, 1], Is.LessThan(0.25d), "Estimated rates must preserve delayed-packet high-rate bearings.");
                Assert.That(maximumErrors[1, 1], Is.GreaterThan(2d), "A reversed pitch/roll sign must reproduce a visible maneuver offset.");
                Assert.That(maximumErrors[2, 1], Is.GreaterThan(2d), "Disabling extrapolation must expose the known observation delay.");
                Assert.That(maximumReferenceLead, Is.GreaterThan(2d), "The followed reference must actually extrapolate its attitude.");
                Assert.That(maximumTargetPoseDifference, Is.LessThan(0.001d), "A stationary target's comparison excludes reference prediction.");
                Assert.That(maximumWrongRateFarError, Is.GreaterThan(maximumWrongRateNearError * 8d),
                    "Reference-angle errors must grow with target distance even when target diagnostics show no difference.");
            }
            finally
            {
                foreach (Realm realm in realms)
                {
                    realm.Dispose();
                }
            }
        }

        /// <summary>Rotates the independent flight trajectory with Rodrigues' vector formula.</summary>
        /// <param name="vector">The world or body vector to rotate.</param>
        /// <param name="axis">The unit rotation axis.</param>
        /// <param name="angle">The signed angle in radians.</param>
        /// <returns>The rotated vector without using Emas presentation state.</returns>
        private static Vector3 RotateTrajectory(Vector3 vector, Vector3 axis, double angle)
        {
            return vector * (float)Math.Cos(angle) + Vector3.Cross(axis, vector) * (float)Math.Sin(angle)
                + axis * (Vector3.Dot(axis, vector) * (float)(1d - Math.Cos(angle)));
        }

        /// <summary>Exercises cubic arrival correction, interruption and per-root state with timed, buffered or untimed input.</summary>
        /// <param name="mode">Zero for timed input, one for buffered input, or two for untimed input.</param>
        private IEnumerator AssertCubicPackets(int mode)
        {
            // Roots that share a strategy must still keep separate packet corrections.
            _realm.InterpolationDelay = mode == 1 ? 0.1f : 0;
            TestGhost ghost = _source.Publish("cubic aircraft " + mode);
            TestGhost peer = _source.Publish("independent aircraft " + mode);
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Spatial peerSpatial = peer.gameObject.AddComponent<Spatial>();
            CoordinatedTurnExtrapolation strategy = new CoordinatedTurnExtrapolation { PacketSmoothingDuration = 0.2f };
            Timestamp? time = mode == 2 ? (Timestamp?)null : new Timestamp(1000, 0);
            foreach (TestGhost root in new[] { ghost, peer })
            {
                Extrapolation extrapolation = root.gameObject.AddComponent<Extrapolation>();
                extrapolation.Strategy = strategy;
                extrapolation.MaximumExtrapolation = 0.05f;
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(default(Double3), time);
                extrapolation.GetComponent<Spatial>().SetBodyAngularVelocity(default(Double3), CoordinateSystem.Unity, time);
            }

            spatial.SetCartesianPosition(default(Double3), time);
            spatial.SetSourceRotation(Quaternion.identity, time);
            peerSpatial.SetCartesianPosition(new Double3(50, 0, 0), time);
            peerSpatial.SetSourceRotation(Quaternion.identity, time);
            _source.Publish("cubic clock " + mode).gameObject.AddComponent<Spatial>().SetCartesianPosition(default(Double3), new Timestamp(1005, 0));
            _realm.Update();
            spatial.SetCartesianPosition(new Double3(10, 0, 0), mode == 2 ? (Timestamp?)null : new Timestamp(1001, 0));
            spatial.SetSourceRotation(Quaternion.Euler(0, 60, 0), mode == 2 ? (Timestamp?)null : new Timestamp(1001, 0));
            // A sudden packet change must start at the visible pose and follow the expected cubic correction curve.
            double started = Time.realtimeSinceStartupAsDouble;
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.01f);
            AssertRotation(ghost.transform.rotation, Quaternion.identity);
            yield return AdvanceSmoothing(0.07d);
            double u = Math.Min(1d, (Time.realtimeSinceStartupAsDouble - started) / strategy.PacketSmoothingDuration);
            double weight = u * u * (3d - 2d * u);
            Assert.That(ghost.transform.position.x, Is.EqualTo(10d * weight).Within(0.15d));
            Assert.That(Quaternion.Angle(ghost.transform.rotation, Quaternion.Euler(0, (float)(60d * weight), 0)), Is.LessThan(1f));
            double beforeArrival = Time.realtimeSinceStartupAsDouble;
            // Measure the starting pose inside this time interval. The previous frame may be older.
            _realm.Update();
            Vector3 before = ghost.transform.position;
            Quaternion beforeRotation = ghost.transform.rotation;
            spatial.SetCartesianPosition(new Double3(-5, 0, 0), mode == 2 ? (Timestamp?)null : new Timestamp(1002, 0));
            spatial.SetSourceRotation(Quaternion.Euler(0, -30, 0), mode == 2 ? (Timestamp?)null : new Timestamp(1002, 0));
            _realm.Update();
            double elapsed = Time.realtimeSinceStartupAsDouble - beforeArrival;
            AssertPosition(ghost.transform.position, before, (float)(0.02d + 75d * elapsed));
            Assert.That(Quaternion.Angle(ghost.transform.rotation, beforeRotation), Is.LessThan(0.1d + 450d * elapsed));
            // Rejected old packets must not restart the correction or change the other root.
            if (time.HasValue)
            {
                spatial.SetCartesianPosition(new Double3(1000, 0, 0), time);
                spatial.SetSourceRotation(Quaternion.Euler(0, 150, 0), time);
            }
            yield return AdvanceSmoothing(0.3d);
            AssertPosition(ghost.transform.position, new Vector3(-5, 0, 0), 0.001f);
            AssertRotation(ghost.transform.rotation, Quaternion.Euler(0, -30, 0));
            AssertPosition(peer.transform.position, new Vector3(50, 0, 0));
            Assert.That(spatial.Position, Is.EqualTo(new Double3(-5, 0, 0)));
            Assert.That(ghost.GetComponent<Smoothing>(), Is.Null, "The strategy supplies its own cubic packet correction.");

            // Half-life smoothing must still filter a new prediction correction after the cubic curve finishes.
            ghost.gameObject.AddComponent<Smoothing>();
            _realm.Update();
            ghost.GetComponent<Spatial>().SetCartesianVelocity(new Double3(70, 0, 0), new Timestamp(1002, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(-5, 0, 0), 0.02f);
            yield return AdvanceSmoothing(0.35d);
            Assert.That(ghost.transform.position.x, Is.GreaterThan(-4f).And.LessThan(-1.51f));
            yield return AdvanceSmoothing(0.6d);
            AssertPosition(ghost.transform.position, new Vector3(-1.5f, 0, 0), 0.02f);
            ghost.GetComponent<Spatial>().SetCartesianVelocity(default(Double3));

            // Check that filter reset and SDK clock reset each clear the corrections they own.
            spatial.SetCartesianPosition(new Double3(20, 0, 0), mode == 2 ? (Timestamp?)null : new Timestamp(1003, 0));
            _realm.Update();
            Assert.That(ghost.transform.position.x, Is.LessThan(19));
            spatial.ResetPresentation();
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(20, 0, 0));
            _realm.ResetSpatialTime();
            spatial.SetCartesianPosition(new Double3(5, 0, 0), new Timestamp(0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(5, 0, 0));
        }

        private static Timestamp UnixTime(DateTime utc)
        {
            long ticks = utc.Ticks - 621355968000000000L;
            return new Timestamp(ticks / TimeSpan.TicksPerSecond, (uint)((ticks % TimeSpan.TicksPerSecond) * 100L));
        }

        private static void AssertBufferedCartesianPose(Spatial spatial, double minimumPosition, double publishedAt)
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - publishedAt;
            float position = spatial.transform.position.x;
            Assert.That(position, Is.InRange(minimumPosition - 0.01, minimumPosition + 100 * elapsed + 0.01));
            Assert.That(position, Is.LessThan(1000), "The delayed pose must interpolate instead of displaying the latest raw endpoint.");
            Quaternion expected = Quaternion.Slerp(Quaternion.Euler(0, 350, 0), Quaternion.Euler(0, 10, 0), position / 1000);
            AssertRotation(spatial.transform.rotation, expected);
        }

        /// <summary>Crosses a forward-and-reverse SDK maneuver in one projection interval while both endpoint velocities are zero and a later velocity remains buffered.</summary>
        /// <returns>An iterator that holds rendering until the full maneuver reaches playback time.</returns>
        private IEnumerator AssertBufferedManeuverPreservesMotion()
        {
            // Store a rapid maneuver followed by a large velocity that playback has not reached yet.
            _realm.ResetSpatialTime();
            _realm.InterpolationDelay = 1.05f;
            TestGhost ghost = _source.Publish("buffered maneuver");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.enabled = false;
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.5f;
            double[] times = { 0, 0.02, 0.04, 0.06, 0.08, 0.10, 0.12, 0.2, 0.9, 1 };
            double[] positions = { 0, 0, 2, 6, 8, 6.5, 5, 5, 5, 5 };
            double[] velocities = { 0, 0, 200, 200, 0, -150, 0, 0, 0, 1000 };
            for (int index = 0; index < times.Length; index++)
            {
                Timestamp sampleTime = Timestamp.FromSeconds(2000 + times[index]);
                spatial.SetCartesianPosition(new Double3(1e9 + positions[index], 0, 0), sampleTime);
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(velocities[index], 0, 0), sampleTime);
            }

            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
            double deadline = Time.realtimeSinceStartupAsDouble + 0.22;
            // Skip across the maneuver in one frame. Smoothing must use motion at playback time, not the newest rate.
            do
            {
                yield return null;
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);

            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(5, 0, 0), 0.02f);
            Assert.That(smoothing.enabled, Is.True);
            Assert.That(extrapolation.enabled, Is.False);
            Assert.That(spatial.Position, Is.EqualTo(new Double3(1e9 + 5, 0, 0)));
            Assert.That(spatial.PositionTime, Is.EqualTo(new Timestamp(2001, 0)));
            Assert.That(extrapolation.GetComponent<Spatial>().Velocity, Is.EqualTo(new Double3(1000, 0, 0)),
                "The accepted future velocity must not replace earlier buffered motion.");
            yield return AdvanceSmoothing(0.06);
            AssertPosition(ghost.transform.position, new Vector3(5, 0, 0), 0.02f);
        }

        /// <summary>Keeps a stationary smoothed pose stationary when independently timed acceleration starts during its extrapolation gap or recorded playback.</summary>
        /// <returns>An iterator that crosses an acceleration timestamp while the accepted velocity remains zero.</returns>
        private IEnumerator AssertBufferedAccelerationPreservesVelocityTimeline()
        {
            _realm.ResetSpatialTime();
            _realm.InterpolationDelay = 0.01f;
            TestGhost ghost = _source.Publish("buffered gap");
            Spatial spatial = ghost.gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = ghost.gameObject.AddComponent<Extrapolation>();
            extrapolation.MaximumExtrapolation = 0.05f;
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.5f;
            Double3 position = new Double3(1e9, 0, 0);
            Timestamp positionTime = new Timestamp(2, 0);
            Double3 acceleration = new Double3(4000, 0, 0);
            spatial.SetCartesianPosition(position, positionTime);
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(default(Double3), new Timestamp(1, 0));
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(acceleration, new Timestamp(2, 10000000));
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);

            // Start the filter before the gap, then cross the acceleration timestamp in one update.
            // A slow frame is also valid because prediction stops at the same stationary pose.
            double deadline = Time.realtimeSinceStartupAsDouble + 0.03;
            do
            {
                yield return null;
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);

            TestGhost clock = _source.Publish("gap clock");
            clock.gameObject.AddComponent<Spatial>().SetCartesianPosition(position, new Timestamp(2, 25000000));
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(spatial.Position, Is.EqualTo(position));
            Assert.That(spatial.PositionTime, Is.EqualTo(positionTime));
            Assert.That(extrapolation.GetComponent<Spatial>().HasAcceleration, Is.True);
            Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(acceleration),
                "Later acceleration stays accepted input without advancing an earlier pose's gap model or smoothing motion.");

            _realm.ResetSpatialTime();
            _realm.InterpolationDelay = 1.05f;
            extrapolation.enabled = false;
            spatial.SetCartesianPosition(position, positionTime);
            spatial.SetCartesianPosition(position, new Timestamp(3, 0));
            extrapolation.GetComponent<Spatial>().SetCartesianVelocity(default(Double3), new Timestamp(1, 0));
            extrapolation.GetComponent<Spatial>().SetCartesianAcceleration(acceleration, new Timestamp(2, 10000000));
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);

            // Recorded playback follows supplied velocity. Acceleration is used separately to predict past recorded poses.
            // Cross the acceleration timestamp while stored positions still cover the playback time.
            deadline = Time.realtimeSinceStartupAsDouble + 0.1;
            do
            {
                yield return null;
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);

            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero, 0.001f);
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(extrapolation.enabled, Is.False);
            Assert.That(smoothing.enabled, Is.True);
            Assert.That(spatial.Position, Is.EqualTo(position));
            Assert.That(spatial.PositionTime, Is.EqualTo(new Timestamp(3, 0)));
            Assert.That(extrapolation.GetComponent<Spatial>().Acceleration, Is.EqualTo(acceleration),
                "Recorded motion assistance must integrate SDK velocity without applying later acceleration retroactively.");
        }

        /// <summary>Publishes a geographic origin and an independently observed peer at one timestamp with a fixed body offset.</summary>
        /// <param name="reference">The followed origin's independent pose channels.</param>
        /// <param name="peer">The peer whose absolute observation is derived from the source body offset.</param>
        /// <param name="position">The origin's WGS84 location.</param>
        /// <param name="yaw">The source heading in degrees.</param>
        /// <param name="offset">The peer's offset in the reference body's Unity axes.</param>
        /// <param name="sampleTime">The original SDK observation time shared by the channels.</param>
        private static void PublishBufferedGeographicPair(Spatial reference, Spatial peer, GeoPosition position,
            double yaw, Vector3 offset, Timestamp sampleTime)
        {
            ReferenceFrame source = new ReferenceFrame { Space = ReferenceSpace.Geographic, GeographicPosition = position };
            source.SetGeographicRotation(yaw, 0, 0);
            reference.SetGeographicPosition(position, sampleTime);
            reference.SetGeographicRotation(yaw, 0, 0, sampleTime);
            peer.SetGeographicPosition(source.ToGeographicPosition(offset), sampleTime);
            peer.SetGeographicRotation(yaw, 0, 0, sampleTime);
        }

        /// <summary>Measures filter error against the capped target while coherent packets arrive 300 ms behind another entity's SDK observations.</summary>
        /// <param name="spatial">The moving entity observed with a fixed transport delay.</param>
        /// <param name="extrapolation">Its enabled motion provider with a 50 ms extrapolation limit.</param>
        /// <param name="clock">A stationary entity reporting the same SDK clock without the moving entity's transport delay.</param>
        /// <param name="interpolationDelay">The realm's immediate or buffered presentation delay.</param>
        /// <returns>An iterator that advances the source and projection streams, then holds the final capped observation.</returns>
        private IEnumerator AssertLatePacketsPreserveCappedMotion(Spatial spatial, Extrapolation extrapolation,
            Spatial clock, float interpolationDelay)
        {
            _realm.ResetSpatialTime();
            _realm.InterpolationDelay = interpolationDelay;
            const double speed = 560d * 1852d / 3600d;
            Timestamp latest = new Timestamp(1000, 0);
            void Publish(double observedSeconds)
            {
                latest = Timestamp.FromSeconds(1000 + observedSeconds);
                spatial.SetCartesianPosition(new Double3(1e9 + speed * observedSeconds, 0, 0), latest);
                extrapolation.GetComponent<Spatial>().SetCartesianVelocity(new Double3(speed, 0, 0), latest);
                // Both channels use the same SDK clock: the moving entity's packet was observed 300 ms earlier.
                clock.SetCartesianPosition(new Double3(1e9, 0, 0), Timestamp.FromSeconds(1000.3 + observedSeconds));
            }

            Publish(0);
            _realm.Update();
            AssertPosition(spatial.transform.position, new Vector3((float)(speed * extrapolation.MaximumExtrapolation), 0, 0), 0.001f);
            double started = Time.realtimeSinceStartupAsDouble;
            double elapsed;
            double maximumError = 0;
            int nextPacket = 1;
            int updates = 0;
            do
            {
                yield return null;
                elapsed = Time.realtimeSinceStartupAsDouble - started;
                while (nextPacket / 60d <= elapsed)
                {
                    Publish(nextPacket / 60d);
                    nextPacket++;
                }

                _realm.Update();
                Double3 expected = spatial.Position + extrapolation.GetComponent<Spatial>().Velocity * extrapolation.MaximumExtrapolation;
                Double3 presented = _realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position);
                maximumError = Math.Max(maximumError, Double3.Distance(presented, expected));
                updates++;
            }
            while (elapsed < 1);

            TestContext.WriteLine("Late capped packets: delay=" + interpolationDelay + " s; maximum filter error="
                + maximumError + " m; observed updates=" + updates / elapsed + " Hz");
            Assert.That(spatial.PositionTime, Is.EqualTo(latest));
            Assert.That(spatial.IsInRange, Is.True);
            Assert.That(maximumError, Is.LessThan(1),
                "Fresh late packets must advance motion assistance even when the previous observation already reached its extrapolation cap.");

            Double3 heldPosition = spatial.Position;
            Double3 heldTarget = heldPosition + extrapolation.GetComponent<Spatial>().Velocity * extrapolation.MaximumExtrapolation;
            Double3 heldPresentation = _realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position);
            double initialError = Double3.Distance(heldPresentation, heldTarget);
            double holdDeadline = Time.realtimeSinceStartupAsDouble + 0.12;
            do
            {
                yield return null;
                _realm.Update();
                Assert.That(extrapolation.enabled, Is.True);
                Assert.That(spatial.PositionTime, Is.EqualTo(latest));
                Assert.That(spatial.Position, Is.EqualTo(heldPosition));
                Assert.That(spatial.IsInRange, Is.True);
                Double3 presented = _realm.ReferenceFrame.ToSimulationPosition(spatial.transform.position);
                Assert.That(Double3.Distance(presented, heldTarget), Is.LessThanOrEqualTo(initialError + 0.001),
                    "Holding an already capped packet may settle the remaining correction but must not keep integrating velocity.");
            }
            while (Time.realtimeSinceStartupAsDouble < holdDeadline);
        }

        /// <summary>Compares timestamped motion support with ordinary smoothing, then holds and corrects the supplied observation.</summary>
        /// <param name="ghost">The moving root with supplied SDK velocity.</param>
        /// <param name="spatial">The root's raw observation and presentation contract.</param>
        /// <param name="smoothing">The filter configured identically to the unassisted comparison root.</param>
        /// <param name="extrapolation">The motion provider disabled for this phase so held observations cannot extrapolate.</param>
        /// <returns>An iterator that advances the moving stream, held sample and opposite correction.</returns>
        private IEnumerator AssertMotionWithoutExtrapolation(TestGhost ghost, Spatial spatial, Smoothing smoothing)
        {
            // Compare the same smoothing settings with and without SDK velocity while prediction is disabled.
            TestGhost unassisted = _source.PublishPosition("unassisted", default(Double3));
            Spatial other = unassisted.GetComponent<Spatial>();
            Smoothing otherSmoothing = unassisted.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.5f;
            otherSmoothing.PositionHalfLife = 0.5f;
            Timestamp sampleTime = Timestamp.FromSeconds(0);
            spatial.SetCartesianPosition(default(Double3), sampleTime);
            other.SetCartesianPosition(default(Double3), sampleTime);
            spatial.SetCartesianVelocity(new Double3(100, 0, 0), sampleTime);
            spatial.ResetPresentation();
            other.ResetPresentation();
            _realm.Update();
            double started = Time.realtimeSinceStartupAsDouble;
            double deadline = started + 0.8;
            double elapsed;
            do
            {
                yield return null;
                elapsed = Time.realtimeSinceStartupAsDouble - started;
                sampleTime = Timestamp.FromSeconds(elapsed);
                Double3 position = new Double3(100 * elapsed, 0, 0);
                spatial.SetCartesianPosition(position, sampleTime);
                other.SetCartesianPosition(position, sampleTime);
                spatial.SetCartesianVelocity(new Double3(100, 0, 0), sampleTime);
                _realm.Update();
                Assert.That(ghost.transform.position.z, Is.EqualTo(position.X).Within(0.05),
                    "Spatial motion must support smoothing without a prediction component between new timestamped observations.");
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);
            Assert.That(Math.Abs(unassisted.transform.position.z - other.Position.X), Is.GreaterThan(20),
                "A Ghost without supplied motion must retain ordinary low-pass smoothing.");
            Assert.That(unassisted.GetComponent<Extrapolation>(), Is.Null);
            Assert.That(ghost.GetComponent<Extrapolation>(), Is.Null);

            // Once the source stops sending positions, supplied velocity must not keep moving the displayed pose.
            Double3 heldPosition = spatial.Position;
            yield return AdvanceSmoothing(0.12);
            Assert.That(ghost.transform.position.z, Is.EqualTo(heldPosition.X).Within(0.05),
                "Supplied velocity must not advance a held observation while extrapolation is disabled.");
            Assert.That(spatial.Position, Is.EqualTo(heldPosition));
            Assert.That(spatial.PositionTime, Is.EqualTo(sampleTime));
            Assert.That(spatial.Velocity, Is.EqualTo(new Double3(100, 0, 0)));

            // Accept and smooth a new position behind the current one, even while velocity still points forward.
            Double3 corrected = heldPosition - new Double3(10, 0, 0);
            Timestamp correctionTime = Timestamp.FromSeconds(elapsed + 0.001);
            spatial.SetCartesianPosition(corrected, correctionTime);
            spatial.SetCartesianVelocity(new Double3(100, 0, 0), correctionTime);
            yield return AdvanceSmoothing(0.08);
            Assert.That(ghost.transform.position.z, Is.LessThan(heldPosition.X).And.GreaterThan(corrected.X + 1),
                "Motion support must filter a correction against travel instead of rejecting it or snapping to it.");
            Assert.That(spatial.Position, Is.EqualTo(corrected));
            Assert.That(spatial.PositionTime, Is.EqualTo(correctionTime));
        }

        private IEnumerator AdvanceSmoothing(double seconds)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            do
            {
                yield return null;
                _realm.Update();
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);
        }

        private void RegisterView(bool observeEnable = false)
        {
            GameObject prefab = new GameObject("spatial view");
            prefab.SetActive(false);
            if (observeEnable)
            {
                prefab.AddComponent<EnableProbe>();
            }

            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(SpatialKind, null, null, prefab);
            _assets.Add(prefab);
            _assets.Add(blueprint);
            _realm.RegisterManifestationBlueprint(blueprint);
        }

        private static View ActiveView(TestGhost ghost)
        {
            return ghost.GetComponentInChildren<View>();
        }

        private static void AssertPosition(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThanOrEqualTo(tolerance),
                "Expected position " + expected.ToString("F6") + ", got " + actual.ToString("F6"));
        }

        private static void SetDistancePose(Spatial spatial, Double3 position, Quaternion rotation, Timestamp time, bool geographic)
        {
            // Use each supported absolute-position contract with the same local/source attitude axes.
            if (geographic)
            {
                spatial.SetEarthCenteredPosition(position, time);
            }
            else
            {
                spatial.SetCartesianPosition(position, time);
            }
            spatial.SetSourceRotation(rotation, time);
        }

        private static void AssertRotation(Quaternion actual, Quaternion expected)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f));
        }
    }
}
