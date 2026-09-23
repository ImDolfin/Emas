using System;

namespace Emas
{
    /// <summary>
    /// Provides convenience methods for reading ghost contracts.
    /// </summary>
    public static class GhostExtensions
    {
        /// <summary>
        /// Gets a required root component contract or fails with the ghost identity.
        /// </summary>
        /// <typeparam name="T">
        /// The required interface type.
        /// </typeparam>
        /// <param name="ghost">
        /// The ghost to read.
        /// </param>
        /// <returns>
        /// The single matching root component.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The ghost is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The ghost does not have exactly one matching root component.
        /// </exception>
        public static T GetRequired<T>(this IGhost ghost) where T : class
        {
            if (ghost == null)
            {
                throw new ArgumentNullException(nameof(ghost));
            }

            T part;
            if (ghost.TryGet<T>(out part))
            {
                return part;
            }

            throw new InvalidOperationException("Ghost '" + ghost.Key + "' requires exactly one root provider for "
                + typeof(T).FullName + ". Add the missing component or remove duplicate providers.");
        }
    }
}
