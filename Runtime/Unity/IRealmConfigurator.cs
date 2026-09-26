namespace Emas
{
    /// <summary>
    /// Registers presence root and module initializers before a prefab realm starts its detectors.
    /// </summary>
    /// <remarks>
    /// Implement on an enabled MonoBehaviour beneath a RealmSetup. A nested RealmSetup uses its own configurators.
    /// Each active component runs once per realm lifetime before detectors attach. Newly enabled
    /// configurators run before a later-enabled anchor starts its detector.
    /// </remarks>
    public interface IRealmConfigurator
    {
        /// <summary>
        /// Adds per-kind presence initializers to the owning realm before detection.
        /// </summary>
        /// <param name="realm">The realm that is about to attach detectors.</param>
        void ConfigureRealm(Realm realm);
    }
}