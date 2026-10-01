namespace Emas
{
    /// <summary>
    /// Supplies an application-owned presence detector whenever a prefab anchor starts.
    /// </summary>
    /// <remarks>
    /// Implement on a MonoBehaviour next to AnchorSetup. Return a new or detached detector each time
    /// the anchor starts. Emas starts and stops the detector's attachment; SDK clients remain application-owned.
    /// </remarks>
    public interface IDetectorProvider
    {
        /// <summary>
        /// Returns a new or detached detector for this anchor's next attachment.
        /// </summary>
        /// <returns>A non-null detector that is not currently attached to an anchor.</returns>
        PresenceDetector CreateDetector();
    }
}
