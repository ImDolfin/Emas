using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Provides source-independent articulation data for a car.</summary>

    public interface IArticulate
    {
        /// <summary>Gets the current steering value.</summary>
        /// <value>The source-independent steering value.</value>
        float Steering { get; }

        /// <summary>Sets the current steering value.</summary>
        /// <param name="steering">The new steering value.</param>
        void SetArticulation(float steering);
    }
}
