using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Verifies late-bound entity attachments and the handoff back to absolute spatial input.</summary>
    public sealed class AttachmentTests
    {
        private static readonly Kind AttachmentKind = new Kind("attachment.entity");
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private Realm _realm;
        private Source _parts;
        private Source _parents;
        private Anchor _partsAnchor;

        /// <summary>Creates independent part and parent detectors in one realm.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm();
            _parts = new Source();
            _parents = new Source();
            _partsAnchor = _realm.GetOrCreateAnchor("parts", _parts);
            _realm.GetOrCreateAnchor("parents", _parents);
        }

        /// <summary>Releases entity lifetimes and authored presentation assets.</summary>
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
        /// Parts discovered before their parents retain tracking, resolve chains before presentation, and survive parent replacement.
        /// </summary>
        [Test]
        public void LateParent_ResolvesChainsAndRecoversAfterRediscovery()
        {
            RegisterView();
            _partsAnchor.Transform.SetPositionAndRotation(new Vector3(100, -20, 7), Quaternion.AngleAxis(20, Vector3.up));
            _partsAnchor.Transform.localScale = new Vector3(2, 3, 4);
            Ghost tip = _parts.Arrive("tip");
            Spatial tipSpatial = tip.GetComponent<Spatial>();
            tipSpatial.Attach(new Key("parts", AttachmentKind, "mount"), Vector3.right,
                Quaternion.AngleAxis(30, Vector3.right));
            _realm.Manifest(tip);
            Ghost mount = _parts.Arrive("mount");
            Spatial mountSpatial = mount.GetComponent<Spatial>();
            Key parentKey = new Key("parents", AttachmentKind, "vehicle");
            mountSpatial.Attach(parentKey, new Vector3(0, 0, 2), Quaternion.AngleAxis(-90, Vector3.up));
            _realm.Manifest(mount);
            MeshRenderer renderer = tip.gameObject.AddComponent<MeshRenderer>();
            BoxCollider collider = tip.gameObject.AddComponent<BoxCollider>();
            _realm.Update();

            Assert.That(tip.IsAvailable, Is.True);
            Assert.That(_realm.Query().Count, Is.EqualTo(2));
            Assert.That(tipSpatial.IsInRange, Is.False);
            Assert.That(mountSpatial.IsInRange, Is.False);
            Assert.That(tip.GetComponentInChildren<View>(), Is.Null);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(collider.enabled, Is.False);

            Ghost parent = _parents.Arrive("vehicle");
            Spatial parentSpatial = parent.GetComponent<Spatial>();
            parentSpatial.SetCartesianPosition(new Double3(10, 20, 30));
            parentSpatial.SetSourceRotation(Quaternion.AngleAxis(90, Vector3.up));
            parent.transform.localScale = new Vector3(2, 3, 4);
            _realm.Update();

            AssertPosition(mount, new Vector3(12, 20, 30));
            AssertPosition(tip, new Vector3(13, 20, 30));
            AssertRotation(tip, Quaternion.AngleAxis(30, Vector3.right));
            Assert.That(tipSpatial.HasPosition, Is.False);
            Assert.That(tipSpatial.HasRotation, Is.False);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(collider.enabled, Is.True);
            Assert.That(tip.GetComponentInChildren<View>(), Is.Not.Null);
            Assert.That(tip.transform.parent, Is.SameAs(_partsAnchor.Transform));
            Assert.That(mount.transform.parent, Is.SameAs(_partsAnchor.Transform));

            parentSpatial.SetCartesianPosition(new Double3(20, 30, 40));
            parentSpatial.SetSourceRotation(Quaternion.AngleAxis(180, Vector3.up));
            _realm.Update();
            AssertPosition(tip, new Vector3(20, 30, 37));
            AssertRotation(tip, Quaternion.AngleAxis(90, Vector3.up) * Quaternion.AngleAxis(30, Vector3.right));

            _parents.Leave("vehicle");
            _realm.Update();
            Assert.That(_realm.TryGetGhost(tip.Key, out IGhost retained), Is.True);
            Assert.That(retained, Is.SameAs(tip));
            Assert.That(tip.IsAvailable, Is.True);
            Assert.That(tipSpatial.IsInRange, Is.False);
            Assert.That(mountSpatial.AttachedTo, Is.EqualTo(parentKey));
            Assert.That(tip.GetComponentInChildren<View>(), Is.Null);

            Ghost replacement = _parents.Arrive("vehicle");
            Assert.That(replacement, Is.Not.SameAs(parent));
            replacement.GetComponent<Spatial>().SetCartesianPosition(new Double3(5, 6, 7));
            replacement.GetComponent<Spatial>().SetSourceRotation(Quaternion.identity);
            _realm.Update();
            AssertPosition(tip, new Vector3(5, 6, 10));
            Assert.That(tip.GetComponentInChildren<View>(), Is.Not.Null);
        }

        /// <summary>
        /// Attachment ignores independently timed absolute packets around a moving geographic reference and detaches to the newest pose.
        /// </summary>
        [Test]
        public void GeographicReference_AttachmentOverridesPacketsAndDetachResumesLatestPose()
        {
            Key parentKey = new Key("parents", AttachmentKind, "vehicle");
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                Coordinates = CoordinateSystem.NorthEastDown,
                FollowedGhost = parentKey,
                UnityPosition = new Vector3(5, 6, 7),
                UnityRotation = Quaternion.AngleAxis(90, Vector3.up),
                MaxDistance = 10
            };
            _realm.ReferenceFrame = frame;
            Ghost part = _parts.Arrive("part");
            Spatial spatial = part.GetComponent<Spatial>();
            GeoPosition stalePosition = new GeoPosition(0, 0.1, 0);
            spatial.SetGeographicPosition(stalePosition);
            spatial.SetGeographicRotation(10, 20, 30);
            spatial.Attach(parentKey, new Vector3(1, 0, 2));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.False);

            Ghost parent = _parents.Arrive("vehicle");
            Spatial parentSpatial = parent.GetComponent<Spatial>();
            parentSpatial.SetGeographicPosition(new GeoPosition(0, 0, 0));
            parentSpatial.SetGeographicRotation(90, 0, 0);
            _realm.Update();
            AssertPosition(part, new Vector3(7, 6, 6));
            AssertRotation(part, frame.UnityRotation);
            Assert.That(spatial.Position, Is.EqualTo(stalePosition.ToEarthCentered()));

            GeoPosition movedPosition = new GeoPosition(10, 10, 100);
            parentSpatial.SetGeographicPosition(movedPosition);
            parentSpatial.SetGeographicRotation(180, 30, 20);
            spatial.SetGeographicPosition(new GeoPosition(-20, -20, 0));
            _realm.Update();
            AssertPosition(part, new Vector3(7, 6, 6));
            AssertRotation(part, frame.UnityRotation);

            // Offsets use Unity axes even with NED source quaternions, and apply the presentation range themselves.
            spatial.Attach(parentKey, new Vector3(11, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.False);
            spatial.Attach(parentKey, new Vector3(10, 0, 0));
            _realm.Update();
            Assert.That(spatial.IsInRange, Is.True);

            spatial.SetGeographicPosition(movedPosition);
            spatial.SetGeographicRotation(180, 30, 20);
            Quaternion cachedRotation = spatial.Rotation;
            spatial.Detach();
            spatial.Detach();
            _realm.Update();
            Assert.That(spatial.AttachedTo, Is.Null);
            Assert.That(spatial.Position, Is.EqualTo(movedPosition.ToEarthCentered()));
            Assert.That(spatial.Rotation, Is.EqualTo(cachedRotation));
            Assert.That(spatial.RotationSpace, Is.EqualTo(RotationSpace.Geographic));
            AssertPosition(part, frame.UnityPosition);
            AssertRotation(part, frame.UnityRotation);
        }

        /// <summary>
        /// Cyclic attachment suppresses the cycle and its descendants without stopping tracking, and breaking it restores placement.
        /// </summary>
        [Test]
        public void AttachmentCycle_HidesDependentsAndRecoversWhenBroken()
        {
            Ghost child = _parts.Arrive("child");
            Ghost first = _parts.Arrive("first");
            Ghost second = _parts.Arrive("second");
            Spatial firstSpatial = first.GetComponent<Spatial>();
            Spatial secondSpatial = second.GetComponent<Spatial>();
            Spatial childSpatial = child.GetComponent<Spatial>();
            childSpatial.Attach(first.Key, Vector3.forward);
            firstSpatial.Attach(second.Key, Vector3.right);
            secondSpatial.Attach(first.Key, Vector3.up);
            secondSpatial.SetCartesianPosition(new Double3(10, 20, 30));
            _realm.Update();

            Assert.That(firstSpatial.IsInRange, Is.False);
            Assert.That(secondSpatial.IsInRange, Is.False);
            Assert.That(childSpatial.IsInRange, Is.False);
            Assert.That(_realm.Query().Count, Is.EqualTo(3));
            Assert.That(_parts.LastError, Is.Null);

            secondSpatial.Detach();
            _realm.Update();
            AssertPosition(second, new Vector3(10, 20, 30));
            AssertPosition(first, new Vector3(11, 20, 30));
            AssertPosition(child, new Vector3(11, 20, 31));
            Assert.That(childSpatial.IsInRange, Is.True);

            // An unavailable spatial parent suppresses its attached dependents and recovers with a new absolute pose.
            _realm.ReferenceFrame = new ReferenceFrame { Position = default, MaxDistance = 5 };
            _realm.Update();
            Assert.That(childSpatial.IsInRange, Is.False);
            secondSpatial.SetCartesianPosition(new Double3(1, 0, 0));
            _realm.Update();
            AssertPosition(child, new Vector3(2, 0, 1));
            Assert.That(childSpatial.IsInRange, Is.True);
        }

        /// <summary>
        /// Invalid requests preserve a pending attachment, and cancelling it needs an absolute position before presentation can resume.
        /// </summary>
        [Test]
        public void InvalidAttachment_PreservesPendingTargetAndDetachWaitsForAbsolutePose()
        {
            Ghost part = _parts.Arrive("part");
            Spatial spatial = part.GetComponent<Spatial>();
            Key missing = new Key("parents", AttachmentKind, "later");
            spatial.Attach(missing, Vector3.right);
            Assert.Throws<ArgumentException>(() => spatial.Attach(default, Vector3.zero));
            Assert.Throws<ArgumentException>(() => spatial.Attach(part.Key, Vector3.zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => spatial.Attach(missing, new Vector3(float.NaN, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => spatial.Attach(missing, Vector3.zero, default));
            Assert.That(spatial.AttachedTo, Is.EqualTo(missing));

            spatial.Detach();
            _realm.Update();
            Assert.That(spatial.AttachedTo, Is.Null);
            Assert.That(spatial.IsInRange, Is.False);
            spatial.SetCartesianPosition(new Double3(2, 3, 4));
            _realm.Update();
            AssertPosition(part, new Vector3(2, 3, 4));
            Assert.That(spatial.IsInRange, Is.True);
        }

        private void RegisterView()
        {
            GameObject prefab = new GameObject("attached view");
            prefab.SetActive(false);
            ManifestationBlueprint blueprint = ScriptableObject.CreateInstance<ManifestationBlueprint>();
            blueprint.Configure(AttachmentKind, null, null, prefab);
            _assets.Add(prefab);
            _assets.Add(blueprint);
            _realm.RegisterManifestationBlueprint(blueprint);
        }

        private static void AssertPosition(Ghost ghost, Vector3 expected)
        {
            Assert.That(Vector3.Distance(ghost.transform.position, expected), Is.LessThan(0.0001f));
        }

        private static void AssertRotation(Ghost ghost, Quaternion expected)
        {
            Assert.That(Quaternion.Angle(ghost.transform.rotation, expected), Is.LessThan(0.05f));
        }

        [RequireComponent(typeof(Spatial))]
        private sealed class Root : Ghost
        {
        }

        private sealed class Source : PresenceDetector
        {
            internal Ghost Arrive(string entityId)
            {
                return GetOrCreate<Root>(entityId, AttachmentKind);
            }

            internal void Leave(string entityId)
            {
                Disappear(AttachmentKind, entityId);
            }
        }
    }
}
