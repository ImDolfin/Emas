namespace Emas.Sample
{
    /// <summary>
    /// Application-owned, read-only articulation data for a car.
    /// </summary>
    public interface IArticulate
    {
        /// <summary>
        /// Gets the normalized steering value, from -1 to 1.
        /// </summary>
        float Steering
        {
            get;
        }
    }
}
