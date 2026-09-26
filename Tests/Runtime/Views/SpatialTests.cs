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
        /// Spatial state does not take over transforms until a reference frame is configured.
        /// </summary>
        [Test]
        public void ReferenceFrame_IsOptionalAndCanBeDisabled()
        {
            Assert.That(_realm.ReferenceFrame, Is.Null);
            TestGhost ghost = _source.PublishPosition("remote", new Double3(1000, 0, 10));
            Vector3 original = new Vector3(2, 3, 4);
            ghost.transform.position = original;
            _realm.Update();
            AssertPosition(ghost.transform.position, original);

            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1000, 0, 0) };
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0, 0, 10));

            _realm.ReferenceFrame = null;
            _source.PublishPosition("remote", new Double3(1000, 0, 20));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(0, 0, 10));
        }

        /// <summary>
        /// Entities without a Spatial component retain their existing placement and view behavior.
        /// </summary>
        [Test]
        public void NonSpatialGhost_RetainsLegacyPlacementAndPresentation()
        {
            RegisterView();
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1000000, 0, 0), MaxDistance = 1 };
            TestGhost ghost = _source.Publish("plain");
            ghost.transform.localPosition = new Vector3(20, 30, 40);
            _realm.Update();

            AssertPosition(ghost.transform.localPosition, new Vector3(20, 30, 40));
            Assert.That(_realm.Manifest(ghost), Is.Not.Null);
            Assert.That(ghost.IsAvailable, Is.True);
        }

        /// <summary>
        /// Double subtraction retains nearby millimetre-scale offsets at large simulation coordinates.
        /// </summary>
        [Test]
        public void Projection_SubtractsBeforeConvertingToFloat()
        {
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1e12, -1e12, 1e12) };
            TestGhost ghost = _source.PublishPosition("remote", new Double3(1e12 + 0.001953125, -1e12 + 0.125, 1e12 + 20.25));
            _realm.Update();

            AssertPosition(ghost.transform.position, new Vector3(0.001953125f, 0.125f, 20.25f), 0.00001f);
            Assert.That(ghost.GetComponent<Spatial>().HasPosition, Is.True);
        }

        /// <summary>
        /// Moving the reference reprojects stationary entities without another publication.
        /// </summary>
        [Test]
        public void ReferenceMovement_ReprojectsCachedEntityPosition()
        {
            ReferenceFrame frame = new ReferenceFrame { Position = new Double3(1e9, 0, 0) };
            _realm.ReferenceFrame = frame;
            TestGhost ghost = _source.PublishPosition("remote", new Double3(1e9 + 20.25, 0, 0));
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(20.25f, 0, 0));

            frame.Position = new Double3(1e9 + 2, 0, 0);
            _realm.Update();
            AssertPosition(ghost.transform.position, new Vector3(18.25f, 0, 0));
        }

        /// <summary>
        /// Translation-only and orientation-following frames both respect the desired Unity pose.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Projection_UsesConfiguredReferenceAndUnityPoses(bool followRotation)
        {
            Quaternion networkRotation = Quaternion.Euler(0, 90, 0);
            Quaternion unityRotation = Quaternion.Euler(0, 30, 0);
            Quaternion entityRotation = Quaternion.Euler(10, 120, 5);
            Vector3 unityPosition = new Vector3(3, 4, 5);
            _realm.ReferenceFrame = new ReferenceFrame
            {
                Position = new Double3(100, 0, 0),
                Rotation = networkRotation,
                UnityPosition = unityPosition,
                UnityRotation = unityRotation,
                FollowRotation = followRotation
            };
            TestGhost ghost = _source.PublishPosition("remote", new Double3(100, 0, 10));
            _source.PublishRotation("remote", entityRotation);
            _realm.Update();

            Quaternion mapping = unityRotation * (followRotation ? Quaternion.Inverse(networkRotation) : Quaternion.identity);
            AssertPosition(ghost.transform.position, unityPosition + mapping * new Vector3(0, 0, 10));
            AssertRotation(ghost.transform.rotation, mapping * entityRotation);
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
        [Test]
        public void MissingFirstPosition_DefersRequestedViewUntilPositionArrives()
        {
            RegisterView();
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(1000, 0, 0) };
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
            AssertPosition(ghost.transform.position, new Vector3(10, 0, 0));
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
                View firstView = _realm.Manifest(ghost, DetailLevel.Reduced);
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
                    Assert.That(restored.RequestedDetailLevel, Is.EqualTo(DetailLevel.Reduced));
                }

                Assert.That(arrivals, Is.EqualTo(1));
                Assert.That(departures, Is.Zero);
                AssertPosition(ghost.transform.position, new Vector3(0, 0.001f, 0));
            }
        }

        /// <summary>
        /// A distant entity remains tracked without writing enormous coordinates into its Transform.
        /// </summary>
        [Test]
        public void FarEntity_RetainsFiniteNearbyTransformUntilInRange()
        {
            RegisterView();
            ReferenceFrame frame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 500 };
            _realm.ReferenceFrame = frame;
            TestGhost ghost = _source.PublishPosition("remote", new Double3(1e100, 0, 0));
            Vector3 retainedPosition = new Vector3(1, 2, 3);
            ghost.transform.position = retainedPosition;
            _realm.Update();

            AssertPosition(ghost.transform.position, retainedPosition);
            Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.False);
            Assert.That(_realm.Manifest(ghost), Is.Null);
            Assert.That(_realm.Query().Count, Is.EqualTo(1));

            frame.Position = new Double3(1e100, 0, 0);
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
            Assert.That(visibleRenderer.enabled, Is.True);
            Assert.That(activeCollider.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
            Assert.That(ActiveView(ghost), Is.Not.Null);
        }

        /// <summary>
        /// Geometry created by root activation is suppressed before an out-of-range update completes.
        /// </summary>
        [Test]
        public void RangeSuppression_CatchesGeometryCreatedDuringRootActivation()
        {
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 10 };
            TestGhost ghost = null;
            CreateGeometryOnEnable probe = null;
            _source.NextUpdate = () =>
            {
                ghost = _source.PublishPosition("remote", new Double3(50, 0, 0));
                probe = ghost.gameObject.AddComponent<CreateGeometryOnEnable>();
            };
            _realm.Update();

            Assert.That(probe.CreatedEnabledGeometry, Is.True);
            Assert.That(probe.Renderer.enabled, Is.False);
            Assert.That(probe.Collider.enabled, Is.False);
            Assert.That(ghost.GetComponent<Spatial>().IsInRange, Is.False);
            Assert.That(ghost.gameObject.activeInHierarchy, Is.True);

            _source.PublishPosition("remote", new Double3(5, 0, 0));
            _realm.Update();
            Assert.That(probe.Renderer.enabled, Is.True);
            Assert.That(probe.Collider.enabled, Is.True);
        }

        /// <summary>
        /// Temporarily hiding a hierarchy must not release range suppression when it becomes active again.
        /// </summary>
        [Test]
        public void RangeSuppression_SurvivesAnchorHierarchyDisableAndEnable()
        {
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 10 };
            TestGhost ghost = _source.PublishPosition("remote", new Double3(50, 0, 0));
            MeshRenderer renderer = ghost.gameObject.AddComponent<MeshRenderer>();
            BoxCollider collider = ghost.gameObject.AddComponent<BoxCollider>();
            _realm.Update();
            Spatial spatial = ghost.GetComponent<Spatial>();
            Assert.That(spatial.IsInRange, Is.False);

            _anchor.Transform.gameObject.SetActive(false);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(spatial.IsInRange, Is.False);
            _anchor.Transform.gameObject.SetActive(true);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(spatial.IsInRange, Is.False);
            Assert.That(ghost.gameObject.activeInHierarchy, Is.True);

            spatial.enabled = false;
            Assert.That(renderer.enabled, Is.True, "Explicitly disabling Spatial releases its presentation control.");
            Assert.That(collider.enabled, Is.True);
            Assert.That(spatial.IsInRange, Is.True);
        }

        /// <summary>
        /// Removing spatial control restores root geometry immediately and resumes requested views on update.
        /// </summary>
        [Test]
        public void RemovingSpatial_RestoresGeometryAndRequestedPresentation()
        {
            RegisterView();
            _realm.ReferenceFrame = new ReferenceFrame { Position = new Double3(0, 0, 0), MaxDistance = 10 };
            TestGhost ghost = _source.PublishPosition("remote", new Double3(50, 0, 0));
            MeshRenderer renderer = ghost.gameObject.AddComponent<MeshRenderer>();
            BoxCollider collider = ghost.gameObject.AddComponent<BoxCollider>();
            GameObject child = new GameObject("deliberately disabled geometry");
            child.transform.SetParent(ghost.transform, false);
            MeshRenderer disabledRenderer = child.AddComponent<MeshRenderer>();
            BoxCollider disabledCollider = child.AddComponent<BoxCollider>();
            disabledRenderer.enabled = false;
            disabledCollider.enabled = false;
            _realm.Update();
            Assert.That(_realm.Manifest(ghost), Is.Null);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(collider.enabled, Is.False);

            UnityEngine.Object.DestroyImmediate(ghost.GetComponent<Spatial>());
            Assert.That(renderer.enabled, Is.True);
            Assert.That(collider.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(disabledCollider.enabled, Is.False);
            Assert.That(ghost.IsAvailable, Is.True);
            _realm.Update();
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

        private sealed class CreateGeometryOnEnable : MonoBehaviour
        {
            internal MeshRenderer Renderer { get; private set; }
            internal BoxCollider Collider { get; private set; }
            internal bool CreatedEnabledGeometry { get; private set; }

            private void OnEnable()
            {
                Renderer = gameObject.AddComponent<MeshRenderer>();
                Collider = gameObject.AddComponent<BoxCollider>();
                CreatedEnabledGeometry = Renderer.enabled && Collider.enabled;
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
