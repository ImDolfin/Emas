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

        /// <summary>
        /// An absent reference maps spatial poses to world space, including after clearing a custom frame.
        /// </summary>
        [Test]
        public void MissingReference_UsesIdentityWorldPoseAndClearingRestoresIt()
        {
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

            // Named geographic angles require Geographic projection; incompatible input suppresses presentation and can recover.
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
            ego.GetComponent<Spatial>().SetEarthCenteredPosition(new Double3(double.MaxValue, double.MaxValue, double.MaxValue));
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            AssertPosition(target.transform.position, projected, 1);
            _source.RemoveEntity("ego");
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            AssertPosition(target.transform.position, projected, 1);

            TestGhost replacement = _source.PublishEarthCenteredPosition("ego", new GeoPosition(0, 0, 0).ToEarthCentered());
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.True);
            AssertPosition(target.transform.position, new Vector3(0, 10, 0));
            AssertRotation(target.transform.rotation, Quaternion.identity);
            Assert.That(ActiveView(target), Is.SameAs(view));
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

            // Independent PROJ cart/topocentric results, reordered from east/north/up to Unity east/up/north.
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
            // Intrinsic heading 90, nose-up pitch 30, right-wing-down roll 90; expected offsets are analytic.
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

            // Source quaternion axes can change without reinterpreting named geographic angles.
            frame.Coordinates = CoordinateSystem.EastNorthUp;
            _realm.Update();
            AssertPosition(target.Root.transform.position, bankedOffset);
            Assert.That(target.Root.GetComponent<Spatial>().Position, Is.EqualTo(storedTarget));

            frame.Coordinates = CoordinateSystem.Unity;
            referenceSpatial.SetSourceRotation(Quaternion.identity);
            _realm.Update();
            Assert.That(frame.RotationSpace, Is.EqualTo(RotationSpace.Source));
            AssertPosition(target.Root.transform.position, new Vector3(6.787977894133f, 4.999993968840f, 5.563909085825f));
            // The application owns these sources; Presence deliberately retains only weak references.
            GC.KeepAlive(targetReading);
            GC.KeepAlive(referenceReading);
        }

        /// <summary>
        /// ECEF channels update independently, can drive the reference, and coexist with local geographic attitudes.
        /// </summary>
        [Test]
        public void EarthCenteredChannels_RetainGlobalAttitudeAndFollowMixedInputs()
        {
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

            spatial.SetEarthCenteredPosition(new GeoPosition(0, 90, 0).ToEarthCentered());
            _realm.Update();
            AssertRotation(spatial.Rotation, north);
            AssertRotation(target.transform.rotation, Quaternion.identity);
            frame.Position = spatial.Position;
            _realm.Update();
            AssertRotation(target.transform.rotation, Quaternion.AngleAxis(90, Vector3.forward));
            Assert.That(ActiveView(target), Is.SameAs(view));

            TestGhost local = _source.PublishEarthCenteredPosition("local", spatial.Position);
            _source.PublishRotation("local", Quaternion.identity);
            frame.FollowedGhost = target.Key;
            frame.FollowRotation = true;
            _realm.Update();
            Assert.That(frame.RotationSpace, Is.EqualTo(RotationSpace.EarthCentered));
            AssertRotation(target.transform.rotation, Quaternion.identity);
            AssertRotation(local.transform.rotation, Quaternion.AngleAxis(-90, Vector3.forward));
            Assert.That(ActiveView(target), Is.SameAs(view));

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
        [TestCase(false)]
        [TestCase(true)]
        public void RangeChanges_PreserveMembershipAndRespectPresentationRequests(bool cancelWhileFar)
        {
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

        /// <summary>
        /// A distant entity remains tracked without writing enormous coordinates into its Transform.
        /// </summary>
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
        [UnityTest]
        public IEnumerator Smoothing_WithoutVelocityFiltersIndependentChannelsAndCanBeResetOrDisabled()
        {
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
            Assert.That(ghost.GetComponent<Prediction>(), Is.Null);
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

                smoothing.RotationHalfLife = 0;
                _realm.Update();
                AssertRotation(ghost.transform.rotation, targetRotation);
                Assert.That(ghost.transform.position.x, Is.LessThan(10), "Changing rotation smoothing keeps position history.");
                smoothing.RotationHalfLife = 2;
                _realm.Update();
                Quaternion nextRotation = Quaternion.Euler(0, 90, 0);
                spatial.SetSourceRotation(nextRotation);
                smoothing.PositionHalfLife = 0;
                yield return AdvanceSmoothing(0.04);
                AssertPosition(ghost.transform.position, new Vector3(10, 2, 0));
                Assert.That(Quaternion.Angle(targetRotation, ghost.transform.rotation), Is.GreaterThan(0).And.LessThan(80),
                    "Disabling position smoothing keeps rotation smoothing active.");

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

        /// <summary>Optional SDK motion reduces position lag, accepts opposite corrections and bounds stale prediction; clearing channels and disabling smoothing restore pose-only handling.</summary>
        [UnityTest]
        public IEnumerator Smoothing_VelocityPredictsMotionWithoutRejectingCorrections()
        {
            _realm.ReferenceFrame = new ReferenceFrame { Coordinates = CoordinateSystem.NorthEastDown, Position = default(Double3) };
            TestGhost ghost = _source.PublishPosition("smooth", new Double3(10, 0, 0));
            Spatial spatial = ghost.GetComponent<Spatial>();
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.2f;
            Prediction prediction = ghost.gameObject.AddComponent<Prediction>();
            prediction.MaximumExtrapolation = 0.1f;
            prediction.SetCartesianVelocity(new Double3(8, 0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0, 0, 10), 0.02f);
            spatial.SetCartesianPosition(new Double3(-10, 2, 0));
            yield return AdvanceSmoothing(0.04);
            Assert.That(ghost.transform.position.z, Is.LessThan(10), "A correction opposite strong velocity is accepted.");
            Assert.That(ghost.transform.position.x, Is.GreaterThan(0));
            Assert.That(spatial.Position, Is.EqualTo(new Double3(-10, 2, 0)));
            Assert.That(prediction.HasVelocity, Is.True);
            Assert.That(prediction.Velocity, Is.EqualTo(new Double3(8, 0, 0)));

            spatial.ResetPresentation();
            spatial.SetCartesianPosition(default(Double3));
            prediction.SetCartesianVelocity(new Double3(8, 0, 0));
            prediction.SetCartesianAcceleration(new Double3(4, 0, 0));
            _realm.Update();
            yield return AdvanceSmoothing(0.3);
            Assert.That(ghost.transform.position.z, Is.EqualTo(0.82f).Within(0.02f), "Prediction is capped independently of smoothing, including acceleration.");
            float stoppedPrediction = ghost.transform.position.z;
            double cachedUntil = Time.realtimeSinceStartupAsDouble + 0.08;
            do
            {
                yield return null;
                spatial.SetCartesianPosition(default(Double3));
                _realm.Update();
            }
            while (Time.realtimeSinceStartupAsDouble < cachedUntil);
            Assert.That(ghost.transform.position.z, Is.EqualTo(stoppedPrediction).Within(0.01f));
            Assert.That(spatial.Position, Is.EqualTo(default(Double3)), "Prediction never changes the SDK input.");
            Assert.That(prediction.HasAcceleration, Is.True);
            Assert.That(prediction.Acceleration, Is.EqualTo(new Double3(4, 0, 0)));
            prediction.ClearVelocity();
            yield return AdvanceSmoothing(0.04);
            Assert.That(ghost.transform.position.z, Is.LessThan(stoppedPrediction), "Acceleration alone does not predict travel.");
            prediction.ClearAcceleration();
            Assert.That(prediction.HasAcceleration, Is.False);
            prediction.SetCartesianVelocity(new Double3(8, 0, 0));

            TestGhost unassisted = _source.PublishPosition("unassisted", default(Double3));
            Spatial other = unassisted.GetComponent<Spatial>();
            Smoothing otherSmoothing = unassisted.gameObject.AddComponent<Smoothing>();
            otherSmoothing.PositionHalfLife = smoothing.PositionHalfLife;
            spatial.ResetPresentation();
            spatial.SetCartesianPosition(default(Double3));
            _realm.Update();
            double started = Time.realtimeSinceStartupAsDouble;
            double deadline = started + 0.12;
            do
            {
                yield return null;
                Double3 position = new Double3(8 * (Time.realtimeSinceStartupAsDouble - started), 0, 0);
                spatial.SetCartesianPosition(position);
                other.SetCartesianPosition(position);
                _realm.Update();
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);
            Assert.That(Math.Abs(ghost.transform.position.z - spatial.Position.X),
                Is.LessThan(Math.Abs(unassisted.transform.position.z - other.Position.X)), "Velocity reduces lag on a moving stream.");

            prediction.ClearVelocity();
            float before = ghost.transform.position.z;
            spatial.SetCartesianPosition(new Double3(-8, 0, 0));
            yield return AdvanceSmoothing(0.04);
            Assert.That(prediction.HasVelocity, Is.False);
            Assert.That(ghost.transform.position.z, Is.LessThan(before));
            prediction.SetCartesianVelocity(new Double3(8, 0, 0));
            smoothing.enabled = false;
            prediction.enabled = false;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0, 0, -8));
        }

        /// <summary>A geographic reference shares independently smoothed channels with its root; unsmoothed reference rotation repositions smoothed targets and attached parts immediately.</summary>
        [UnityTest]
        public IEnumerator Smoothing_GeographicReferenceAndAttachmentsSharePresentationPose()
        {
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
            reference.gameObject.AddComponent<Prediction>().SetEarthCenteredVelocity(new Double3(0, 0, 0));
            TestGhost target = _source.PublishEarthCenteredPosition("target", new GeoPosition(52.520108, 13.404954, 40).ToEarthCentered());
            target.gameObject.AddComponent<Smoothing>().PositionHalfLife = 2;
            TestGhost part = _source.Publish("part");
            Spatial partSpatial = part.gameObject.AddComponent<Spatial>();
            Smoothing partSmoothing = part.gameObject.AddComponent<Smoothing>();
            partSmoothing.PositionHalfLife = 2;
            partSmoothing.RotationHalfLife = 2;
            Vector3 offset = new Vector3(2, 0, 0);
            partSpatial.Attach(reference.Key, offset);
            _realm.Update();

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

            smoothing.RotationHalfLife = 0;
            spatial.SetGeographicRotation(180, 0, 0);
            _realm.Update();
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

            partSpatial.SetEarthCenteredPosition(input);
            partSpatial.Detach();
            _realm.Update();
            Assert.That(frame.TryToUnityPosition(input, out Vector3 releasePosition), Is.True);
            AssertPosition(part.transform.position, releasePosition);
        }

        /// <summary>Views requested by arrival observers use one finalized predicted pose for the followed reference, parent and attached children; standalone requests still project fresh input.</summary>
        [Test]
        public void Manifest_FromArrivalObserversPreservesSharedAttachmentPresentation()
        {
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
                    reference.gameObject.AddComponent<Prediction>().SetCartesianVelocity(new Double3(2, 0, 0));
                    reference.gameObject.AddComponent<Smoothing>().PositionHalfLife = 0.08f;
                    left = _source.Publish("left");
                    left.gameObject.AddComponent<Spatial>().Attach(new Key("simulation", SpatialKind, "parent"), leftOffset);
                    right = _source.Publish("right");
                    right.gameObject.AddComponent<Spatial>().Attach(new Key("simulation", SpatialKind, "parent"), rightOffset);
                    parent = _source.PublishPosition("parent", new Double3(1010, 0, 0));
                    parent.GetComponent<Spatial>().SetSourceRotation(Quaternion.Euler(10, 35, 7));
                    parent.gameObject.AddComponent<Prediction>().SetCartesianVelocity(new Double3(8, 0, 0));
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
            parent.GetComponent<Prediction>().enabled = false;
            parent.GetComponent<Smoothing>().enabled = false;
            Spatial parentSpatial = parent.GetComponent<Spatial>();
            parentSpatial.SetCartesianPosition(new Double3(1020, 0, 0));
            Assert.That(_realm.Manifest(parent), Is.SameAs(existingView));
            Assert.That(frame.TryToUnityPosition(parentSpatial.Position, out Vector3 immediatePosition), Is.True);
            AssertPosition(parent.transform.position, immediatePosition);
        }

        /// <summary>Visibility uses current source range and recovered or re-enabled roots snap to fresh inputs.</summary>
        [Test]
        public void Smoothing_RangeRecoveryAndReenableDiscardOldHistory()
        {
            ReferenceFrame frame = new ReferenceFrame { Position = default(Double3), MaxDistance = 10 };
            _realm.ReferenceFrame = frame;
            TestGhost ghost = _source.PublishPosition("smooth", new Double3(5, 0, 0));
            Spatial spatial = ghost.GetComponent<Spatial>();
            Smoothing smoothing = ghost.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 10;
            _realm.Update();
            spatial.SetCartesianPosition(new Double3(100, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.False);
            spatial.SetCartesianPosition(new Double3(7, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.True);
            AssertPosition(ghost.transform.position, new Vector3(7, 0, 0));

            spatial.enabled = false;
            ghost.transform.position = Vector3.zero;
            spatial.SetCartesianPosition(new Double3(8, 0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, Vector3.zero);
            spatial.enabled = true;
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(8, 0, 0));
        }

        /// <summary>Timestamped entities use one estimated source clock, reject older pose/motion packets and refresh stationary sample age; behavior traits can be disabled independently.</summary>
        [Test]
        public void TimestampedPrediction_AlignsPacketAgesAndRejectsOlderChannels()
        {
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1e9, 0, 0) };
            Timestamp older = new Timestamp(1700000000L, 250000000U);
            Timestamp latest = new Timestamp(1700000000L, 900000000U);
            TestGhost delayed = _source.Publish("delayed");
            Spatial spatial = delayed.gameObject.AddComponent<Spatial>();
            Prediction prediction = delayed.gameObject.AddComponent<Prediction>();
            prediction.MaximumExtrapolation = 0.4f;
            spatial.SetCartesianPosition(new Double3(1e9 + 10, 0, 0), older);
            spatial.SetSourceRotation(Quaternion.identity, older);
            prediction.SetCartesianVelocity(new Double3(8, 0, 0), older);
            prediction.SetCartesianAcceleration(new Double3(4, 0, 0), older);
            TestGhost current = _source.Publish("current");
            Spatial currentSpatial = current.gameObject.AddComponent<Spatial>();
            currentSpatial.SetCartesianPosition(new Double3(1e9 + 100, 0, 0), latest);
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(13.52f, 0, 0), 0.001f);
            AssertPosition(current.transform.position, new Vector3(100, 0, 0));
            Assert.That(spatial.Position, Is.EqualTo(new Double3(1e9 + 10, 0, 0)));
            Assert.That(delayed.Traits, Does.Contain(prediction));

            Timestamp stale = new Timestamp(1700000000L, 100000000U);
            spatial.SetCartesianPosition(new Double3(1e9 - 100, 0, 0), stale);
            spatial.SetSourceRotation(Quaternion.Euler(0, 90, 0), stale);
            prediction.SetCartesianVelocity(new Double3(-80, 0, 0), stale);
            prediction.SetCartesianAcceleration(new Double3(-40, 0, 0), stale);
            spatial.SetCartesianPosition(new Double3(1e9 - 100, 0, 0), older);
            _realm.Update();
            AssertPosition(delayed.transform.position, new Vector3(13.52f, 0, 0), 0.001f);
            AssertRotation(delayed.transform.rotation, Quaternion.identity);
            Assert.That(spatial.PositionTime, Is.EqualTo(older));
            Assert.That(spatial.RotationTime, Is.EqualTo(older));
            Assert.That(prediction.Velocity, Is.EqualTo(new Double3(8, 0, 0)));

            spatial.SetCartesianPosition(spatial.Position, latest);
            _realm.Update();
            Assert.That(spatial.PositionTime, Is.EqualTo(latest));
            Assert.That(delayed.transform.position.x, Is.EqualTo(10).Within(0.1), "A fresh stationary observation refreshes age.");
            prediction.enabled = false;
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
            Assert.Throws<ArgumentOutOfRangeException>(() => prediction.MaximumExtrapolation = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => prediction.MaximumExtrapolation = -1);
        }

        /// <summary>Prediction derives velocity from timed position intervals, uses the same predicted reference for every root, and clock reset accepts restarted SDK timestamps without stale estimates.</summary>
        [Test]
        public void TimestampedPrediction_EstimatesMotionAndRecoversFromClockRestart()
        {
            ReferenceFrame frame = new ReferenceFrame { FollowedGhost = new Key("simulation", SpatialKind, "reference") };
            _realm.ReferenceFrame = frame;
            TestGhost reference = _source.Publish("reference");
            Spatial spatial = reference.gameObject.AddComponent<Spatial>();
            Prediction prediction = reference.gameObject.AddComponent<Prediction>();
            prediction.MaximumExtrapolation = 0.25f;
            spatial.SetCartesianPosition(new Double3(1e9, 0, 0), new Timestamp(1000, 0));
            TestGhost target = _source.Publish("target");
            Spatial targetSpatial = target.gameObject.AddComponent<Spatial>();
            targetSpatial.SetCartesianPosition(new Double3(1e9 + 20, 0, 0), new Timestamp(1002, 0));
            _realm.Update();
            spatial.SetCartesianPosition(new Double3(1e9 + 10, 0, 0), new Timestamp(1001, 0));
            _realm.Update();
            Assert.That(prediction.HasVelocity, Is.False, "SDK velocity is optional.");
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
        [UnityTest]
        public IEnumerator TimestampedPrediction_DelayedStreamPreservesMotionAndRelativePlacement()
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
                    Prediction referencePrediction = reference.gameObject.AddComponent<Prediction>();
                    Prediction targetPrediction = target.gameObject.AddComponent<Prediction>();
                    referencePrediction.MaximumExtrapolation = 0.5f;
                    targetPrediction.MaximumExtrapolation = 0.5f;
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
                        referencePrediction.SetCartesianVelocity(velocity, sampleTime);
                        targetPrediction.SetCartesianVelocity(velocity, sampleTime);
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
                        // Capture actual observation time before transport delay; never stamp a future or arrival-time pose.
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
                            // Hold this original observation until newer packets have already been accepted.
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
                        "Prediction must continue advancing between source observations in both smoothing configurations.");
                }
            }
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

        private static void AssertRotation(Quaternion actual, Quaternion expected)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f));
        }

        private sealed class TestGhost : Ghost
        {
            internal float Articulation { get; set; }
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
    }
}
