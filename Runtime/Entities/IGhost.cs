using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Provides stable identity and root-component access for one ghost.</summary>
    public interface IGhost
    {
        /// <summary>Gets the stable ghost key.</summary>
        /// <value>The anchor, kind and entity identity.</value>
        Key Key { get; }

        /// <summary>Gets the display name.</summary>
        /// <value>The source display name used by name filters.</value>
        string Name { get; }

        /// <summary>Gets the selected visual variant.</summary>
        /// <value>The appearance, or <see cref="Variant.None"/> if unspecified.</value>
        Variant Variant { get; }

        /// <summary>Gets whether the ghost currently has initialized source data.</summary>
        /// <value>True when source data is initialized and queryable.</value>
        bool IsAvailable { get; }

        /// <summary>Gets a root component implementing the requested interface.</summary>
        /// <typeparam name="T">The requested interface type.</typeparam>
        /// <param name="part">Receives the matching component, when found.</param>
        /// <returns>True when exactly one matching component exists.</returns>
        bool TryGet<T>(out T part) where T : class;
    }
}
