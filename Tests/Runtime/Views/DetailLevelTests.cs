using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Verifies detail values, equality, ordering and display names.
    /// </summary>
    public sealed class DetailLevelTests
    {
        /// <summary>
        /// Presets and application-defined detail levels retain their numeric values.
        /// </summary>
        [Test]
        public void Values_PreservePresetAndCustomLevels()
        {
            Assert.That(DetailLevel.None.Level, Is.EqualTo(0));
            Assert.That(DetailLevel.Minimal.Level, Is.EqualTo(1));
            Assert.That(DetailLevel.Reduced.Level, Is.EqualTo(2));
            Assert.That(DetailLevel.Full.Level, Is.EqualTo(3));
            Assert.That(new DetailLevel(7).Level, Is.EqualTo(7));
        }

        /// <summary>
        /// Equality and hashing depend only on the numeric level, including preset values.
        /// </summary>
        [Test]
        public void Equality_AndHashingUseLevel()
        {
            DetailLevel custom = new DetailLevel(3);
            Assert.That(custom, Is.EqualTo(DetailLevel.Full));
            Assert.That(custom == DetailLevel.Full, Is.True);
            Assert.That(custom != DetailLevel.Full, Is.False);
            Assert.That(custom.GetHashCode(), Is.EqualTo(DetailLevel.Full.GetHashCode()));
            Assert.That(custom, Is.Not.EqualTo(DetailLevel.Minimal));
            Assert.That(custom == DetailLevel.Minimal, Is.False);
            Assert.That(custom != DetailLevel.Minimal, Is.True);
        }

        /// <summary>
        /// Comparison methods and operators consistently order lower, equal and higher levels.
        /// </summary>
        [Test]
        public void Comparison_OrdersByLevel()
        {
            Assert.That(DetailLevel.None < DetailLevel.Minimal, Is.True);
            Assert.That(DetailLevel.Minimal < DetailLevel.Reduced, Is.True);
            Assert.That(DetailLevel.Reduced < DetailLevel.Full, Is.True);
            Assert.That(DetailLevel.Full > DetailLevel.None, Is.True);
            DetailLevel same = new DetailLevel(2);
            Assert.That(same <= DetailLevel.Reduced, Is.True);
            Assert.That(same >= DetailLevel.Reduced, Is.True);
            Assert.That(same <= DetailLevel.Full, Is.True);
            Assert.That(DetailLevel.Full >= same, Is.True);
            Assert.That(DetailLevel.None.CompareTo(DetailLevel.Full), Is.LessThan(0));
            Assert.That(DetailLevel.Full.CompareTo(DetailLevel.None), Is.GreaterThan(0));
            Assert.That(same.CompareTo(DetailLevel.Reduced), Is.Zero);
        }

        /// <summary>
        /// Presets use readable names while custom levels expose their numeric value.
        /// </summary>
        [Test]
        public void Formatting_DescribesPresetAndCustomLevels()
        {
            Assert.That(DetailLevel.None.ToString(), Does.Contain("None"));
            Assert.That(DetailLevel.Minimal.ToString(), Does.Contain("Minimal"));
            Assert.That(DetailLevel.Reduced.ToString(), Does.Contain("Reduced"));
            Assert.That(DetailLevel.Full.ToString(), Does.Contain("Full"));
            Assert.That(new DetailLevel(7).ToString(), Does.Contain("7"));
        }
    }
}
