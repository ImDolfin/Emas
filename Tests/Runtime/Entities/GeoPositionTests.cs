using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Verifies WGS84 input, Earth-centered storage and geographic recovery.</summary>
    public sealed class GeoPositionTests
    {
        /// <summary>Earth-centered axes and ellipsoidal height agree with equatorial and polar WGS84 landmarks.</summary>
        [Test]
        public void EarthCenteredConversion_MapsKnownLocationsAndRecoversGlobalPositions()
        {
            Assert.That(Double3.Distance(new GeoPosition(0, 0, 10).ToEarthCentered(), new Double3(6378147, 0, 0)), Is.LessThan(1e-8));
            Assert.That(Double3.Distance(new GeoPosition(0, 90, 0).ToEarthCentered(), new Double3(0, 6378137, 0)), Is.LessThan(1e-8));
            Assert.That(Double3.Distance(new GeoPosition(90, 0, 0).ToEarthCentered(), new Double3(0, 0, 6356752.314245179)), Is.LessThan(1e-8));
            GeoPosition pole = GeoPosition.FromEarthCentered(new Double3(0, 0, -6356762.314245179));
            Assert.That(pole.LatitudeDegrees, Is.EqualTo(-90));
            Assert.That(pole.LongitudeDegrees, Is.Zero);
            Assert.That(pole.HeightMeters, Is.EqualTo(10).Within(1e-8));

            foreach (GeoPosition position in new[]
            {
                new GeoPosition(52.520008, 13.404954, 40.125),
                new GeoPosition(-33.9, -179.9999, -400),
                new GeoPosition(89.99999, 179.9999, 12000),
                new GeoPosition(0, -180, 35786000)
            })
            {
                GeoPosition recovered = GeoPosition.FromEarthCentered(position.ToEarthCentered());
                Assert.That(recovered.LatitudeDegrees, Is.EqualTo(position.LatitudeDegrees).Within(1e-10));
                Assert.That(recovered.LongitudeDegrees, Is.EqualTo(position.LongitudeDegrees).Within(1e-10));
                Assert.That(recovered.HeightMeters, Is.EqualTo(position.HeightMeters).Within(1e-7));
            }
        }

        /// <summary>Serialized geographic values retain their units and format independently of the application's culture.</summary>
        [Test]
        public void Serialization_RetainsGeographicCoordinates()
        {
            GeoPosition source = new GeoPosition(52.5, 13.25, 40.125);
            GeoPosition copy = JsonUtility.FromJson<GeoPosition>(JsonUtility.ToJson(source));
            Assert.That(Double3.Distance(source.ToEarthCentered(), copy.ToEarthCentered()), Is.LessThan(1e-9));
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(copy.ToString(), Is.EqualTo("(52.5 deg, 13.25 deg, 40.125 m)"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>Invalid WGS84 readings and undefined Earth-center conversions fail before they can enter projection.</summary>
        [Test]
        public void InvalidReadings_AreRejectedIncludingSerializedInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(91, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(0, -181, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(0, 0, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoPosition.FromEarthCentered(default));
            GeoPosition invalid = JsonUtility.FromJson<GeoPosition>("{\"_latitudeDegrees\":100}");
            Assert.Throws<ArgumentOutOfRangeException>(() => invalid.ToEarthCentered());
        }
    }
}
