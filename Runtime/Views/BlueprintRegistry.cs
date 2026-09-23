using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Tracks snapshots by kind and removes old keys when an asset is re-registered under another kind.
    /// </summary>
    internal sealed class BlueprintRegistry
    {
        private readonly Dictionary<string, BlueprintSnapshot> _byKind = new Dictionary<string, BlueprintSnapshot>(StringComparer.Ordinal);

        internal List<Kind> Register(Blueprint blueprint)
        {
            BlueprintSnapshot snapshot = blueprint.CaptureSnapshot();
            string kindId = snapshot.Kind.Id;
            List<Kind> staleKinds = null;
            foreach (KeyValuePair<string, BlueprintSnapshot> entry in _byKind)
            {
                if (!ReferenceEquals(entry.Value.Asset, blueprint) || string.Equals(entry.Key, kindId, StringComparison.Ordinal))
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

            _byKind[kindId] = snapshot;
            return staleKinds;
        }

        internal bool Remove(Kind kind)
        {
            return _byKind.Remove(kind.Id);
        }

        internal bool TryGet(string kindId, out BlueprintSnapshot blueprint)
        {
            if (!_byKind.TryGetValue(kindId, out blueprint) || blueprint.Asset == null)
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
