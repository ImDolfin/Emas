namespace Emas
{
    /// <summary>
    /// Creates one application-owned presence detector for a prefab anchor when its realm starts.
    /// </summary>
    /// <remarks>
    /// Implement on a MonoBehaviour next to AnchorSetup. Return a new or detached detector each time
    /// the realm starts. The detector owns its attachment; SDK clients remain application-owned.
    /// </remarks>
    public interface IDetectorProvider
    {
        /// <summary>
        /// Creates the detector to attach to this anchor for the current realm lifetime.
        /// </summary>
        /// <returns>The detector to attach.</returns>
        PresenceDetector CreateDetector();
    }
}
