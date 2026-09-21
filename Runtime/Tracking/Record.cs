using System.Collections.Generic;

namespace Emas
{
    internal sealed class Record
    {
        internal Record(Ghost ghost, Coordinator owner, Blueprint blueprint)
        {
            Key = ghost.Key;
            Ghost = ghost;
            Owner = owner;
            Blueprint = blueprint;
            RequestedDegree = DetailLevel.Full;
            RegistrationGeneration = owner == null ? 0 : owner.RegistrationGeneration;
        }

        internal readonly Key Key;
        internal readonly Ghost Ghost;
        internal Coordinator Owner;
        internal Blueprint Blueprint;
        internal View View;
        internal UnityEngine.GameObject ViewPrefab;
        internal bool ViewRequested;
        internal bool PendingActivation;
        internal bool ViewDirty;
        internal bool RefreshingView;
        internal long ViewVersion;
        internal long OwnershipVersion;
        internal long RegistrationGeneration;
        internal DetailLevel RequestedDegree;
    }
}
