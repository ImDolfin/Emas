namespace Emas
{
    /// <summary>
    /// Registers Ghost root types and trait input bindings before a prefab realm starts its detectors.
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
        /// <remarks>
        /// Runs on Unity's main thread once for this component in each realm lifetime, before its detectors start.
        /// An exception aborts that startup attempt; the setup releases partially created tracking state.
        /// </remarks>
        void ConfigureRealm(Realm realm);
    }
}
