using System.Collections.Generic;

namespace Emas
{
    // State for one root. Availability, visibility and requests for a view can change separately.
    internal sealed class Record
    {
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
        // Owner changes stop old trait work. Source versions reject reports queued before a restart.
        internal long OwnershipVersion;
        internal long RegistrationGeneration;
        // Keep old roots until the next cleanup update and the queued startup work have both finished.
        internal long HandoverUpdate;
        internal long HandoverDispatchSequence;
        internal double LastPublishedAt;

        internal Record(Ghost ghost, PresenceDetector owner, ManifestationBlueprintSnapshot blueprint)
        {
            Key = ghost.Key;
            Ghost = ghost;
            Owner = owner;
            ManifestationBlueprint = blueprint;
            RegistrationGeneration = owner == null ? 0 : owner.RegistrationGeneration;
        }
    }
}
