using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// A car whose independent position and orientation publications use the realm spatial layer.
    /// </summary>
    [RequireComponent(typeof(Spatial))]
    public sealed class RelativeCar : Ghost
    {
        internal static readonly Kind Kind = new Kind("relative.car");
        internal static readonly Variant Ego = new Variant("ego");
        internal static readonly Variant Traffic = new Variant("traffic");
    }
}
