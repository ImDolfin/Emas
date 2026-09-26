namespace Emas
{
    /// <summary>
    /// Creates one application-owned presence source for a prefab anchor when its realm starts.
    /// </summary>
    /// <remarks>
    /// Implement on a MonoBehaviour next to AnchorSetup. Return a new or detached source each time
    /// the realm starts. The source owns its attachment; SDK clients remain application-owned.
    /// </remarks>
    public interface ISourceProvider
    {
        /// <summary>
        /// Creates the source to attach to this anchor for the current realm lifetime.
        /// </summary>
        /// <returns>The source to attach.</returns>
        PresenceSource CreateSource();
    }
}
