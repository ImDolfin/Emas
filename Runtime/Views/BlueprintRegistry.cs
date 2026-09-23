using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Tracks blueprint registrations by kind and removes old keys when an asset changes kind.
    /// </summary>
    internal sealed class BlueprintRegistry
    {
        private readonly Dictionary<string, Blueprint> _byKind = new Dictionary<string, Blueprint>(StringComparer.Ordinal);

        internal List<Kind> Register(Blueprint blueprint)
        {
            string kindId = blueprint.Kind.Id;
            List<Kind> staleKinds = null;
            foreach (KeyValuePair<string, Blueprint> entry in _byKind)
            {
                if (!ReferenceEquals(entry.Value, blueprint) || string.Equals(entry.Key, kindId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (staleKinds == null)
                {
                    staleKinds = new List<Kind>();
                }

                staleKinds.Add(new Kind(entry.Key));
            }

            if (staleKinds != null)
            {
                for (int index = 0; index < staleKinds.Count; index++)
                {
                    _byKind.Remove(staleKinds[index].Id);
                }
            }

            _byKind[kindId] = blueprint;
            return staleKinds;
        }

        internal bool Remove(Kind kind)
        {
            return _byKind.Remove(kind.Id);
        }

        internal bool TryGet(string kindId, out Blueprint blueprint)
        {
            if (!_byKind.TryGetValue(kindId, out blueprint) || blueprint == null
                || !string.Equals(blueprint.Kind.Id, kindId, StringComparison.Ordinal))
            {
                blueprint = null;
                return false;
            }

            return true;
        }

        internal void Clear()
        {
            _byKind.Clear();
        }
    }
}
