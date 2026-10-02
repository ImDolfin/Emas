using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Provides convenience methods for reading ghost contracts.
    /// </summary>
    public static class GhostExtensions
    {
        /// <summary>
        /// Gets a required root component contract or fails with the Ghost identity and component diagnostics.
        /// </summary>
        /// <typeparam name="T">
        /// The required interface or root component class.
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
        /// The Ghost does not have exactly one matching root MonoBehaviour. For a Unity Ghost, the message
        /// distinguishes missing and duplicate matches and identifies the GameObject and component types.
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

            throw new InvalidOperationException(DescribeFailure<T>(ghost));
        }

        private static string DescribeFailure<T>(IGhost ghost) where T : class
        {
            string prefix = "Ghost '" + ghost.Key + "' cannot resolve required root component " + DescribeType(typeof(T));
            Ghost root = ghost as Ghost;
            if (root == null)
            {
                return prefix + ". TryGet did not resolve a single matching root component.";
            }

            List<string> matches = new List<string>();
            List<string> components = new List<string>();
            foreach (MonoBehaviour component in root.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                {
                    continue;
                }

                string description = DescribeType(component.GetType()) + " (instance " + component.GetInstanceID() + ")";
                components.Add(description);
                if (component is T)
                {
                    matches.Add(description);
                }
            }

            string context = prefix + ": found " + matches.Count + " matching root components on GameObject '" + root.name + "'. ";
            if (matches.Count == 0)
            {
                return context + "Check the Manifestation Blueprint for Kind '" + ghost.Key.Kind.Id
                    + "' registered in the owning Realm (Realm Setup > Manifestation Blueprints). Assign its Ghost Prefab "
                    + "field to a prefab containing the required component on the Ghost root. If the blueprint is missing "
                    + "or its Ghost Prefab is unassigned, no prefab modules are instantiated. A Ghost subclass can also "
                    + "require the component through RequireComponent. Components on the Anchor, parents, children or views "
                    + "are not searched. Attached root MonoBehaviours: " + string.Join(", ", components) + ".";
            }

            if (matches.Count > 1)
            {
                return context + "Matching components: " + string.Join(", ", matches)
                    + ". Keep exactly one matching component on this Ghost root; disabled components also count.";
            }

            return context + "TryGet did not resolve the matching root component.";
        }

        private static string DescribeType(Type type)
        {
            return type.FullName + " [" + type.Assembly.GetName().Name + "]";
        }
    }
}
