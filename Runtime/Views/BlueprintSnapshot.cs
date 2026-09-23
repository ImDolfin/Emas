using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Keeps one registration's blueprint settings stable until that scope registers again.
    /// </summary>
    internal sealed class BlueprintSnapshot
    {
        internal readonly Blueprint Asset;
        internal readonly Kind Kind;
        internal readonly Ghost GhostPrefab;
        private readonly Blueprint.ViewMapping[] _views;
        private readonly GameObject _fallbackViewPrefab;

        internal BlueprintSnapshot(Blueprint asset, Kind kind, Ghost ghostPrefab,
            Blueprint.ViewMapping[] views, GameObject fallbackViewPrefab)
        {
            Asset = asset;
            Kind = kind;
            GhostPrefab = ghostPrefab;
            _views = views;
            _fallbackViewPrefab = fallbackViewPrefab;
        }

        internal GameObject ResolveViewPrefab(Variant variant, DetailLevel detailLevel)
        {
            return Blueprint.ResolveViewPrefab(_views, _fallbackViewPrefab, variant, detailLevel);
        }
    }
}
