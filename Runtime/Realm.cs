using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Owns anchors, ghosts, blueprints, views and query subscriptions.
    /// </summary>
    /// <remarks>
    /// All operations require Unity's main thread. The application handles SDK threading before calling Emas.
    /// Dispose isolated realms when their owner stops; Unity advances Realm.Default automatically.
    /// </remarks>
    public sealed class Realm : IDisposable
    {
        /// <summary>
        /// Gets the shared realm advanced automatically by Unity.
        /// </summary>
        /// <value>
        /// The live default realm; a disposed default is recreated on access.
        /// </value>
        public static Realm Default
        {
            get
            {
                return DefaultRuntime.Realm;
            }
        }

        /// <summary>
        /// Gets a copied, read-only snapshot of this realm's anchors.
        /// </summary>
        /// <remarks>
        /// Read on the Unity thread. Earlier snapshots do not change; disposed realms return an empty snapshot.
        /// The referenced anchors retain their own lifetimes. No ordering is guaranteed.
        /// </remarks>
        public IReadOnlyList<Anchor> Anchors
        {
            get
            {
                if (_disposed)
                {
                    return Array.AsReadOnly(Array.Empty<Anchor>());
                }

                return new List<Anchor>(_anchors.Values).AsReadOnly();
            }
        }

        private readonly Dictionary<string, Anchor> _anchors = new Dictionary<string, Anchor>();
        private readonly Registry _ghosts = new Registry();
        private readonly BlueprintRegistry _blueprints = new BlueprintRegistry();
        private readonly Subscriptions _subscriptions;
        private readonly Func<double> _elapsedSeconds;
        private readonly ViewManager _views;
        private readonly SceneEffects _scene = new SceneEffects();
        private readonly Queue<DispatchItem> _dispatch = new Queue<DispatchItem>();
        // Deterministic action budget: newly queued work waits for the following update.
        internal const int MaxDispatchActionsPerUpdate = 256;
        private long _updateNumber;
        private long _dispatchSequence;
        private int _sourceDepth;
        private bool _finalizing;
        private bool _updating;
        private bool _disposed;

        /// <summary>
        /// Creates an isolated realm advanced explicitly with Update.
        /// </summary>
        public Realm()
            : this(() => Time.realtimeSinceStartupAsDouble)
        {
        }

        internal Realm(Func<double> elapsedSeconds)
        {
            _elapsedSeconds = elapsedSeconds ?? throw new ArgumentNullException(nameof(elapsedSeconds));
            _subscriptions = new Subscriptions(this);
            _views = new ViewManager(_ghosts, _scene);
        }

        internal bool IsDisposed
        {
            get
            {
                return _disposed;
            }
        }

        internal void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Realm));
            }
        }

        internal bool ContainsAnchor(string id)
        {
            return _anchors.ContainsKey(id);
        }

        /// <summary>
        /// Registers a blueprint by kind.
        /// </summary>
        /// <param name="blueprint">
        /// The blueprint to register.
        /// </param>
        /// <remarks>
        /// Assets remain application-owned. This realm-wide default applies where an anchor has no override.
        /// Each registration captures the asset's settings. Re-register after edits to refresh requested views on the next update;
        /// existing roots stay intact. Re-register after changing its kind to release the previous kind registration.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The blueprint is null or its kind/view mappings are invalid.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public void RegisterBlueprint(Blueprint blueprint)
        {
            ThrowIfDisposed();
            ValidateBlueprint(blueprint);
            List<Kind> staleKinds = _blueprints.Register(blueprint);
            RebindBlueprintRegistration(staleKinds, blueprint.Kind, null);
        }

        internal void RegisterBlueprint(Anchor anchor, Blueprint blueprint)
        {
            ThrowIfDisposed();
            anchor.ThrowIfDisposed();
            ValidateBlueprint(blueprint);
            List<Kind> staleKinds = anchor.SetBlueprint(blueprint);
            RebindBlueprintRegistration(staleKinds, blueprint.Kind, anchor.Id);
        }

        internal void UnregisterBlueprint(Anchor anchor, Kind kind)
        {
            ThrowIfDisposed();
            anchor.ThrowIfDisposed();
            if (!kind.IsValid)
            {
                throw new ArgumentException("The blueprint kind must be valid.", nameof(kind));
            }

            if (anchor.RemoveBlueprint(kind))
            {
                RebindBlueprints(kind, anchor.Id);
            }
        }

        private void RebindBlueprintRegistration(List<Kind> staleKinds, Kind kind, string anchorId)
        {
            if (staleKinds != null)
            {
                for (int index = 0; index < staleKinds.Count; index++)
                {
                    RebindBlueprints(staleKinds[index], anchorId);
                }
            }

            RebindBlueprints(kind, anchorId);
        }

        private static void ValidateBlueprint(Blueprint blueprint)
        {
            if (blueprint == null)
            {
                throw new ArgumentException("A valid blueprint is required.", nameof(blueprint));
            }

            string error = blueprint.GetConfigurationError();
            if (error != null)
            {
                throw new ArgumentException(error, nameof(blueprint));
            }
        }

        /// <summary>
        /// Creates or returns an anchor at the root scene frame.
        /// </summary>
        /// <param name="id">
        /// The anchor identifier.
        /// </param>
        /// <param name="sources">
        /// The sources to attach and start, including on an existing anchor.
        /// </param>
        /// <returns>
        /// The existing or new anchor.
        /// </returns>
        /// <remarks>
        /// On failure, newly attached sources are removed and prepared ghosts are restored; existing sources remain.
        /// A newly created anchor is disposed.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The anchor ID is empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// An existing anchor uses another frame, or source attachment fails.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public Anchor GetOrCreateAnchor(string id, params PresenceSource[] sources)
        {
            return GetOrCreateAnchor(id, null, sources);
        }

        /// <summary>
        /// Creates or returns an anchor under a scene frame.
        /// </summary>
        /// <param name="id">
        /// The anchor identifier.
        /// </param>
        /// <param name="frame">
        /// The optional parent transform.
        /// </param>
        /// <param name="sources">
        /// The sources to attach and start, including on an existing anchor.
        /// </param>
        /// <returns>
        /// The existing or new anchor.
        /// </returns>
        /// <remarks>
        /// On failure, newly attached sources are removed and prepared ghosts are restored; existing sources remain.
        /// A newly created anchor is disposed.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The anchor ID is empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// An existing anchor uses another frame, or source attachment fails.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public Anchor GetOrCreateAnchor(string id, Transform frame, params PresenceSource[] sources)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An anchor requires an identifier.", nameof(id));
            }

            Anchor anchor;
            bool created = false;
            List<PresenceSource> newSources = null;
            HashSet<Record> previousRecords = null;
            if (!_anchors.TryGetValue(id, out anchor))
            {
                anchor = new Anchor(this, id, frame);
                _anchors.Add(id, anchor);
                created = true;
            }

            try
            {
                if (anchor.Transform.parent != frame)
                {
                    if (frame != null || anchor.Transform.parent != null)
                    {
                        throw new InvalidOperationException("The existing anchor uses a different coordinate frame.");
                    }
                }

                if (sources != null)
                {
                    for (int index = 0; index < sources.Length; index++)
                    {
                        PresenceSource source = sources[index];
                        if (!created && !anchor.ContainsSource(source))
                        {
                            if (newSources == null)
                            {
                                newSources = new List<PresenceSource>();
                                previousRecords = CaptureGhosts();
                            }

                            newSources.Add(source);
                        }

                        anchor.AddSource(source);
                    }
                }

                return anchor;
            }
            catch
            {
                if (created)
                {
                    anchor.Dispose();
                }
                else if (newSources != null)
                {
                    RollbackAnchorSources(anchor, newSources, previousRecords);
                }

                throw;
            }
        }

        private void RollbackAnchorSources(Anchor anchor, List<PresenceSource> sources, HashSet<Record> previous)
        {
            for (int index = sources.Count - 1; index >= 0; index--)
            {
                Anchor current;
                if (_disposed || !_anchors.TryGetValue(anchor.Id, out current) || !ReferenceEquals(current, anchor))
                {
                    return;
                }

                try
                {
                    anchor.RollbackAddedSource(sources[index], previous);
                }
                catch (Exception exception)
                {
                    PresenceSource.LogError(exception, "rolling back source attachment to anchor '" + anchor.Id + "'");
                }
            }
        }

        /// <summary>
        /// Removes and disposes an anchor.
        /// </summary>
        /// <param name="id">
        /// The anchor identifier.
        /// </param>
        /// <remarks>
        /// Unknown IDs are ignored.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The ID is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public void RemoveAnchor(string id)
        {
            ThrowIfDisposed();
            Anchor anchor;
            if (_anchors.TryGetValue(id, out anchor))
            {
                anchor.Dispose();
            }
        }

        /// <summary>
        /// Finds a tracked ghost by its exact identity, including unavailable ghosts.
        /// </summary>
        /// <param name="key">
        /// The case-sensitive anchor, kind and entity identity.
        /// </param>
        /// <param name="ghost">
        /// The tracked ghost, or null when no live object exists for this identity.
        /// </param>
        /// <returns>
        /// True when this realm contains the ghost; false for missing or invalid keys and disposed realms.
        /// </returns>
        /// <remarks>
        /// Read on Unity's main thread. Prepared ghosts and ghosts in a source handover
        /// can be returned while unavailable; check IsAvailable before consuming their data. Lookup does not
        /// create, activate or update anything. A returned reference remains subject to its tracking lifetime.
        /// </remarks>
        public bool TryGetGhost(Key key, out IGhost ghost)
        {
            ghost = null;
            if (_disposed)
            {
                return false;
            }

            // Resolve identity independently of availability, excluding destroyed Unity objects.
            Record record;
            if (!_ghosts.TryGetValue(key, out record) || record.Ghost == null)
            {
                return false;
            }

            ghost = record.Ghost;
            return true;
        }

        /// <summary>
        /// Creates a query by partial display name.
        /// </summary>
        /// <param name="partialName">
        /// The optional case-insensitive partial name.
        /// </param>
        /// <returns>
        /// The query.
        /// </returns>
        public Query Query(string partialName = null)
        {
            return new Query(this, partialName);
        }

        /// <summary>
        /// Uses an existing immutable query description in this realm.
        /// </summary>
        /// <param name="description">
        /// The query description.
        /// </param>
        /// <returns>
        /// A query bound to this realm.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the description is null.
        /// </exception>
        public Query Query(Query description)
        {
            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            return description.Rebind(this);
        }

        /// <summary>
        /// Prepares an unavailable typed ghost before source discovery.
        /// </summary>
        /// <typeparam name="TGhost">
        /// The ghost component type.
        /// </typeparam>
        /// <param name="anchorId">
        /// The anchor identifier.
        /// </param>
        /// <param name="kind">
        /// The ghost kind.
        /// </param>
        /// <param name="entityId">
        /// The source entity identifier.
        /// </param>
        /// <param name="variant">
        /// The appearance; null retains an existing value, and None clears it.
        /// </param>
        /// <returns>
        /// The prepared ghost.
        /// </returns>
        /// <remarks>
        /// The anchor must exist. Preparation never makes a ghost available or assigns a source.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The kind or entity ID is invalid.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The anchor does not exist.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public TGhost Prepare<TGhost>(string anchorId, Kind kind, string entityId, Variant? variant = null) where TGhost : Ghost
        {
            return GetOrCreate<TGhost>(null, anchorId, entityId, kind, variant, null);
        }

        /// <summary>
        /// Requests a view at Full detail when no view request exists, or refreshes the existing request.
        /// </summary>
        /// <param name="ghost">
        /// The ghost.
        /// </param>
        /// <returns>
        /// The view component, or null when no view can be created yet.
        /// </returns>
        /// <remarks>
        /// Null, foreign and removed ghosts return null. Detail level None clears the request.
        /// Presentation failures are logged without stopping tracking; call Manifest again after fixing the cause to retry.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public View Manifest(IGhost ghost)
        {
            ThrowIfDisposed();
            Record record = FindRecord(ghost);
            if (record != null && record.ViewRequested)
            {
                RefreshView(record);
                return record.View;
            }

            return Manifest(ghost, DetailLevel.Full);
        }

        /// <summary>
        /// Requests a view at a specific detail level.
        /// </summary>
        /// <remarks>
        /// Requests made during source mutation or finalization are refreshed after source data is complete.
        /// </remarks>
        /// <param name="ghost">
        /// The ghost.
        /// </param>
        /// <param name="detailLevel">
        /// The desired detail level.
        /// </param>
        /// <returns>
        /// The view component, or null when no view can be created yet.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the detail level is negative.
        /// </exception>
        /// <remarks>
        /// Null, foreign and removed ghosts return null. Detail level None clears the request.
        /// Presentation failures are logged without stopping tracking; call Manifest again after fixing the cause to retry.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public View Manifest(IGhost ghost, DetailLevel detailLevel)
        {
            ThrowIfDisposed();
            ValidateDetailLevel(detailLevel);
            Record record = FindRecord(ghost);
            if (record == null)
            {
                return null;
            }

            record.ViewVersion++;
            record.ViewDirty = true;
            record.RequestedDetailLevel = detailLevel;
            record.ViewRequested = detailLevel.Level > 0;
            if (!record.ViewRequested)
            {
                _views.Destroy(record);
                return null;
            }

            RefreshView(record);
            return record.View;
        }

        /// <summary>
        /// Removes a ghost's view while retaining its ghost.
        /// </summary>
        /// <param name="ghost">
        /// The ghost to demanifest.
        /// </param>
        /// <remarks>
        /// Null, foreign and removed ghosts are ignored; source availability is unchanged.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public void Demanifest(IGhost ghost)
        {
            ThrowIfDisposed();
            Record record = FindRecord(ghost);
            if (record == null)
            {
                return;
            }

            record.ViewVersion++;
            record.ViewDirty = false;
            record.ViewRequested = false;
            record.RequestedDetailLevel = DetailLevel.None;
            _views.Destroy(record);
        }

        /// <summary>
        /// Changes the requested view detail level.
        /// </summary>
        /// <param name="ghost">
        /// The ghost whose view should change.
        /// </param>
        /// <param name="detailLevel">
        /// The desired detail level.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the detail level is negative.
        /// </exception>
        /// <remarks>
        /// Does not create a request for a never-requested ghost. Null, foreign and removed ghosts are ignored.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The realm was disposed.
        /// </exception>
        public void SetDetailLevel(IGhost ghost, DetailLevel detailLevel)
        {
            ThrowIfDisposed();
            ValidateDetailLevel(detailLevel);
            Record record = FindRecord(ghost);
            if (record == null)
            {
                return;
            }

            record.ViewVersion++;
            record.ViewDirty = true;
            record.RequestedDetailLevel = detailLevel;
            if (detailLevel.Level <= 0)
            {
                if (record.ViewRequested)
                {
                    record.ViewRequested = false;
                    _views.Destroy(record);
                }

                return;
            }

            if (record.ViewRequested)
            {
                RefreshView(record);
            }
        }

        /// <summary>
        /// Applies a bounded source batch, cleans up expired ghosts, finalizes availability and views, then notifies subscribers.
        /// </summary>
        /// <remarks>
        /// Processes at most 256 queued actions present at update entry. Newly queued actions wait for a later update. A disposed realm does nothing.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Called inside another update, source callback or finalization phase.
        /// </exception>
        public void Update()
        {
            if (_disposed)
            {
                return;
            }

            if (_updating || _sourceDepth > 0 || _finalizing)
            {
                throw new InvalidOperationException("Emas realm updates cannot be reentrant.");
            }

            _updating = true;
            _updateNumber++;
            try
            {
                // Capture the batch boundary before callbacks can enqueue more work.
                int dispatchCount = Math.Min(_dispatch.Count, MaxDispatchActionsPerUpdate);
                for (int index = 0; index < dispatchCount && !_disposed; index++)
                {
                    ExecuteDispatch(_dispatch.Dequeue());
                }

                // Source callbacks may remove anchors while this snapshot is being processed.
                List<Anchor> anchors = new List<Anchor>(_anchors.Values);
                for (int index = 0; index < anchors.Count && !_disposed; index++)
                {
                    Anchor anchor = anchors[index];
                    Anchor current;
                    if (!_anchors.TryGetValue(anchor.Id, out current) || current != anchor)
                    {
                        continue;
                    }

                    if (anchor.Transform == null)
                    {
                        anchor.Dispose();
                        continue;
                    }

                    anchor.Tick();
                }

                if (!_disposed)
                {
                    // Let publications in this update refresh their deadlines before expiring silent ghosts.
                    RemoveUnpublishedHandoverGhosts();
                    RemoveExpiredGhosts();
                    // Expose complete data and refresh views before notifying consumers.
                    FinalizeChanges(null);
                    _subscriptions.NotifyAll();
                }
            }
            finally
            {
                _updating = false;
            }
        }

        /// <summary>
        /// Disposes all anchors, ghosts, views and subscriptions.
        /// </summary>
        /// <remarks>
        /// Repeated disposal is safe. Subsequent mutating operations throw ObjectDisposedException.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // Invalidate pending work before source cleanup can call back into the realm.
            _disposed = true;
            _dispatch.Clear();
            _subscriptions.Clear();
            List<Anchor> anchors = new List<Anchor>(_anchors.Values);
            for (int index = 0; index < anchors.Count; index++)
            {
                anchors[index].Dispose();
            }

            List<Record> records = _ghosts.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                RemoveRecord(records[index].Key, records[index]);
            }

            _blueprints.Clear();
        }

        internal List<IGhost> Evaluate(Query query)
        {
            List<IGhost> result = new List<IGhost>();
            Evaluate(query, result);
            return result;
        }

        internal void Evaluate(Query query, List<IGhost> result)
        {
            result.Clear();
            foreach (Record record in _ghosts.Values)
            {
                if (query.Matches(record.Ghost))
                {
                    result.Add(record.Ghost);
                }
            }
        }

        internal int CountMatches(Query query, out IGhost first)
        {
            first = null;
            int count = 0;
            foreach (Record record in _ghosts.Values)
            {
                if (query.Matches(record.Ghost))
                {
                    if (count == 0)
                    {
                        first = record.Ghost;
                    }

                    count++;
                }
            }

            return count;
        }

        internal IGhost FirstMatch(Query query)
        {
            foreach (Record record in _ghosts.Values)
            {
                if (query.Matches(record.Ghost))
                {
                    return record.Ghost;
                }
            }

            return null;
        }

        internal IDisposable Subscribe(Query query, Action<IGhost> callback, Action<Key> onLeave = null)
        {
            ThrowIfDisposed();
            return _subscriptions.Subscribe(query, callback, onLeave, _sourceDepth == 0 && !_finalizing);
        }

        internal void Dispatch(PresenceSource source, long generation, Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_disposed)
            {
                return;
            }

            _dispatch.Enqueue(new DispatchItem(
                source,
                generation,
                action,
                ++_dispatchSequence));
        }

        internal TGhost GetOrCreate<TGhost>(
            PresenceSource owner,
            string anchorId,
            string entityId,
            Kind kind,
            Variant? variant,
            string nameValue) where TGhost : Ghost
        {
            ThrowIfDisposed();
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
            if (_ghosts.TryGetValue(key, out record))
            {
                TGhost existingTyped = RequireGhost<TGhost>(record.Ghost);
                if (owner != null && record.Owner != null && record.Owner != owner)
                {
                    throw new InvalidOperationException("The ghost is owned by another source.");
                }

                BlueprintSnapshot resolved = ResolveBlueprint(anchorId, kind);
                if (!ReferenceEquals(record.Blueprint, resolved))
                {
                    record.Blueprint = resolved;
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

                return existingTyped;
            }

            BlueprintSnapshot blueprint = ResolveBlueprint(anchorId, kind);
            Ghost prefab = blueprint == null ? null : blueprint.GhostPrefab;
            Transform anchorTransform = GetAnchorTransform(anchorId);
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
                        throw new InvalidOperationException("Blueprint for kind " + kind.Id + " prefab " + prefab.name + " does not contain required component " + typeof(TGhost).FullName + ".");
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
            _ghosts.Add(key, newRecord);
            return typed;
        }

        internal void MarkPublished(PresenceSource owner, IGhost ghost)
        {
            ThrowIfDisposed();
            if (ghost == null)
            {
                throw new ArgumentNullException(nameof(ghost));
            }

            Record record = FindRecord(ghost);
            if (record == null || record.Ghost == null || record.Owner != owner
                || !owner.IsRegistration(this, record.RegistrationGeneration))
            {
                throw new ArgumentException("The ghost is not owned by this source's current attachment.", nameof(ghost));
            }

            record.LastPublishedAt = _elapsedSeconds();
        }

        private void RemoveExpiredGhosts()
        {
            double now = _elapsedSeconds();
            List<Record> records = _ghosts.Snapshot();
            for (int index = 0; index < records.Count && !_disposed; index++)
            {
                Record record = records[index];
                PresenceSource owner = record.Owner;
                if (!_ghosts.Contains(record) || owner == null
                    || !owner.IsRegistration(this, record.RegistrationGeneration))
                {
                    continue;
                }

                TimeSpan? timeout = owner.InactivityTimeout;
                if (timeout.HasValue && now - record.LastPublishedAt >= timeout.Value.TotalSeconds)
                {
                    RemoveRecord(record.Key, record);
                }
            }
        }

        internal void FinalizeSource(PresenceSource owner)
        {
            if (!_updating && _sourceDepth == 0 && !_finalizing && !_disposed)
            {
                FinalizeChanges(owner);
            }
        }

        internal void RemoveGhost(PresenceSource owner, Key key)
        {
            Record record;
            if (!_ghosts.TryGetValue(key, out record) || record.Owner != owner)
            {
                return;
            }

            RemoveRecord(key, record);
        }

        internal void RemoveSourceGhosts(PresenceSource owner)
        {
            long generation = owner.RegistrationGeneration;
            List<Record> records = _ghosts.OwnedBy(owner);
            // Hide the whole population before destruction callbacks can observe or reattach it.
            for (int index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }

            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                // Scene callbacks may reattach this source and reclaim a root that is still in this batch.
                if (_ghosts.Contains(record) && record.Owner == owner && record.RegistrationGeneration <= generation)
                {
                    RemoveRecord(record.Key, record);
                }
            }
        }

        internal void CompleteSourceHandover(PresenceSource owner)
        {
            List<Record> records = _ghosts.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (record.RegistrationGeneration != owner.RegistrationGeneration)
                {
                    record.HandoverUpdate = _updateNumber + 1;
                    record.HandoverDispatchSequence = _dispatchSequence;
                }
            }
        }

        private void RemoveUnpublishedHandoverGhosts()
        {
            List<Record> records = _ghosts.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (!_ghosts.Contains(record) || record.HandoverUpdate == 0 || _updateNumber < record.HandoverUpdate)
                {
                    continue;
                }

                // Wait only for work queued by startup, so later traffic cannot prolong the handover indefinitely.
                if (_dispatch.Count > 0 && _dispatch.Peek().Sequence <= record.HandoverDispatchSequence)
                {
                    continue;
                }

                record.HandoverUpdate = 0;
                if (!record.PendingActivation && record.Ghost != null && !record.Ghost.IsAvailable)
                {
                    RemoveRecord(record.Key, record);
                }
            }
        }

        internal void TransferSource(PresenceSource current, PresenceSource replacement)
        {
            List<Record> records = _ghosts.OwnedBy(current);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                record.Owner = replacement;
                record.RegistrationGeneration = -1;
                InvalidateAvailability(record);
            }

            DeactivateRecords(records, replacement);
        }

        internal void MarkUnavailable(PresenceSource owner)
        {
            List<Record> records = _ghosts.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }

            DeactivateRecords(records, owner);
        }

        internal void NotifyAnchorDestroyed(Anchor anchor)
        {
            anchor.Dispose();
        }

        internal IReadOnlyList<IGhost> GetOwnedGhosts(PresenceSource owner)
        {
            List<IGhost> result = new List<IGhost>();
            GetOwnedGhosts(owner, result);
            return result;
        }

        internal void GetOwnedGhosts(PresenceSource owner, List<IGhost> result)
        {
            result.Clear();
            foreach (Record record in _ghosts.Values)
            {
                if (record.Owner == owner)
                {
                    result.Add(record.Ghost);
                }
            }
        }

        private void ExecuteDispatch(DispatchItem item)
        {
            if (item.Source != null && !item.Source.IsRegistration(this, item.Generation))
            {
                return;
            }

            string sourceContext = item.Source == null ? "realm" : item.Source.CaptureErrorContext();
            try
            {
                ApplySourceChanges(item.Action);
            }
            catch (Exception exception)
            {
                string context = PresenceSource.DescribeError(sourceContext, "Dispatch");
                if (item.Source == null)
                {
                    PresenceSource.LogError(exception, context);
                }
                else if (item.Source.IsRegistration(this, item.Generation))
                {
                    item.Source.HandleFailure(exception, context);
                }
            }
        }

        private BlueprintSnapshot ResolveBlueprint(string anchorId, Kind kind)
        {
            Anchor anchor;
            BlueprintSnapshot blueprint;
            if (_anchors.TryGetValue(anchorId, out anchor) && anchor.TryGetBlueprint(kind.Id, out blueprint))
            {
                return blueprint;
            }

            _blueprints.TryGet(kind.Id, out blueprint);
            return blueprint;
        }

        private void RebindBlueprints(Kind kind, string anchorId)
        {
            List<Record> records = _ghosts.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (record.Key.Kind != kind || (anchorId != null && record.Key.AnchorId != anchorId))
                {
                    continue;
                }

                // Realm defaults do not replace a specific anchor's configuration.
                Anchor anchor;
                BlueprintSnapshot ignored;
                if (anchorId == null && _anchors.TryGetValue(record.Key.AnchorId, out anchor)
                    && anchor.TryGetBlueprint(kind.Id, out ignored))
                {
                    continue;
                }

                record.Blueprint = ResolveBlueprint(record.Key.AnchorId, kind);
                if (record.ViewRequested)
                {
                    record.ViewVersion++;
                    record.ViewDirty = true;
                }
            }
        }

        private Transform GetAnchorTransform(string anchorId)
        {
            Anchor anchor;
            if (!_anchors.TryGetValue(anchorId, out anchor) || anchor.Transform == null)
            {
                throw new InvalidOperationException("The anchor '" + anchorId + "' does not exist.");
            }

            return anchor.Transform;
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

        private Record FindRecord(IGhost ghost)
        {
            if (ghost == null)
            {
                return null;
            }

            Record record;
            if (!_ghosts.TryGetValue(ghost.Key, out record))
            {
                return null;
            }

            return ReferenceEquals(record.Ghost, ghost) ? record : null;
        }

        private void RefreshView(Record record)
        {
            record.ViewDirty = true;
            if (_sourceDepth == 0 && !_finalizing)
            {
                _views.Refresh(record);
            }
        }

        private void RemoveRecord(Key key, Record record)
        {
            if (!_ghosts.Contains(record))
            {
                return;
            }

            _ghosts.Remove(key);
            InvalidateAvailability(record);
            _views.Destroy(record);
            if (record.Ghost != null)
            {
                _scene.Destroy(record.Ghost.gameObject);
            }
        }

        private void RemoveAnchorGhosts(string anchorId)
        {
            List<Record> records = _ghosts.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (string.Equals(record.Key.AnchorId, anchorId, StringComparison.Ordinal))
                {
                    RemoveRecord(record.Key, record);
                }
            }
        }

        internal void ApplySourceChanges(Action action)
        {
            _sourceDepth++;
            try
            {
                action();
            }
            finally
            {
                _sourceDepth--;
            }
        }

        internal bool IsCurrentGhost(IGhost ghost)
        {
            Ghost component = ghost as Ghost;
            return component != null && FindRecord(ghost) != null;
        }

        internal void DestroySceneObject(GameObject target)
        {
            _scene.Destroy(target);
        }

        internal void NotifyAnchorDisposed(Anchor anchor)
        {
            Anchor current;
            if (_anchors.TryGetValue(anchor.Id, out current) && current == anchor)
            {
                _anchors.Remove(anchor.Id);
                RemoveAnchorGhosts(anchor.Id);
            }
        }

        internal HashSet<Record> CaptureGhosts()
        {
            return new HashSet<Record>(_ghosts.Values);
        }

        internal void RollbackSource(PresenceSource owner, HashSet<Record> previous)
        {
            long generation = owner.RegistrationGeneration;
            List<Record> records = _ghosts.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                // Preserve roots reclaimed by a newer attachment during earlier scene cleanup.
                if (!_ghosts.Contains(record) || record.Owner != owner || record.RegistrationGeneration > generation)
                {
                    continue;
                }

                if (!previous.Contains(record))
                {
                    RemoveRecord(record.Key, record);
                }
                else if (_ghosts.Contains(record) && record.Owner == owner)
                {
                    record.Owner = null;
                    InvalidateAvailability(record);
                    if (record.Ghost != null)
                    {
                        _scene.SetActive(record.Ghost.gameObject, false);
                    }
                }
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

            _subscriptions.Forget(record.Key);
        }

        private void DeactivateRecords(List<Record> records, PresenceSource owner)
        {
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (_ghosts.Contains(record) && record.Owner == owner && record.Ghost != null
                    && !record.Ghost.IsAvailable)
                {
                    _scene.SetActive(record.Ghost.gameObject, false);
                }
            }
        }

        private bool CanFinalize(Record record, PresenceSource onlyOwner)
        {
            return !_disposed && _ghosts.Contains(record) && record.Ghost != null
                && record.Owner != null && (onlyOwner == null || record.Owner == onlyOwner)
                && record.Owner.IsRegistration(this, record.RegistrationGeneration);
        }

        private void FinalizeChanges(PresenceSource onlyOwner)
        {
            _finalizing = true;
            try
            {
                List<Record> records = _ghosts.Snapshot();
                // Phase 1: make initialized roots available and activate them.
                for (int index = 0; index < records.Count; index++)
                {
                    Record record = records[index];
                    if (!CanFinalize(record, onlyOwner) || !record.PendingActivation)
                    {
                        continue;
                    }

                    PresenceSource owner = record.Owner;
                    long generation = record.RegistrationGeneration;
                    long ownership = record.OwnershipVersion;
                    try
                    {
                        record.PendingActivation = false;
                        record.ViewDirty = true;
                        record.Ghost.SetAvailable(true);
                        _scene.SetActive(record.Ghost.gameObject, true);
                        if (!CanFinalize(record, onlyOwner) || record.OwnershipVersion != ownership)
                        {
                            continue;
                        }
                    }
                    catch (Exception exception)
                    {
                        if (owner.IsRegistration(this, generation))
                        {
                            owner.HandleFailure(exception, PresenceSource.DescribeError(owner.CaptureErrorContext(), "Activate", record.Key.Kind, record.Key.EntityId));
                        }
                        else
                        {
                            Debug.LogException(exception);
                        }
                    }
                }

                // Phase 2: all source updates and root activation precede dirty view refresh.
                for (int index = 0; index < records.Count; index++)
                {
                    Record record = records[index];
                    if (!CanFinalize(record, onlyOwner) || !record.Ghost.IsAvailable || !record.ViewDirty)
                    {
                        continue;
                    }

                    _views.Refresh(record);
                }
            }
            finally
            {
                _finalizing = false;
            }
        }

        private static void ValidateDetailLevel(DetailLevel detailLevel)
        {
            if (detailLevel.Level < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(detailLevel), "A detail level cannot be negative.");
            }
        }

        private sealed class DispatchItem
        {
            public DispatchItem(PresenceSource source, long generation, Action action, long sequence)
            {
                Source = source;
                Generation = generation;
                Action = action;
                Sequence = sequence;
            }

            public readonly PresenceSource Source;
            public readonly long Generation;
            public readonly Action Action;
            public readonly long Sequence;
        }
    }
}
