using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Owns tracked entity creation, initialization, ownership and availability transitions.
    /// </summary>
    internal sealed class Population
    {
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
            Transform anchorTransform,
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

            // Reuse a compatible root whenever this identity already exists.
            Key key = new Key(anchorId, kind, entityId);
            Record record;
            if (_identities.TryGetValue(key, out record))
            {
                TGhost existingTyped = RequireGhost<TGhost>(record.Ghost);
                if (owner != null && record.Owner != null && record.Owner != owner)
                {
                    throw new InvalidOperationException("The ghost is owned by another source.");
                }

                if (!ReferenceEquals(record.ManifestationBlueprint, blueprint))
                {
                    record.ManifestationBlueprint = blueprint;
                    if (record.ViewRequested)
                    {
                        record.ViewVersion++;
                        record.ViewDirty = true;
                    }
                }

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

            Ghost prefab = blueprint == null ? null : blueprint.GhostPrefab;
            if (anchorTransform == null)
            {
                throw new InvalidOperationException("The anchor '" + anchorId + "' does not exist.");
            }

            // Keep the root inactive until its identity and initial data are ready.
            GameObject staging = new GameObject("[Emas Staging]");
            staging.transform.SetParent(anchorTransform, false);
            staging.SetActive(false);

            TGhost typed;
            try
            {
                if (prefab == null)
                {
                    GameObject gameObject = new GameObject("[Ghost] " + entityId);
                    gameObject.transform.SetParent(staging.transform, false);
                    typed = gameObject.AddComponent<TGhost>();
                }
                else
                {
                    Ghost clone = UnityEngine.Object.Instantiate(prefab, staging.transform, false);
                    typed = clone.GetComponent<TGhost>();
                    if (typed == null)
                    {
                        throw new InvalidOperationException("ManifestationBlueprint for kind " + kind.Id + " prefab " + prefab.name + " does not contain required component " + typeof(TGhost).FullName + ".");
                    }
                }

                typed.gameObject.SetActive(false);
                typed.Initialize(key, nameValue ?? entityId, variant ?? Variant.None);
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
            Record newRecord = new Record(typed, owner, blueprint);
            newRecord.LastPublishedAt = owner == null ? 0 : _elapsedSeconds();
            newRecord.PendingActivation = owner != null;
            newRecord.ViewDirty = true;
            _identities.Add(newRecord);
            return typed;
        }

        internal Presence ApplyPresenceReport(PresenceDetector detector, string anchorId, string entityId,
            Kind kind, string name, Variant? variant, List<Type> capabilitySnapshot, object data,
            Transform anchorTransform, ManifestationBlueprintSnapshot blueprint)
        {
            Key key = new Key(anchorId, kind, entityId);
            IPresenceInitializer initializer;
            Ghost root;
            if (_presenceInitializers.TryGetValue(kind.Id, out initializer))
            {
                root = initializer.GetOrCreate(this, detector, key, variant, name, anchorTransform, blueprint);
            }
            else
            {
                Record existing;
                if (_identities.TryGetValue(key, out existing) || (blueprint != null && blueprint.GhostPrefab != null))
                {
                    root = GetOrCreate<Ghost>(detector, anchorId, entityId, kind, variant, name, anchorTransform, blueprint);
                }
                else
                {
                    root = GetOrCreate<DefaultGhost>(detector, anchorId, entityId, kind, variant, name, anchorTransform, blueprint);
                }
            }

            Record record = _identities.Find(root);
            Presence presence = EnsurePresence(record);
            bool capabilitiesChanged = presence.SetMetadata(root.Name, root.Variant, capabilitySnapshot);
            if (!record.PresenceInitialized || capabilitiesChanged)
            {
                if (initializer != null)
                {
                    initializer.Initialize(presence);
                }

                record.PresenceInitialized = true;
            }

            if (data != null)
            {
                presence.ApplyData(data);
            }

            return presence;
        }

        private Presence EnsurePresence(Record record)
        {
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
                record.IsMissing = false;
                record.MissingUntil = 0;
                record.PendingActivation = true;
            }
        }

        internal void RemoveExpiredGhosts()
        {
            double now = _elapsedSeconds();
            List<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count && !_realm.IsDisposed; index++)
            {
                Record record = records[index];
                PresenceDetector owner = record.Owner;
                if (!_identities.Contains(record) || owner == null
                    || !owner.IsRegistration(_realm, record.RegistrationGeneration))
                {
                    continue;
                }

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
            // Hide the whole population before destruction callbacks can observe or reattach it.
            for (int index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }

            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                // Scene callbacks may reattach this source and reclaim a root that is still in this batch.
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
                    record.HandoverUpdate = updateNumber + 1;
                    record.HandoverDispatchSequence = dispatchSequence;
                }
            }
        }

        internal void RemoveUnpublishedHandoverGhosts(long updateNumber, Func<long, bool> hasPendingThrough)
        {
            List<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (!_identities.Contains(record) || record.HandoverUpdate == 0 || updateNumber < record.HandoverUpdate)
                {
                    continue;
                }

                // Wait only for work queued by startup, so later traffic cannot prolong the handover indefinitely.
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
                record.RegistrationGeneration = -1;
                InvalidateAvailability(record);
            }

            DeactivateRecords(records, replacement);
        }

        internal void MarkUnavailable(PresenceDetector owner)
        {
            List<Record> records = _identities.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }

            DeactivateRecords(records, owner);
        }

        internal void RemoveAnchorGhosts(string anchorId)
        {
            List<Record> records = _identities.Snapshot();
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
                // Preserve roots reclaimed by a newer attachment during earlier scene cleanup.
                if (!_identities.Contains(record) || record.Owner != owner || record.RegistrationGeneration > generation)
                {
                    continue;
                }

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
            if (!_identities.Remove(record))
            {
                return;
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

        private void InvalidateAvailability(Record record)
        {
            record.PendingActivation = false;
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
            }

            _subscriptions.Forget(record.Key);
        }

        private void DeactivateRecords(List<Record> records, PresenceDetector owner)
        {
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

        internal bool CanFinalize(Record record, PresenceDetector onlyOwner)
        {
            return !_realm.IsDisposed && _identities.Contains(record) && record.Ghost != null
                && record.Owner != null && (onlyOwner == null || record.Owner == onlyOwner)
                && record.Owner.IsRegistration(_realm, record.RegistrationGeneration);
        }

        internal void ActivateRoots(List<Record> records, PresenceDetector onlyOwner)
        {
            // Make initialized roots available before Realm refreshes their views.
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

        private interface IPresenceInitializer
        {
            Ghost GetOrCreate(Population population, PresenceDetector detector, Key key, Variant? variant,
                string name, Transform anchorTransform, ManifestationBlueprintSnapshot blueprint);

            void Initialize(Presence presence);
        }

        private sealed class PresenceInitializer<TGhost> : IPresenceInitializer where TGhost : Ghost
        {
            private readonly Action<Presence, TGhost> _initialize;

            internal PresenceInitializer(Action<Presence, TGhost> initialize)
            {
                _initialize = initialize;
            }

            /// <summary>
            /// Resolves the root type selected by this initializer.
            /// </summary>
            public Ghost GetOrCreate(Population population, PresenceDetector detector, Key key, Variant? variant,
                string name, Transform anchorTransform, ManifestationBlueprintSnapshot blueprint)
            {
                return population.GetOrCreate<TGhost>(detector, key.AnchorId, key.EntityId, key.Kind, variant,
                    name, anchorTransform, blueprint);
            }

            /// <summary>
            /// Applies the registered module setup to this presence.
            /// </summary>
            public void Initialize(Presence presence)
            {
                _initialize(presence, (TGhost)presence.Root);
            }
        }
    }
}
