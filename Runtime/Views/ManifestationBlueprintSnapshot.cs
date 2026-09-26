using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Keeps one registration's manifestation settings stable until that scope registers again.
    /// </summary>
    internal sealed class ManifestationBlueprintSnapshot
    {
        internal readonly ManifestationBlueprint Asset;
        internal readonly Kind Kind;
        internal readonly Ghost GhostPrefab;
        private readonly ManifestationBlueprint.ViewMapping[] _views;
        private readonly GameObject _fallbackViewPrefab;

        internal ManifestationBlueprintSnapshot(ManifestationBlueprint asset, Kind kind, Ghost ghostPrefab,
            ManifestationBlueprint.ViewMapping[] views, GameObject fallbackViewPrefab)
        {
            Asset = asset;
            Kind = kind;
            GhostPrefab = ghostPrefab;
            _views = views;
            _fallbackViewPrefab = fallbackViewPrefab;
        }

        internal bool HasManifestationPrefab
        {
            get
            {
                return _views.Length > 0 || _fallbackViewPrefab != null;
            }
        }

        internal GameObject ResolveViewPrefab(Variant variant, DetailLevel detailLevel)
        {
            return ManifestationBlueprint.ResolveViewPrefab(_views, _fallbackViewPrefab, variant, detailLevel);
        }
    }
}
