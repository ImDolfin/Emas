using NUnit.Framework;

namespace Emas.Tests
{
    /// <summary>
    /// Tests package metadata.
    /// </summary>
    public class PackageTests
    {
        /// <summary>
        /// Runs the Version_IsNotNullOrEmpty test.
        /// </summary>
        [Test]
        public void Version_IsNotNullOrEmpty()
        {
            Assert.That(Package.Version, Is.Not.Null.And.Not.Empty);
        }
    }
}
