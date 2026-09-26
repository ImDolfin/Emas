using System.Collections.Generic;

namespace Emas
{
    internal sealed class Record
    {
        internal Record(Ghost ghost, PresenceDetector owner, ManifestationBlueprintSnapshot blueprint)
        {
            Key = ghost.Key;
            Ghost = ghost;
            Owner = owner;
            ManifestationBlueprint = blueprint;
            RequestedDetailLevel = DetailLevel.Full;
            RegistrationGeneration = owner == null ? 0 : owner.RegistrationGeneration;
        }

        internal readonly Key Key;
        internal readonly Ghost Ghost;
        internal Presence Presence;
        internal PresenceDetector Owner;
        internal ManifestationBlueprintSnapshot ManifestationBlueprint;
        internal View View;
        internal UnityEngine.GameObject ViewPrefab;
        internal bool SpatialVisible = true;
        internal bool ViewRequested;
        internal bool PresenceInitialized;
        internal bool IsMissing;
        internal double MissingUntil;
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
