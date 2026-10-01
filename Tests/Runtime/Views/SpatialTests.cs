using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

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
            TestGhost ego = _source.PublishPosition("ego", new GeoPosition(0, 0, 0).ToEarthCentered());
            TestGhost target = _source.PublishPosition("target", new GeoPosition(0, 0, 10).ToEarthCentered());
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
            ego.GetComponent<Spatial>().SetPosition(new Double3(double.MaxValue, double.MaxValue, double.MaxValue));
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            AssertPosition(target.transform.position, projected, 1);
            _source.RemoveEntity("ego");
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.False);
            AssertPosition(target.transform.position, projected, 1);

            TestGhost replacement = _source.PublishPosition("ego", new GeoPosition(0, 0, 0).ToEarthCentered());
            _realm.Update();
            Assert.That(frame.IsReferenceAvailable, Is.True);
            AssertPosition(target.transform.position, new Vector3(0, 10, 0));
            AssertRotation(target.transform.rotation, Quaternion.identity);
            Assert.That(ActiveView(target), Is.SameAs(view));
        }

        /// <summary>
        /// Bound WGS84 readers on separate anchors project a target against the latest reference without repeated detection.
        /// </summary>
        [Test]
        public void GeographicReaders_FollowAnotherAnchorAndRefreshWithoutRediscovery()
        {
            GameObject rootPrefab = new GameObject("geographic root");
            rootPrefab.SetActive(false);
            Ghost root = rootPrefab.AddComponent<Ghost>();
            rootPrefab.AddComponent<GeographicModule>();
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
                ghost.GetComponent<GeographicModule>().Bind(() => ((GeographicReading)presence.Source).Position);
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

            reference.Root.GetComponent<Spatial>().SetRotation(Quaternion.AngleAxis(90, Vector3.up));
            frame.FollowRotation = true;
            _realm.Update();
            AssertPosition(target.Root.transform.position, new Vector3(-5.563909085825f, 4.999993968840f, 6.787977894133f));
            AssertPosition(reference.Root.transform.position, Vector3.zero);
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
            spatial.SetPosition(new Double3(6378137, 0, 0));
            _realm.Update();
            View view = ActiveView(target);
            Assert.That(view, Is.Not.Null);
            AssertRotation(target.transform.rotation, Quaternion.identity);
            Assert.That(spatial.UsesEarthCenteredRotation, Is.True);
            Assert.Throws<ArgumentException>(() => spatial.SetEarthCenteredRotation(Quaternion.identity, CoordinateSystem.Unity));

            spatial.SetPosition(new GeoPosition(0, 90, 0).ToEarthCentered());
            _realm.Update();
            AssertRotation(spatial.Rotation, north);
            AssertRotation(target.transform.rotation, Quaternion.identity);
            frame.Position = spatial.Position;
            _realm.Update();
            AssertRotation(target.transform.rotation, Quaternion.AngleAxis(90, Vector3.forward));
            Assert.That(ActiveView(target), Is.SameAs(view));

            TestGhost local = _source.PublishPosition("local", spatial.Position);
            _source.PublishRotation("local", Quaternion.identity);
            frame.FollowedGhost = target.Key;
            frame.FollowRotation = true;
            _realm.Update();
            Assert.That(frame.UsesEarthCenteredRotation, Is.True);
            AssertRotation(target.transform.rotation, Quaternion.identity);
            AssertRotation(local.transform.rotation, Quaternion.AngleAxis(-90, Vector3.forward));
            Assert.That(ActiveView(target), Is.SameAs(view));

            spatial.SetRotation(Quaternion.identity);
            _realm.Update();
            Assert.That(spatial.UsesEarthCenteredRotation, Is.False);
            Assert.That(frame.UsesEarthCenteredRotation, Is.False);
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
        /// Range also suppresses root geometry and physics, restoring only previously enabled components.
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

            _source.PublishPosition("remote", new Double3(6, 0, 0));
            _realm.Update();
            Assert.That(visibleRenderer.enabled, Is.True);
            Assert.That(activeCollider.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
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
        private sealed class GeographicModule : EntityModule<GeoPosition>
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
                GetSpatial(ghost).SetPosition(position);
                return ghost;
            }

            internal TestGhost PublishRotation(string entityId, Quaternion rotation)
            {
                TestGhost ghost = Publish(entityId);
                GetSpatial(ghost).SetRotation(rotation);
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
