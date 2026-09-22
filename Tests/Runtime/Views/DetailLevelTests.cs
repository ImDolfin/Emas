using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Tests detail level values and comparisons.
    /// </summary>
    public class DetailLevelTests
    {
        /// <summary>
        /// Runs the Presets_HaveExpectedLevels test.
        /// </summary>
        [Test]
        public void Presets_HaveExpectedLevels()
        {
            Assert.That(DetailLevel.None.Level, Is.EqualTo(0));
            Assert.That(DetailLevel.Minimal.Level, Is.EqualTo(1));
            Assert.That(DetailLevel.Reduced.Level, Is.EqualTo(2));
            Assert.That(DetailLevel.Full.Level, Is.EqualTo(3));
        }

        /// <summary>
        /// Runs the CustomDetailLevel_PreservesLevel test.
        /// </summary>
        [Test]
        public void CustomDetailLevel_PreservesLevel()
        {
            DetailLevel detailLevel = new DetailLevel(7);
            Assert.That(detailLevel.Level, Is.EqualTo(7));
        }

        /// <summary>
        /// Runs the Equality_SameLevel_AreEqual test.
        /// </summary>
        [Test]
        public void Equality_SameLevel_AreEqual()
        {
            DetailLevel a = new DetailLevel(2);
            DetailLevel b = new DetailLevel(2);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
        }

        /// <summary>
        /// Runs the Equality_DifferentLevel_AreNotEqual test.
        /// </summary>
        [Test]
        public void Equality_DifferentLevel_AreNotEqual()
        {
            DetailLevel a = DetailLevel.Minimal;
            DetailLevel b = DetailLevel.Full;

            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        }

        /// <summary>
        /// Runs the Equality_MatchesPreset test.
        /// </summary>
        [Test]
        public void Equality_MatchesPreset()
        {
            DetailLevel custom = new DetailLevel(3);
            Assert.That(custom, Is.EqualTo(DetailLevel.Full));
        }

        /// <summary>
        /// Runs the Comparison_OrdersByLevel test.
        /// </summary>
        [Test]
        public void Comparison_OrdersByLevel()
        {
            Assert.That(DetailLevel.None < DetailLevel.Minimal, Is.True);
            Assert.That(DetailLevel.Minimal < DetailLevel.Reduced, Is.True);
            Assert.That(DetailLevel.Reduced < DetailLevel.Full, Is.True);
            Assert.That(DetailLevel.Full > DetailLevel.None, Is.True);
        }

        /// <summary>
        /// Runs the Comparison_LessOrEqual_GreaterOrEqual test.
        /// </summary>
        [Test]
        public void Comparison_LessOrEqual_GreaterOrEqual()
        {
            DetailLevel a = new DetailLevel(2);
            DetailLevel b = new DetailLevel(2);
            DetailLevel c = new DetailLevel(3);

            Assert.That(a <= b, Is.True);
            Assert.That(a >= b, Is.True);
            Assert.That(a <= c, Is.True);
            Assert.That(c >= a, Is.True);
        }

        /// <summary>
        /// Runs the CompareTo_ReturnsCorrectOrdering test.
        /// </summary>
        [Test]
        public void CompareTo_ReturnsCorrectOrdering()
        {
            Assert.That(DetailLevel.None.CompareTo(DetailLevel.Full), Is.LessThan(0));
            Assert.That(DetailLevel.Full.CompareTo(DetailLevel.None), Is.GreaterThan(0));
            Assert.That(DetailLevel.Minimal.CompareTo(DetailLevel.Minimal), Is.EqualTo(0));
        }

        /// <summary>
        /// Runs the GetHashCode_SameLevelSameHash test.
        /// </summary>
        [Test]
        public void GetHashCode_SameLevelSameHash()
        {
            DetailLevel a = new DetailLevel(5);
            DetailLevel b = new DetailLevel(5);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        /// <summary>
        /// Runs the ToString_PresetsShowNames test.
        /// </summary>
        [Test]
        public void ToString_PresetsShowNames()
        {
            Assert.That(DetailLevel.None.ToString(), Does.Contain("None"));
            Assert.That(DetailLevel.Minimal.ToString(), Does.Contain("Minimal"));
            Assert.That(DetailLevel.Reduced.ToString(), Does.Contain("Reduced"));
            Assert.That(DetailLevel.Full.ToString(), Does.Contain("Full"));
        }

        /// <summary>
        /// Runs the ToString_CustomShowsLevel test.
        /// </summary>
        [Test]
        public void ToString_CustomShowsLevel()
        {
            DetailLevel detailLevel = new DetailLevel(7);
            Assert.That(detailLevel.ToString(), Does.Contain("7"));
        }
    }
}
