namespace Emas
{
    /// <summary>
    /// Registers presence root and module initializers before a prefab realm starts its detectors.
    /// </summary>
    /// <remarks>
    /// Implement on an enabled MonoBehaviour beneath a RealmSetup. A nested RealmSetup uses its own configurators.
    /// The method runs once for each realm lifetime before any detector attaches.
    /// </remarks>
    public interface IRealmConfigurator
    {
        /// <summary>
        /// Adds per-kind presence initializers to the newly created realm.
        /// </summary>
        /// <param name="realm">The realm that is about to start its detectors.</param>
        void ConfigureRealm(Realm realm);
    }
}