using System.Collections.Generic;

namespace Emas
{
    // One tracked root's lifetime and presentation state; availability, spatial visibility and view requests are independent.
    internal sealed class Record
    {
        internal Record(Ghost ghost, PresenceDetector owner, ManifestationBlueprintSnapshot blueprint)
        {
            Key = ghost.Key;
            Ghost = ghost;
            Owner = owner;
            ManifestationBlueprint = blueprint;
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
        // Ownership changes invalidate trait work; attachment generations reject publications from old detector lifetimes.
        internal long OwnershipVersion;
        internal long RegistrationGeneration;
        // Handover waits for its earliest cleanup update and for work queued through the end of startup.
        internal long HandoverUpdate;
        internal long HandoverDispatchSequence;
        internal double LastPublishedAt;
    }
}
