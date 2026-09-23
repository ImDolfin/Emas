using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies reference configuration and conversion between simulation and Unity coordinates.
    /// </summary>
    public sealed class ReferenceFrameTests
    {
        /// <summary>
        /// A frame starts without a reference position and with neutral rotations and placement.
        /// </summary>
        [Test]
        public void Defaults_RequireAnExplicitReferencePosition()
        {
            ReferenceFrame frame = new ReferenceFrame();

            Assert.That(frame.HasPosition, Is.False);
            Assert.That(frame.IsReferenceAvailable, Is.False);
            Assert.That(frame.Position, Is.EqualTo(default(Double3)));
            Assert.That(frame.Rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(frame.UnityPosition, Is.EqualTo(Vector3.zero));
            Assert.That(frame.UnityRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(frame.FollowRotation, Is.True);
            Assert.That(frame.MaxDistance, Is.Null);
            Assert.That(frame.FollowedGhost, Is.Null);
            Assert.That(frame.TryToUnityPosition(default, out Vector3 unused), Is.False);
            Assert.Throws<InvalidOperationException>(() => frame.ToSimulationPosition(Vector3.zero));
            Assert.Throws<InvalidOperationException>(() => frame.ToUnityRotation(Quaternion.identity));
            Assert.Throws<InvalidOperationException>(() => frame.ToSimulationRotation(Quaternion.identity));
            Assert.Throws<InvalidOperationException>(() => frame.DistanceTo(default));
        }

        /// <summary>
        /// A manually supplied origin enables projection and retains small offsets at large coordinates.
        /// </summary>
        [Test]
        public void ManualOrigin_PreservesNearbyFractionalCoordinates()
        {
            ReferenceFrame frame = new ReferenceFrame();
            frame.Position = new Double3(1e12 + 0.125d, -1e12 + 0.25d, 1e12 + 0.5d);
            frame.UnityPosition = new Vector3(5f, 6f, 7f);
            Double3 entity = frame.Position + new Double3(20.25d, 4.5d, -2.75d);

            Assert.That(frame.HasPosition, Is.True);
            Assert.That(frame.IsReferenceAvailable, Is.True);
            Assert.That(frame.TryToUnityPosition(entity, out Vector3 unity), Is.True);
            Assert.That(unity, Is.EqualTo(new Vector3(25.25f, 10.5f, 4.25f)));
            Assert.That(frame.ToSimulationPosition(unity), Is.EqualTo(entity));
            Assert.That(frame.DistanceTo(frame.Position + new Double3(3d, 4d, 0d)), Is.EqualTo(5d));
        }

        /// <summary>
        /// Projection combines an inverse reference rotation with the desired Unity rotation.
        /// </summary>
        [Test]
        public void PoseConversions_RoundTripLargeOriginsAndRotatedUnityPlacement()
        {
            ReferenceFrame frame = new ReferenceFrame();
            frame.Position = new Double3(1e12 + 0.125d, -1e12 + 0.25d, 1e12 + 0.5d);
            frame.Rotation = new Quaternion(0f, 1f, 0f, 1f);
            frame.UnityPosition = new Vector3(5f, 6f, 7f);
            frame.UnityRotation = new Quaternion(0f, 0f, 1f, 1f);
            Double3 entity = frame.Position + new Double3(20.25d, 4.5d, -2.75d);
            Quaternion entityRotation = Quaternion.Euler(15f, 25f, 35f);

            Assert.That(frame.TryToUnityPosition(entity, out Vector3 unity), Is.True);
            Assert.That(Vector3.Distance(unity, new Vector3(0.5f, 8.75f, 27.25f)), Is.LessThan(0.00001f));
            Assert.That(Double3.Distance(frame.ToSimulationPosition(unity), entity), Is.LessThan(0.001d));
            Quaternion projectedRotation = frame.ToUnityRotation(entityRotation);
            Quaternion expectedRotation = frame.UnityRotation * Quaternion.Inverse(frame.Rotation) * entityRotation;
            Assert.That(Quaternion.Angle(projectedRotation, expectedRotation), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(projectedRotation), entityRotation), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(frame.Rotation), frame.UnityRotation), Is.LessThan(0.05f));
        }

        /// <summary>
        /// Position-only following ignores network reference heading while retaining Unity placement.
        /// </summary>
        [Test]
        public void PositionOnlyMode_IgnoresReferenceRotation()
        {
            ReferenceFrame frame = new ReferenceFrame();
            frame.Position = new Double3(1000d, 2000d, 3000d);
            frame.Rotation = new Quaternion(0f, 1f, 0f, 1f);
            frame.UnityPosition = new Vector3(5f, 6f, 7f);
            frame.UnityRotation = new Quaternion(0f, 0f, 1f, 1f);
            frame.FollowRotation = false;
            Double3 entity = frame.Position + new Double3(20d, 4d, -2d);
            Quaternion entityRotation = Quaternion.Euler(15f, 25f, 35f);

            Assert.That(frame.TryToUnityPosition(entity, out Vector3 unity), Is.True);
            Assert.That(Vector3.Distance(unity, new Vector3(1f, 26f, 5f)), Is.LessThan(0.00001f));
            Assert.That(Double3.Distance(frame.ToSimulationPosition(unity), entity), Is.LessThan(0.00001d));
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(entityRotation), frame.UnityRotation * entityRotation), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(frame.ToUnityRotation(entityRotation)), entityRotation), Is.LessThan(0.05f));
        }

        /// <summary>
        /// Presentation distance is measured in double simulation coordinates and includes its boundary.
        /// </summary>
        [Test]
        public void MaxDistance_UsesSimulationDistanceAndCanBeCleared()
        {
            ReferenceFrame frame = new ReferenceFrame();
            frame.Position = new Double3(1e12, 1e12, 1e12);
            frame.UnityPosition = new Vector3(500f, 600f, 700f);
            frame.MaxDistance = 5d;

            Assert.That(frame.MaxDistance, Is.EqualTo(5d));
            Assert.That(frame.TryToUnityPosition(frame.Position + new Double3(3d, 4d, 0d), out Vector3 boundary), Is.True);
            Assert.That(boundary, Is.EqualTo(new Vector3(503f, 604f, 700f)));
            Double3 outside = frame.Position + new Double3(3d, 4.25d, 0d);
            Assert.That(frame.TryToUnityPosition(outside, out Vector3 unused), Is.False);
            Assert.That(frame.DistanceTo(outside), Is.GreaterThan(5d));
            frame.MaxDistance = null;
            Assert.That(frame.TryToUnityPosition(outside, out unused), Is.True);
            Assert.That(frame.MaxDistance, Is.Null);
        }

        /// <summary>
        /// Unrepresentable relative positions are not written to Unity transforms.
        /// </summary>
        [Test]
        public void Projection_RejectsFloatOverflowEvenWithoutARangeLimit()
        {
            ReferenceFrame frame = new ReferenceFrame();
            frame.Position = default;

            Assert.That(frame.TryToUnityPosition(new Double3(1e100, 0d, 0d), out Vector3 unused), Is.False);
            frame.Position = new Double3(-double.MaxValue, 0d, 0d);
            Assert.That(frame.TryToUnityPosition(new Double3(double.MaxValue, 0d, 0d), out unused), Is.False);
        }

        /// <summary>
        /// Invalid configuration is rejected while valid nonunit rotations are normalized.
        /// </summary>
        [Test]
        public void Configuration_RejectsInvalidValuesAndNormalizesRotations()
        {
            ReferenceFrame frame = new ReferenceFrame();
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.MaxDistance = 0d);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.MaxDistance = -1d);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.MaxDistance = double.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.MaxDistance = double.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.UnityPosition = new Vector3(float.NaN, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.UnityPosition = new Vector3(0f, float.PositiveInfinity, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Rotation = default);
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.UnityRotation = new Quaternion(0f, 0f, 0f, float.NaN));
            frame.Rotation = new Quaternion(0f, 0f, 0f, 4f);
            frame.UnityRotation = new Quaternion(0f, 0f, 0f, 2f);
            Assert.That(frame.Rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(frame.UnityRotation, Is.EqualTo(Quaternion.identity));
            frame.Position = default;
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.ToSimulationPosition(new Vector3(0f, 0f, float.PositiveInfinity)));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.ToUnityRotation(default));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.ToSimulationRotation(default));
        }

        /// <summary>
        /// Following retains exact ghost identity and can be disabled for a manual origin.
        /// </summary>
        [Test]
        public void FollowedGhost_AcceptsAnIdentityAndCanBeCleared()
        {
            ReferenceFrame frame = new ReferenceFrame();
            Key key = new Key("vehicles", new Kind("cars"), "driver");
            frame.FollowedGhost = key;
            Assert.That(frame.FollowedGhost, Is.EqualTo(key));
            frame.FollowedGhost = null;
            Assert.That(frame.FollowedGhost, Is.Null);
            Assert.Throws<ArgumentException>(() => frame.FollowedGhost = default(Key));
        }
    }
}
