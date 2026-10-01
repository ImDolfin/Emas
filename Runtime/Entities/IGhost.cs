using System;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Provides stable identity and root-component access for one ghost.
    /// </summary>
    /// <remarks>
        /// Read on the Unity thread. Emas owns identity and availability; configured modules update application data.
    /// Consumers use read-only application interfaces and must not assume retained data is current while IsAvailable is false.
    /// </remarks>
    public interface IGhost
    {
        /// <summary>
        /// Gets the stable ghost key.
        /// </summary>
        /// <value>
        /// The anchor, kind and entity identity.
        /// </value>
        Key Key
        {
            get;
        }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        /// <value>
        /// The source display name used by name filters.
        /// </value>
        string Name
        {
            get;
        }

        /// <summary>
        /// Gets the selected visual variant.
        /// </summary>
        /// <value>
        /// The appearance, or <see cref="Variant.None"/> if unspecified.
        /// </value>
        Variant Variant
        {
            get;
        }

        /// <summary>
        /// Gets whether the ghost currently has initialized source data.
        /// </summary>
        /// <value>
        /// True when source data is initialized and queryable.
        /// </value>
        bool IsAvailable
        {
            get;
        }

        /// <summary>
        /// Gets the single root MonoBehaviour assignable to the requested contract type.
        /// </summary>
        /// <typeparam name="T">
        /// The requested interface or component class.
        /// </typeparam>
        /// <param name="part">
        /// Receives the matching component, when found.
        /// </param>
        /// <returns>
        /// True when exactly one matching component exists.
        /// </returns>
        /// <remarks>
        /// Includes disabled root MonoBehaviours, including the Ghost itself; child and view components do not participate.
        /// No match returns false with a null result. Multiple matches also return false with a null result and log a
        /// contextual error once until a lookup observes a non-ambiguous result. No component is added.
        /// </remarks>
        bool TryGet<T>(out T part) where T : class;
    }
}
