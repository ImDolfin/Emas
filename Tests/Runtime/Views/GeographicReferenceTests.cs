using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Verifies projection around a moving WGS84 tangent frame through public conversion APIs.</summary>
    public sealed class GeographicReferenceTests
    {
        /// <summary>Moving the geographic reference rotates north/up with its location instead of retaining a static tangent frame.</summary>
        [Test]
        public void ReferenceMovement_RebuildsTangentAxesAcrossTheGlobe()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                GeographicPosition = new GeoPosition(0, 0, 0),
                FollowRotation = false
            };
            Double3 stationary = new GeoPosition(0, 0, 10).ToEarthCentered();
            Assert.That(frame.TryToUnityPosition(stationary, out Vector3 local), Is.True);
            Assert.That(Vector3.Distance(local, new Vector3(0, 10, 0)), Is.LessThan(0.0001f));

            frame.GeographicPosition = new GeoPosition(0, 90, 0);
            Assert.That(frame.TryToUnityPosition(stationary, out local), Is.True);
            Assert.That(Vector3.Distance(local, new Vector3(-6378147, -6378137, 0)), Is.LessThan(1));
            Assert.That(frame.TryToUnityPosition(new GeoPosition(0, 90, 10), out local), Is.True);
            Assert.That(Vector3.Distance(local, new Vector3(0, 10, 0)), Is.LessThan(0.0001f));

            frame.GeographicPosition = new GeoPosition(0, 179.9999, 0);
            GeoPosition acrossDateLine = new GeoPosition(0, -179.9999, 0);
            Assert.That(frame.TryToUnityPosition(acrossDateLine, out local), Is.True);
            Assert.That(local.x, Is.EqualTo(22.263898).Within(0.0001));
            Assert.That(Math.Abs(local.y), Is.LessThan(0.0001));
            Assert.That(Double3.Distance(frame.ToGeographicPosition(local).ToEarthCentered(), acrossDateLine.ToEarthCentered()), Is.LessThan(0.00001));
        }

        /// <summary>Geographic projection preserves small displacements, placement, inverse conversion and distance limits.</summary>
        [Test]
        public void PlacementAndRange_PreserveDoublePrecisionAndRoundTripNearAPole()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                GeographicPosition = new GeoPosition(89.9999, 40, 1000),
                UnityPosition = new Vector3(5, 6, 7),
                UnityRotation = Quaternion.AngleAxis(90, Vector3.up),
                MaxDistance = 30
            };
            Vector3 expected = new Vector3(25.25f, 6.125f, 4.5f);
            Double3 earthCentered = frame.ToSimulationPosition(expected);
            Assert.That(frame.TryToUnityPosition(earthCentered, out Vector3 actual), Is.True);
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.00001f));
            GeoPosition geographic = frame.ToGeographicPosition(expected);
            Assert.That(frame.TryToUnityPosition(geographic, out actual), Is.True);
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.00001f));
            Assert.That(frame.DistanceTo(earthCentered), Is.EqualTo(Vector3.Distance(expected, frame.UnityPosition)).Within(0.00001));
            Assert.That(frame.TryToUnityPosition(frame.Position + new Double3(31, 0, 0), out actual), Is.False);
            Assert.That(frame.GeographicPosition.HeightMeters, Is.EqualTo(1000).Within(1e-7));
        }

        /// <summary>Attitudes use each entity's tangent plane, selected axes and the optional reference attitude cancellation.</summary>
        [Test]
        public void Attitudes_AccountForEntityLocationAndFollowOrientation()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                GeographicPosition = new GeoPosition(0, 0, 0),
                Coordinates = CoordinateSystem.NorthEastDown,
                Rotation = Quaternion.AngleAxis(90, Vector3.forward),
                UnityRotation = Quaternion.AngleAxis(25, Vector3.forward),
                FollowRotation = false
            };
            GeoPosition east = new GeoPosition(0, 90, 0);
            Quaternion attitude = Quaternion.AngleAxis(90, Vector3.forward);
            Quaternion expected = frame.UnityRotation * Quaternion.AngleAxis(-90, Vector3.forward)
                * Quaternion.AngleAxis(90, Vector3.up);
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(attitude, east), expected), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(expected, east), attitude), Is.LessThan(0.05f));

            frame.FollowRotation = true;
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(attitude, frame.GeographicPosition), frame.UnityRotation), Is.LessThan(0.05f));
            Quaternion projected = frame.ToUnityRotation(attitude, east);
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(projected, east), attitude), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToUnityRotation(attitude), frame.UnityRotation), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToSimulationRotation(frame.UnityRotation), attitude), Is.LessThan(0.05f));
        }

        /// <summary>ECEF attitudes honor source body axes and round-trip through scene alignment and reference following.</summary>
        [Test]
        public void EarthCenteredAttitudes_ConvertBodyFramesAndManualReference()
        {
            ReferenceFrame frame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                Position = new Double3(6378137, 0, 0),
                Coordinates = CoordinateSystem.NorthEastDown,
                FollowRotation = false
            };
            // At lat/lon zero, level north maps FRD body X to ECEF Z, Y to Y, and Z to -X.
            Quaternion north = Quaternion.AngleAxis(-90, Vector3.up);
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(north), Quaternion.identity), Is.LessThan(0.05f));
            Quaternion enuBody = Quaternion.AngleAxis(120, Vector3.one.normalized);
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(enuBody, CoordinateSystem.EastNorthUp), Quaternion.identity), Is.LessThan(0.05f));
            CoordinateSystem forwardLeftUp = new CoordinateSystem(Axis.NegativeY, Axis.PositiveZ, Axis.PositiveX);
            Quaternion fluBody = north * Quaternion.AngleAxis(180, Vector3.right);
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(fluBody, forwardLeftUp), Quaternion.identity), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToEarthCenteredRotation(Quaternion.identity, forwardLeftUp), fluBody), Is.LessThan(0.05f));

            frame.UnityRotation = Quaternion.Euler(10, 20, 30);
            Quaternion banked = north * Quaternion.AngleAxis(30, Vector3.right);
            Quaternion expected = frame.UnityRotation * Quaternion.AngleAxis(-30, Vector3.forward);
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(banked), expected), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToEarthCenteredRotation(expected), banked), Is.LessThan(0.05f));
            frame.SetEarthCenteredRotation(banked);
            frame.FollowRotation = true;
            Assert.That(frame.UsesEarthCenteredRotation, Is.True);
            Assert.That(Quaternion.Angle(frame.Rotation, banked), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(banked), frame.UnityRotation), Is.LessThan(0.05f));
            frame.GeographicPosition = new GeoPosition(0, 90, 0);
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(banked), frame.UnityRotation), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(frame.ToEarthCenteredRotation(frame.UnityRotation), banked), Is.LessThan(0.05f));
            frame.Rotation = Quaternion.identity;
            Assert.That(frame.UsesEarthCenteredRotation, Is.False);
        }

        /// <summary>Invalid ECEF body conventions fail without replacing a usable reference attitude.</summary>
        [Test]
        public void EarthCenteredAttitudes_RejectInvalidBodyAxesAndRequireGeographicSpace()
        {
            ReferenceFrame frame = new ReferenceFrame();
            Assert.Throws<InvalidOperationException>(() => frame.SetEarthCenteredRotation(Quaternion.identity));
            frame.Space = ReferenceSpace.Geographic;
            frame.GeographicPosition = new GeoPosition(0, 0, 0);
            Quaternion north = Quaternion.AngleAxis(-90, Vector3.up);
            frame.SetEarthCenteredRotation(north);
            Assert.Throws<ArgumentException>(() => frame.SetEarthCenteredRotation(Quaternion.identity, CoordinateSystem.Unity));
            Assert.Throws<ArgumentException>(() => frame.ToUnityEarthCenteredRotation(north, default(CoordinateSystem)));
            Assert.Throws<ArgumentException>(() => frame.ToEarthCenteredRotation(Quaternion.identity, CoordinateSystem.Unity));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetEarthCenteredRotation(new Quaternion(0, 0, float.NaN, 1)));
            Assert.That(frame.UsesEarthCenteredRotation, Is.True);
            Assert.That(Quaternion.Angle(frame.ToUnityEarthCenteredRotation(north), Quaternion.identity), Is.LessThan(0.05f));
        }

        /// <summary>Uninitialized and invalid geographic references cannot project entities or silently replace valid settings.</summary>
        [Test]
        public void GeographicValidation_WaitsForPositionAndRejectsInvalidConfiguration()
        {
            ReferenceFrame frame = new ReferenceFrame();
            Assert.Throws<InvalidOperationException>(() => frame.GeographicPosition = default);
            Assert.Throws<InvalidOperationException>(() => frame.TryToUnityPosition(new GeoPosition(), out Vector3 unused));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Space = (ReferenceSpace)42);
            frame.Space = ReferenceSpace.Geographic;
            Assert.That(frame.TryToUnityPosition(new GeoPosition(), out Vector3 waiting), Is.False);
            Assert.Throws<InvalidOperationException>(() => frame.ToGeographicPosition(Vector3.zero));
            Assert.Throws<InvalidOperationException>(() => frame.ToUnityRotation(Quaternion.identity, new GeoPosition()));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Position = default);
            frame.GeographicPosition = new GeoPosition(0, 0, 0);
            Assert.That(frame.TryToUnityPosition(default(Double3), out waiting), Is.False);
            Assert.That(frame.HasPosition, Is.True);
        }
    }
}
