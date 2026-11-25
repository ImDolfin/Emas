using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Emas
{
    /// <summary>Resolves root component contracts and reports ambiguous providers for one ghost.</summary>
    internal sealed class GhostPartResolver
    {
        private static class Contract<T> where T : class
        {
            internal static readonly Type Type = typeof(T);
            internal static readonly bool IsUniqueComponent = Type.IsSealed
                && typeof(MonoBehaviour).IsAssignableFrom(Type)
                && Attribute.IsDefined(Type, typeof(DisallowMultipleComponent), true);
        }

        private readonly List<MonoBehaviour> _components = new List<MonoBehaviour>();
        private HashSet<Type> _reportedAmbiguities;
        private Dictionary<Type, MonoBehaviour> _uniqueComponents;

        internal bool TryGet<T>(Ghost ghost, out T part) where T : class
        {
            Type contract = Contract<T>.Type;
            if (Contract<T>.IsUniqueComponent)
            {
                // Unity prevents duplicates of these concrete types. A live component
                // therefore stays the unique provider until it is destroyed.
                MonoBehaviour component = null;
                if (_uniqueComponents == null || !_uniqueComponents.TryGetValue(contract, out component)
                    || component == null)
                {
                    component = ghost.GetComponent(contract) as MonoBehaviour;
                    if (component != null)
                    {
                        if (_uniqueComponents == null)
                        {
                            _uniqueComponents = new Dictionary<Type, MonoBehaviour>();
                        }

                        _uniqueComponents[contract] = component;
                    }
                    else if (_uniqueComponents != null)
                    {
                        // Do not remember absence. Components can be added while
                        // roots are inactive, without any Emas lifecycle callback.
                        _uniqueComponents.Remove(contract);
                    }
                }

                part = component as T;
                return component != null;
            }

            part = null;
            // Interfaces and types that permit multiple providers still need a live
            // scan so additions, removals and ambiguity are observed immediately.
            ghost.GetComponents(_components);
            int count = 0;
            for (int index = 0; index < _components.Count; index++)
            {
                T candidate = _components[index] as T;
                if (candidate == null)
                {
                    continue;
                }

                part = candidate;
                count++;
            }

            if (count > 1)
            {
                // Do not choose between duplicate matches. Log the problem once until those matches change.
                part = null;
                if (_reportedAmbiguities == null)
                {
                    _reportedAmbiguities = new HashSet<Type>();
                }

                if (_reportedAmbiguities.Add(contract))
                {
                    Debug.LogError(DescribeAmbiguity<T>(ghost), ghost);
                }

                return false;
            }

            // Clear the old duplicate warning so a later duplicate can be logged again.
            if (_reportedAmbiguities != null)
            {
                _reportedAmbiguities.Remove(contract);
            }

            return count == 1;
        }

        internal void Reset()
        {
            _components.Clear();
            if (_uniqueComponents != null)
            {
                _uniqueComponents.Clear();
            }

            if (_reportedAmbiguities != null)
            {
                _reportedAmbiguities.Clear();
            }
        }

        private string DescribeAmbiguity<T>(Ghost ghost) where T : class
        {
            // Include instance IDs because several matching components can have the same type.
            StringBuilder message = new StringBuilder();
            message.Append("Ghost '").Append(ghost.Key).Append("' has multiple root providers for ")
                .Append(typeof(T).FullName).Append(": ");
            bool first = true;
            for (int index = 0; index < _components.Count; index++)
            {
                MonoBehaviour component = _components[index];
                if (!(component is T))
                {
                    continue;
                }

                if (!first)
                {
                    message.Append(", ");
                }

                message.Append(component.GetType().FullName).Append(" (instance ")
                    .Append(component.GetInstanceID()).Append(")");
                first = false;
            }

            message.Append(". Keep exactly one provider for this contract on the ghost root.");
            return message.ToString();
        }
    }
}
