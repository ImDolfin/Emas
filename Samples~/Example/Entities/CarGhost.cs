using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Defines the reusable modules carried by a sample entity.</summary>
    [RequireComponent(typeof(PositionModule), typeof(ArticulationModule))]
    public sealed class CarGhost : Ghost
    {
    }
}
