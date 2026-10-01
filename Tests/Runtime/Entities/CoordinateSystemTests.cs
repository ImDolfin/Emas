using System;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies invalid source axes cannot replace a usable coordinate convention.
    /// </summary>
    public sealed class CoordinateSystemTests
    {
        /// <summary>
        /// Invalid axes cannot replace a frame's usable convention or define a custom coordinate system.
        /// </summary>
        [Test]
        public void InvalidCoordinates_LeaveFrameConfigurationUsable()
        {
            Assert.Throws<ArgumentException>(() =>
                new CoordinateSystem(Axis.PositiveX, Axis.NegativeX, Axis.PositiveZ));
            Assert.Throws<ArgumentException>(() =>
                new CoordinateSystem((Axis)42, Axis.PositiveY, Axis.PositiveZ));
            ReferenceFrame frame = new ReferenceFrame
            {
                Position = default,
                Coordinates = CoordinateSystem.NorthEastDown
            };
            Assert.Throws<ArgumentException>(() => frame.Coordinates = default);

            Assert.That(frame.TryToUnityPosition(new Double3(2, 3, 5), out Vector3 position), Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(3, -5, 2)));
        }
    }
}
