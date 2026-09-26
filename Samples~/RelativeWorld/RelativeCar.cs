using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// A Ghost root whose WGS84 position and attitude are converted into the realm spatial layer.
    /// </summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class RelativeCar : Ghost
    {
        internal static readonly Kind Kind = new Kind("relative.car");
        internal static readonly Variant Origin = new Variant("origin");
        internal static readonly Variant Target = new Variant("target");
    }
}
