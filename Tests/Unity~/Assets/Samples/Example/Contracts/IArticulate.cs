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
        /// <value>The current steering input; zero represents straight steering.</value>
        float Steering
        {
            get;
        }
    }
}
