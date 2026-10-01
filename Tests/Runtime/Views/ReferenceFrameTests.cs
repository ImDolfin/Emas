using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies reference configuration and conversion between shared coordinates and Unity coordinates.
    /// </summary>
    public sealed class ReferenceFrameTests
    {
        /// <summary>
        /// Projection requires an explicit reference position so uninitialized frames cannot invent a world origin.
        /// </summary>
        [Test]
        public void Defaults_RequireAnExplicitReferencePosition()
        {
            ReferenceFrame frame = new ReferenceFrame();

            Assert.That(frame.HasPosition, Is.False);
            Assert.That(frame.IsReferenceAvailable, Is.False);
            Assert.That(frame.TryToUnityPosition(default(Double3), out Vector3 unused), Is.False);
            Assert.Throws<InvalidOperationException>(() => frame.ToSimulationPosition(Vector3.zero));
            Assert.Throws<InvalidOperationException>(() => frame.ToUnityRotation(Quaternion.identity));
            Assert.Throws<InvalidOperationException>(() => frame.ToSimulationRotation(Quaternion.identity));
            Assert.Throws<InvalidOperationException>(() => frame.DistanceTo(default));
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
        /// Geographic presets map physical directions and handed rotations into Unity's right/up/forward axes.
        /// </summary>
        [Test]
        public void GeographicCoordinates_MapPositionsAndRotations()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                Position = default,
                Coordinates = CoordinateSystem.EastNorthUp
            };
            Assert.That(frame.TryToUnityPosition(new Double3(2, 3, 5), out Vector3 enu), Is.True);
            Assert.That(enu, Is.EqualTo(new Vector3(2, 5, 3)));
            Quaternion sourceTurn = Quaternion.AngleAxis(90, Vector3.forward);
            Assert.That(Vector3.Distance(frame.ToUnityRotation(sourceTurn) * Vector3.forward, Vector3.left),
                Is.LessThan(0.0001f));

            frame.Coordinates = CoordinateSystem.NorthEastDown;
            Assert.That(frame.TryToUnityPosition(new Double3(2, 3, 5), out Vector3 ned), Is.True);
            Assert.That(ned, Is.EqualTo(new Vector3(3, -5, 2)));
            Assert.That(Vector3.Distance(frame.ToUnityRotation(sourceTurn) * Vector3.forward, Vector3.right),
                Is.LessThan(0.0001f));

            Quaternion source = Quaternion.AngleAxis(30, Vector3.right)
                * Quaternion.AngleAxis(40, Vector3.up) * Quaternion.AngleAxis(50, Vector3.forward);
            Quaternion expected = Quaternion.AngleAxis(-30, Vector3.forward)
                * Quaternion.AngleAxis(-40, Vector3.right) * Quaternion.AngleAxis(50, Vector3.up);
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(source), expected), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(expected), source), Is.LessThan(0.05f));
            Assert.That(Double3.Distance(frame.ToSimulationPosition(ned), new Double3(2, 3, 5)), Is.LessThan(0.00001d));
        }

        /// <summary>
        /// Serialized custom axes compose with reference cancellation and Unity placement without losing large-origin precision.
        /// </summary>
        [Test]
        public void SerializedCustomCoordinates_RoundTripReferenceAndUnityPlacement()
        {
            CoordinateSystem coordinates = new CoordinateSystem(Axis.NegativeZ, Axis.NegativeX, Axis.PositiveY);
            ReferenceFrame frame = new ReferenceFrame
            {
                Position = new Double3(1e12, -1e12, 1e12),
                Coordinates = JsonUtility.FromJson<CoordinateSystem>(JsonUtility.ToJson(coordinates)),
                Rotation = Quaternion.AngleAxis(90, Vector3.right),
                UnityPosition = new Vector3(5, 6, 7),
                UnityRotation = Quaternion.AngleAxis(90, Vector3.forward)
            };
            Double3 source = frame.Position + new Double3(20.25d, 4.5d, -2.75d);
            Assert.That(frame.TryToUnityPosition(source, out Vector3 unity), Is.True);
            Assert.That(Vector3.Distance(unity, new Vector3(25.25f, 10.5f, 4.25f)), Is.LessThan(0.0001f));
            Assert.That(Double3.Distance(frame.ToSimulationPosition(unity), source), Is.LessThan(0.001d));

            Quaternion sourceRotation = frame.Rotation * Quaternion.AngleAxis(90, Vector3.forward);
            Quaternion expected = frame.UnityRotation * Quaternion.AngleAxis(-90, Vector3.right);
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(sourceRotation), expected), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(expected), sourceRotation), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(frame.Rotation), frame.UnityRotation), Is.LessThan(0.05f));
        }

        /// <summary>
        /// Position-only following preserves source axes and Unity placement while ignoring the reference orientation.
        /// </summary>
        [Test]
        public void PositionOnlyMode_RetainsCoordinateConvention()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                Position = new Double3(1000, 2000, 3000),
                Coordinates = CoordinateSystem.NorthEastDown,
                Rotation = Quaternion.AngleAxis(90, Vector3.forward),
                UnityPosition = new Vector3(5, 6, 7),
                UnityRotation = Quaternion.AngleAxis(90, Vector3.forward),
                FollowRotation = false
            };
            Double3 source = frame.Position + new Double3(3, 4, 5);
            Assert.That(frame.TryToUnityPosition(source, out Vector3 unity), Is.True);
            Assert.That(Vector3.Distance(unity, new Vector3(10, 10, 10)), Is.LessThan(0.0001f));
            Assert.That(Double3.Distance(frame.ToSimulationPosition(unity), source), Is.LessThan(0.0001d));

            Quaternion expected = frame.UnityRotation * Quaternion.AngleAxis(90, Vector3.up);
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(frame.Rotation), expected), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(expected), frame.Rotation), Is.LessThan(0.05f));
        }

        /// <summary>
        /// Presentation distance is measured in double-precision shared coordinates and includes its boundary.
        /// </summary>
        [Test]
        public void MaxDistance_UsesSimulationDistanceAndCanBeCleared()
        {
            ReferenceFrame frame = new ReferenceFrame();
            frame.Position = new Double3(1e12, 1e12, 1e12);
            frame.UnityPosition = new Vector3(500f, 600f, 700f);
            frame.MaxDistance = 5d;
            frame.Coordinates = CoordinateSystem.NorthEastDown;

            Assert.That(frame.MaxDistance, Is.EqualTo(5d));
            Assert.That(frame.TryToUnityPosition(frame.Position + new Double3(3d, 4d, 0d), out Vector3 boundary), Is.True);
            Assert.That(boundary, Is.EqualTo(new Vector3(504f, 600f, 703f)));
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

    }
}
