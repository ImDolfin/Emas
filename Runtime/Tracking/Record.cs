using System.Collections.Generic;

namespace Emas
{
    internal sealed class Record
    {
        internal Record(Ghost ghost, PresenceSource owner, BlueprintSnapshot blueprint)
        {
            Key = ghost.Key;
            Ghost = ghost;
            Owner = owner;
            Blueprint = blueprint;
            RequestedDetailLevel = DetailLevel.Full;
            RegistrationGeneration = owner == null ? 0 : owner.RegistrationGeneration;
        }

        internal readonly Key Key;
        internal readonly Ghost Ghost;
        internal PresenceSource Owner;
        internal BlueprintSnapshot Blueprint;
        internal View View;
        internal UnityEngine.GameObject ViewPrefab;
        internal bool ViewRequested;
        internal bool PendingActivation;
        internal bool ViewDirty;
        internal bool RefreshingView;
        internal long ViewVersion;
        internal long OwnershipVersion;
        internal long RegistrationGeneration;
        internal long HandoverUpdate;
        internal long HandoverDispatchSequence;
        internal double LastPublishedAt;
        internal DetailLevel RequestedDetailLevel;
    }
}
