using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Owns tracked entity creation, initialization, ownership and availability transitions.</summary>
    internal sealed class Population
    {
        private interface IPresenceInitializer
        {
            /// <summary>Resolves or creates the root type selected by this registered initializer.</summary>
            /// <param name="population">The population that owns identity lookup and root creation.</param>
            /// <param name="detector">The source claiming the identity.</param>
            /// <param name="key">The full anchor, kind and entity identity.</param>
            /// <param name="variant">The requested appearance, or null to retain the existing value.</param>
            /// <param name="name">The display name, or null to retain the existing value.</param>
            /// <param name="anchor">The owning anchor and scene parent for a newly created root.</param>
            /// <param name="blueprint">The captured root and view configuration, or null when none is registered.</param>
            /// <returns>The compatible root currently assigned to this identity.</returns>
            Ghost GetOrCreate(Population population, PresenceDetector detector, Key key, Variant? variant,
                string name, Anchor anchor, ManifestationBlueprintSnapshot blueprint);

            /// <summary>Binds the configured root's traits to the detected source.</summary>
            /// <param name="presence">The presence whose root and weak source are ready for binding.</param>
            void Initialize(Presence presence);
        }

        private sealed class PresenceInitializer<TGhost> : IPresenceInitializer where TGhost : Ghost
        {
            private readonly Action<Presence, TGhost> _initialize;

            internal PresenceInitializer(Action<Presence, TGhost> initialize)
            {
                _initialize = initialize;
            }

            /// <summary>Resolves the root type selected by this initializer.</summary>
            /// <param name="population">The population that owns identity lookup and root creation.</param>
            /// <param name="detector">The source claiming the identity.</param>
            /// <param name="key">The full anchor, kind and entity identity.</param>
            /// <param name="variant">The requested appearance, or null to retain the existing value.</param>
            /// <param name="name">The display name, or null to retain the existing value.</param>
            /// <param name="anchor">The owning anchor and scene parent for a newly created root.</param>
            /// <param name="blueprint">The captured root and view configuration, or null when none is registered.</param>
            /// <returns>The compatible root currently assigned to this identity.</returns>
            public Ghost GetOrCreate(Population population, PresenceDetector detector, Key key, Variant? variant,
                string name, Anchor anchor, ManifestationBlueprintSnapshot blueprint)
            {
                return population.GetOrCreate<TGhost>(detector, key.AnchorId, key.EntityId, key.Kind, variant,
                    name, anchor, blueprint);
            }

            /// <summary>Applies the registered trait setup to this presence.</summary>
            /// <param name="presence">The presence whose root is of the registered Ghost type.</param>
            public void Initialize(Presence presence)
            {
                _initialize(presence, (TGhost)presence.Root);
            }
        }

        private readonly Realm _realm;
        private readonly IdentityMap _identities;
        private readonly Func<double> _elapsedSeconds;
        private readonly ViewManager _views;
        private readonly Subscriptions _subscriptions;
        private readonly SceneChangeQueue _sceneChanges;
        private readonly Dictionary<string, IPresenceInitializer> _presenceInitializers =
            new Dictionary<string, IPresenceInitializer>(StringComparer.Ordinal);

        internal Population(Realm realm, IdentityMap identities, Func<double> elapsedSeconds,
            ViewManager views, Subscriptions subscriptions, SceneChangeQueue sceneChanges)
        {
            _realm = realm;
            _identities = identities;
            _elapsedSeconds = elapsedSeconds ?? throw new ArgumentNullException(nameof(elapsedSeconds));
            _views = views;
            _subscriptions = subscriptions;
            _sceneChanges = sceneChanges;
        }

        internal void RegisterPresenceInitializer<TGhost>(Kind kind, Action<Presence, TGhost> initialize)
            where TGhost : Ghost
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("A presence initializer requires a valid kind.", nameof(kind));
            }

            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            // Choose setup rules before creating roots so every root of this Kind follows the same rules.
            foreach (Record record in _identities.Values)
            {
                if (record.Key.Kind == kind)
                {
                    throw new InvalidOperationException("Register a presence initializer before creating ghosts of its kind.");
                }
            }

            _presenceInitializers[kind.Id] = new PresenceInitializer<TGhost>(initialize);
        }

        internal bool TryGetPresence(Key key, out Presence presence)
        {
            presence = null;
            if (_realm.IsDisposed)
            {
                return false;
            }

            Record record;
            if (!_identities.TryGetValue(key, out record) || record.Ghost == null || record.Presence == null)
            {
                return false;
            }

            presence = EnsurePresence(record);
            return true;
        }

        internal IGhost FindPresenceRoot(Presence presence)
        {
            if (presence == null || !ReferenceEquals(presence.Realm, _realm) || presence.IsRemoved)
            {
                return null;
            }

            Record record = _identities.Find(presence.Root);
            return record != null && ReferenceEquals(record.Presence, presence) ? record.Ghost : null;
        }

        internal IReadOnlyList<Presence> GetOwnedPresences(PresenceDetector detector)
        {
            List<Presence> result = new List<Presence>();
            foreach (Record record in _identities.Values)
            {
                if (record.Owner == detector && record.Presence != null)
                {
                    result.Add(EnsurePresence(record));
                }
            }

            return result.AsReadOnly();
        }

        internal IReadOnlyList<IGhost> GetOwnedGhosts(PresenceDetector owner)
        {
            List<IGhost> result = new List<IGhost>();
            foreach (Record record in _identities.Values)
            {
                if (record.Owner == owner)
                {
                    result.Add(record.Ghost);
                }
            }

            return result;
        }

        internal TGhost GetOrCreate<TGhost>(
            PresenceDetector owner,
            string anchorId,
            string entityId,
            Kind kind,
            Variant? variant,
            string nameValue,
            Anchor anchor,
            ManifestationBlueprintSnapshot blueprint) where TGhost : Ghost
        {
            if (_realm.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(Realm));
            }

            if (owner != null && !owner.IsActive)
            {
                throw new InvalidOperationException("A stopped source cannot publish ghosts.");
            }

            if (!kind.IsValid || string.IsNullOrEmpty(anchorId) || string.IsNullOrEmpty(entityId))
            {
                throw new ArgumentException("Anchor, kind and entity identifiers are required.");
            }

            // Keep the existing root when its Key and required type match.
            Key key = new Key(anchorId, kind, entityId);
            Record record;
            if (_identities.TryGetValue(key, out record))
            {
                return ReuseRoot<TGhost>(record, owner, variant, nameValue, blueprint);
            }

            return CreateRoot<TGhost>(owner, key, variant, nameValue, anchor, blueprint);
        }

        internal Presence ApplyPresenceDetection(PresenceDetector detector, string anchorId, string entityId,
            Kind kind, string name, Variant? variant, List<Type> capabilitySnapshot, object source,
            Anchor anchor, ManifestationBlueprintSnapshot blueprint, Action<Presence, Ghost> localInitializer)
        {
            // Use the Anchor's setup callback when present. Otherwise use the Realm's setup rules.
            Key key = new Key(anchorId, kind, entityId);
            IPresenceInitializer initializer = null;
            Ghost root;
            if (localInitializer == null && _presenceInitializers.TryGetValue(kind.Id, out initializer))
            {
                root = initializer.GetOrCreate(this, detector, key, variant, name, anchor, blueprint);
            }
            else
            {
                root = GetOrCreate<Ghost>(detector, anchorId, entityId, kind, variant, name, anchor, blueprint);
            }

            // Keep the same Presence and check whether a changed source needs new readers.
            Record record = _identities.Find(root);
            Presence presence = EnsurePresence(record);
            bool sourceChanged = source != null && presence.SetSource(source);
            if (sourceChanged)
            {
                // Release readers that still use the old source before connecting the new source.
                ClearTraitBindings(root);
            }

            bool capabilitiesChanged = presence.SetMetadata(root.Name, root.Variant, capabilitySnapshot);
            // Changing just the name or appearance does not need new readers.
            if (!record.PresenceInitialized || capabilitiesChanged || sourceChanged)
            {
                if (localInitializer != null)
                {
                    localInitializer(presence, root);
                }
                else if (initializer != null)
                {
                    initializer.Initialize(presence);
                }

                record.PresenceInitialized = true;
            }

            return presence;
        }

        internal void MarkPublished(PresenceDetector owner, IGhost ghost)
        {
            if (ghost == null)
            {
                throw new ArgumentNullException(nameof(ghost));
            }

            Record record = _identities.Find(ghost);
            if (record == null || record.Ghost == null || record.Owner != owner
                || !owner.IsRegistration(_realm, record.RegistrationGeneration))
            {
                throw new ArgumentException("The ghost is not owned by this source's current attachment.", nameof(ghost));
            }

            record.LastPublishedAt = _elapsedSeconds();
            if (record.IsMissing)
            {
                // Mark the root for recovery, but wait until the caller finishes its data before activating it.
                record.IsMissing = false;
                record.MissingUntil = 0;
                record.PendingActivation = true;
            }
        }

        internal void RemoveExpiredGhosts()
        {
            // Use one time reading and one root list for all timeout checks in this pass.
            double now = _elapsedSeconds();
            IReadOnlyList<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count && !_realm.IsDisposed; index++)
            {
                Record record = records[index];
                PresenceDetector owner = record.Owner;
                if (!_identities.Contains(record) || owner == null
                    || !owner.IsRegistration(_realm, record.RegistrationGeneration))
                {
                    continue;
                }

                // Missing roots wait for grace to end. Other roots disappear after their source's timeout.
                if (record.IsMissing)
                {
                    if (now >= record.MissingUntil)
                    {
                        RemoveRecord(record);
                    }

                    continue;
                }

                TimeSpan? timeout = owner.InactivityTimeout;
                if (timeout.HasValue && now - record.LastPublishedAt >= timeout.Value.TotalSeconds)
                {
                    RemoveGhost(owner, record.Key);
                }
            }
        }

        internal void RemoveGhost(PresenceDetector owner, Key key)
        {
            Record record;
            if (!_identities.TryGetValue(key, out record) || record.Owner != owner)
            {
                return;
            }

            if (record.IsMissing)
            {
                return;
            }

            TimeSpan grace = owner.DisappearanceGracePeriod;
            if (grace <= TimeSpan.Zero)
            {
                RemoveRecord(record);
                return;
            }

            // Hide the root now, but keep it and its view request in case the source reports it again.
            record.IsMissing = true;
            record.MissingUntil = _elapsedSeconds() + grace.TotalSeconds;
            InvalidateAvailability(record);
            if (record.Ghost != null)
            {
                _sceneChanges.SetActive(record.Ghost.gameObject, false);
            }
        }

        internal void RemoveSourceGhosts(PresenceDetector owner)
        {
            long generation = owner.RegistrationGeneration;
            List<Record> records = _identities.OwnedBy(owner);
            // Hide all owned roots before cleanup callbacks can use or report them again.
            for (int index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }

            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                // An earlier scene callback may restart this source and report a root we have not removed yet.
                if (_identities.Contains(record) && record.Owner == owner && record.RegistrationGeneration <= generation)
                {
                    RemoveRecord(record);
                }
            }
        }

        internal void CompleteSourceHandover(PresenceDetector owner, long updateNumber, long dispatchSequence)
        {
            List<Record> records = _identities.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (record.RegistrationGeneration != owner.RegistrationGeneration)
                {
                    // Allow one update and the queued startup work for the new source to report each old root.
                    record.HandoverUpdate = updateNumber + 1;
                    record.HandoverDispatchSequence = dispatchSequence;
                }
            }
        }

        internal void RemoveUnpublishedHandoverGhosts(long updateNumber, Func<long, bool> hasPendingThrough)
        {
            IReadOnlyList<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (!_identities.Contains(record) || record.HandoverUpdate == 0 || updateNumber < record.HandoverUpdate)
                {
                    continue;
                }

                // Wait only for startup work. Later queued work must not keep old roots alive forever.
                if (hasPendingThrough(record.HandoverDispatchSequence))
                {
                    continue;
                }

                record.HandoverUpdate = 0;
                if (!record.PendingActivation && record.Ghost != null && !record.Ghost.IsAvailable)
                {
                    RemoveRecord(record);
                }
            }
        }

        internal void TransferSource(PresenceDetector current, PresenceDetector replacement)
        {
            List<Record> records = _identities.OwnedBy(current);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                record.Owner = replacement;
                // Change the owner now. The new source must report the root before it belongs to the new source version.
                record.RegistrationGeneration = -1;
                InvalidateAvailability(record);
            }

            DeactivateRecords(records, replacement);
        }

        internal void MarkUnavailable(PresenceDetector owner)
        {
            // Mark all roots unavailable before OnDisable callbacks can inspect other roots.
            List<Record> records = _identities.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }

            DeactivateRecords(records, owner);
        }

        internal void RemoveAnchorGhosts(string anchorId)
        {
            IReadOnlyList<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (string.Equals(record.Key.AnchorId, anchorId, StringComparison.Ordinal))
                {
                    RemoveRecord(record);
                }
            }
        }

        internal HashSet<Record> CaptureGhosts()
        {
            return new HashSet<Record>(_identities.Values);
        }

        internal void RollbackSource(PresenceDetector owner, HashSet<Record> previous)
        {
            long generation = owner.RegistrationGeneration;
            List<Record> records = _identities.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                // Keep roots already reported again by a restarted source during scene cleanup.
                if (!_identities.Contains(record) || record.Owner != owner || record.RegistrationGeneration > generation)
                {
                    continue;
                }

                // Remove new roots. Keep previously prepared roots for another source to use.
                if (!previous.Contains(record))
                {
                    RemoveRecord(record);
                }
                else if (_identities.Contains(record) && record.Owner == owner)
                {
                    record.Owner = null;
                    InvalidateAvailability(record);
                    if (record.Ghost != null)
                    {
                        _sceneChanges.SetActive(record.Ghost.gameObject, false);
                    }
                }
            }
        }

        internal void RemoveRecord(Record record)
        {
            // Remove the Key first so cleanup callbacks can create a new root with that Key.
            if (!_identities.Remove(record))
            {
                return;
            }

            if (record.Ghost != null)
            {
                // Clear the Anchor link before callbacks can inspect the old root or report its Key again.
                record.Ghost.ClearAnchor();
            }

            if (record.Presence != null)
            {
                record.Presence.MarkRemoved();
            }
            InvalidateAvailability(record);
            _views.Destroy(record);
            if (record.Ghost != null)
            {
                _sceneChanges.Destroy(record.Ghost.gameObject);
            }
        }

        internal bool CanFinalize(Record record, PresenceDetector onlyOwner)
        {
            return !_realm.IsDisposed && _identities.Contains(record) && record.Ghost != null
                && record.Owner != null && (onlyOwner == null || record.Owner == onlyOwner)
                && record.Owner.IsRegistration(_realm, record.RegistrationGeneration);
        }

        internal void RefreshTraits(IReadOnlyList<Record> records, PresenceDetector onlyOwner)
        {
            // Use the saved root list, but check that each source still owns the root before reading its data.
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                Record record = records[recordIndex];
                if (!CanFinalize(record, onlyOwner) || (!record.PendingActivation && !record.Ghost.IsAvailable))
                {
                    continue;
                }

                PresenceDetector owner = record.Owner;
                long generation = record.RegistrationGeneration;
                long ownership = record.OwnershipVersion;
                try
                {
                    IReadOnlyList<Trait> traits = record.Ghost.Traits;
                    for (int index = 0; index < traits.Count; index++)
                    {
                        Trait trait = traits[index];
                        // Unbound traits and presentation-only traits cannot call user code.
                        // Check ownership immediately before each actual reader instead.
                        if (trait == null || !trait.enabled || !trait.NeedsRefresh)
                        {
                            continue;
                        }

                        // An earlier reader may restart the detector or change owners. Stop using the old readers then.
                        if (!CanFinalize(record, onlyOwner) || record.Owner != owner
                            || record.RegistrationGeneration != generation || record.OwnershipVersion != ownership)
                        {
                            break;
                        }

                        trait.Refresh();
                    }
                }
                catch (Exception exception)
                {
                    // An error from an old reader must not stop a source that restarted during that read.
                    if (owner.IsRegistration(_realm, generation))
                    {
                        owner.HandleFailure(exception, PresenceDetector.DescribeError(owner.CaptureErrorContext(),
                            "Update traits", record.Key.Kind, record.Key.EntityId));
                    }
                    else
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }

        internal void UpdateGhosts(IReadOnlyList<Record> records)
        {
            // All trait reads are finished. Run Ghost.OnUpdate only on enabled roots that still have an owner.
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                Record record = records[recordIndex];
                if (!CanFinalize(record, null) || !record.Ghost.enabled
                    || (!record.PendingActivation && !record.Ghost.IsAvailable))
                {
                    continue;
                }

                PresenceDetector owner = record.Owner;
                long generation = record.RegistrationGeneration;
                try
                {
                    record.Ghost.Tick();
                }
                catch (Exception exception)
                {
                    if (owner.IsRegistration(_realm, generation))
                    {
                        owner.HandleFailure(exception, PresenceDetector.DescribeError(owner.CaptureErrorContext(),
                            "Ghost.OnUpdate", record.Key.Kind, record.Key.EntityId));
                    }
                    else
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }

        internal void ActivateRoots(IReadOnlyList<Record> records, PresenceDetector onlyOwner)
        {
            // Make ready roots available before the Realm updates their views.
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (!CanFinalize(record, onlyOwner) || !record.PendingActivation)
                {
                    continue;
                }

                PresenceDetector owner = record.Owner;
                long generation = record.RegistrationGeneration;
                long ownership = record.OwnershipVersion;
                try
                {
                    // Set IsAvailable before OnEnable so callbacks see a ready root.
                    record.PendingActivation = false;
                    record.ViewDirty = true;
                    record.Ghost.SetAvailable(true);
                    if (record.Presence != null)
                    {
                        record.Presence.SetAvailable(true);
                    }
                    _sceneChanges.SetActive(record.Ghost.gameObject, true);
                    if (!CanFinalize(record, onlyOwner) || record.OwnershipVersion != ownership)
                    {
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    if (owner.IsRegistration(_realm, generation))
                    {
                        owner.HandleFailure(exception, PresenceDetector.DescribeError(owner.CaptureErrorContext(), "Activate", record.Key.Kind, record.Key.EntityId));
                    }
                    else
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }

        private TGhost ReuseRoot<TGhost>(Record record, PresenceDetector owner, Variant? variant,
            string nameValue, ManifestationBlueprintSnapshot blueprint) where TGhost : Ghost
        {
            TGhost existingTyped = RequireGhost<TGhost>(record.Ghost);
            if (owner != null && record.Owner != null && record.Owner != owner)
            {
                throw new InvalidOperationException("The ghost is owned by another source.");
            }

            // Update changed view settings even when the root and owner stay the same.
            if (!ReferenceEquals(record.ManifestationBlueprint, blueprint))
            {
                record.ManifestationBlueprint = blueprint;
                if (record.ViewRequested)
                {
                    record.ViewVersion++;
                    record.ViewDirty = true;
                }
            }

            // Set the owner and refresh the timeout now. Activate only after the source finishes its data.
            bool variantChanged = variant.HasValue && record.Ghost.Variant != variant.Value;
            record.Owner = owner ?? record.Owner;
            if (owner != null)
            {
                record.HandoverUpdate = 0;
                record.RegistrationGeneration = owner.RegistrationGeneration;
                record.LastPublishedAt = _elapsedSeconds();
                record.IsMissing = false;
                record.MissingUntil = 0;
            }

            // A changed appearance needs a view refresh even when the Ghost stays the same.
            record.Ghost.SetMetadata(nameValue, variant);
            if (variantChanged)
            {
                record.ViewVersion++;
                record.ViewDirty = true;
            }

            if (owner != null)
            {
                if (!record.Ghost.IsAvailable)
                {
                    record.Ghost.SetAvailable(false);
                    record.PendingActivation = true;
                }
            }

            if (record.Presence != null)
            {
                EnsurePresence(record);
            }

            return existingTyped;
        }

        private TGhost CreateRoot<TGhost>(PresenceDetector owner, Key key, Variant? variant, string nameValue,
            Anchor anchor, ManifestationBlueprintSnapshot blueprint) where TGhost : Ghost
        {
            Ghost prefab = blueprint == null ? null : blueprint.GhostPrefab;
            if (anchor == null || anchor.Transform == null)
            {
                throw new InvalidOperationException("The anchor '" + key.AnchorId + "' does not exist.");
            }

            Transform anchorTransform = anchor.Transform;

            // Keep the root inactive until its Key and first data are ready.
            GameObject staging = new GameObject("[Emas Staging]");
            staging.transform.SetParent(anchorTransform, false);
            staging.SetActive(false);

            TGhost typed;
            try
            {
                // Create under an inactive parent so OnEnable cannot run before the root gets its Key.
                if (prefab == null)
                {
                    GameObject gameObject = new GameObject(key.EntityId);
                    gameObject.transform.SetParent(staging.transform, false);
                    typed = gameObject.AddComponent<TGhost>();
                }
                else
                {
                    Ghost clone = UnityEngine.Object.Instantiate(prefab, staging.transform, false);
                    typed = clone.GetComponent<TGhost>();
                    if (typed == null)
                    {
                        throw new InvalidOperationException("ManifestationBlueprint for kind " + key.Kind.Id + " prefab " + prefab.name + " does not contain required component " + typeof(TGhost).FullName + ".");
                    }
                }

                // Set the Key, then move the inactive root under its Anchor.
                typed.gameObject.name = key.EntityId;
                typed.gameObject.SetActive(false);
                typed.Initialize(anchor, key, nameValue ?? key.EntityId, variant ?? Variant.None);
                typed.transform.SetParent(anchorTransform, false);
                typed.gameObject.SetActive(false);
            }
            catch
            {
                UnityEngine.Object.Destroy(staging);
                throw;
            }

            UnityEngine.Object.Destroy(staging);
            typed.SetAvailable(false);
            // Add the ready root to tracking so failed startup can undo it or restore a prepared root.
            Record newRecord = new Record(typed, owner, blueprint);
            newRecord.LastPublishedAt = owner == null ? 0 : _elapsedSeconds();
            newRecord.PendingActivation = owner != null;
            newRecord.ViewDirty = true;
            _identities.Add(newRecord);
            return typed;
        }

        private Presence EnsurePresence(Record record)
        {
            // Name and appearance changes keep the same Presence. Only removal ends its lifetime.
            if (record.Presence == null)
            {
                record.Presence = new Presence(_realm, record.Key, record.Ghost.Name, record.Ghost.Variant);
                record.Presence.BindRoot(record.Ghost);
            }
            else
            {
                record.Presence.SetMetadata(record.Ghost.Name, record.Ghost.Variant, null);
            }

            record.Presence.SetAvailable(record.Ghost.IsAvailable);
            return record.Presence;
        }

        private TGhost RequireGhost<TGhost>(IGhost ghost) where TGhost : Ghost
        {
            TGhost typed = ghost as TGhost;
            if (typed == null)
            {
                throw new InvalidOperationException("The existing ghost does not contain " + typeof(TGhost).FullName + ".");
            }

            return typed;
        }

        private void InvalidateAvailability(Record record)
        {
            // Release old readers before hiding the root, even when keeping it for a restart or grace period.
            record.PendingActivation = false;
            record.PresenceInitialized = false;
            if (record.Ghost != null)
            {
                ClearTraitBindings(record.Ghost);
            }

            // Change these versions so unfinished reader and view work knows it must stop using old data.
            record.OwnershipVersion++;
            record.ViewVersion++;
            record.ViewDirty = true;
            if (record.Ghost != null)
            {
                record.Ghost.SetAvailable(false);
            }

            if (record.Presence != null)
            {
                record.Presence.SetAvailable(false);
                record.Presence.SetSource(null);
            }

            // Report the loss to queries even if the source reports the same Key again right away.
            _subscriptions.Forget(record.Key);
        }

        private static void ClearTraitBindings(Ghost root)
        {
            foreach (Trait trait in root.Traits)
            {
                if (trait != null)
                {
                    trait.ClearBinding();
                }
            }
        }

        private void DeactivateRecords(IReadOnlyList<Record> records, PresenceDetector owner)
        {
            // An earlier OnDisable may report another root again. Check its owner and availability before hiding it.
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (_identities.Contains(record) && record.Owner == owner && record.Ghost != null
                    && !record.Ghost.IsAvailable)
                {
                    _sceneChanges.SetActive(record.Ghost.gameObject, false);
                }
            }
        }
    }
}
