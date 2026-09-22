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
        private readonly Dictionary<string, Blueprint> _blueprints = new Dictionary<string, Blueprint>(StringComparer.Ordinal);
        private readonly Subscriptions _subscriptions;
        private readonly ViewManager _views;
        private readonly SceneEffects _scene = new SceneEffects();
        private readonly Queue<DispatchItem> _dispatch = new Queue<DispatchItem>();
        // Deterministic action budget: newly queued work waits for the following update.
        internal const int MaxDispatchActionsPerUpdate = 256;
        private int _sourceDepth;
        private bool _finalizing;
        private bool _updating;
        private bool _disposed;

        /// <summary>
        /// Creates an isolated realm advanced explicitly with Update.
        /// </summary>
        public Realm()
        {
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
        /// Assets remain application-owned. A later registration of the same kind replaces its configuration.
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
            if (blueprint == null)
            {
                throw new ArgumentException("A valid blueprint is required.", nameof(blueprint));
            }

            string error = blueprint.GetConfigurationError();
            if (error != null)
            {
                throw new ArgumentException(error, nameof(blueprint));
            }

            _blueprints[blueprint.Kind.Id] = blueprint;
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
                        anchor.AddSource(sources[index]);
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

                throw;
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
        /// The view component, or null when no prefab resolves.
        /// </returns>
        /// <remarks>
        /// Null, foreign and removed ghosts return null. Detail level None clears the request.
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
        /// The view component, or null when no prefab resolves.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the detail level is negative.
        /// </exception>
        /// <remarks>
        /// Null, foreign and removed ghosts return null. Detail level None clears the request.
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
        /// Applies a bounded source batch, finalizes availability, refreshes views, then notifies subscribers.
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
            foreach (Record record in _ghosts.Values)
            {
                if (query.Matches(record.Ghost))
                {
                    result.Add(record.Ghost);
                }
            }

            return result;
        }

        internal IDisposable Subscribe(Query query, Action<IGhost> callback)
        {
            ThrowIfDisposed();
            return _subscriptions.Subscribe(query, callback, _sourceDepth == 0 && !_finalizing);
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
                action));
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

                if (record.Blueprint == null)
                {
                    _blueprints.TryGetValue(kind.Id, out record.Blueprint);
                }

                bool variantChanged = variant.HasValue && record.Ghost.Variant != variant.Value;
                record.Owner = owner ?? record.Owner;
                if (owner != null)
                {
                    record.RegistrationGeneration = owner.RegistrationGeneration;
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

            Blueprint blueprint;
            _blueprints.TryGetValue(kind.Id, out blueprint);
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
            newRecord.PendingActivation = owner != null;
            newRecord.ViewDirty = true;
            _ghosts.Add(key, newRecord);
            return typed;
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
            List<Record> records = _ghosts.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                if (records[index].Owner == owner)
                {
                    RemoveRecord(records[index].Key, records[index]);
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
            foreach (Record record in _ghosts.Values)
            {
                if (record.Owner == owner)
                {
                    result.Add(record.Ghost);
                }
            }

            return result;
        }

        private void ExecuteDispatch(DispatchItem item)
        {
            if (item.Source != null && !item.Source.IsRegistration(this, item.Generation))
            {
                return;
            }

            try
            {
                ApplySourceChanges(item.Action);
            }
            catch (Exception exception)
            {
                if (item.Source == null)
                {
                    Debug.LogException(exception);
                }
                else if (item.Source.IsRegistration(this, item.Generation))
                {
                    item.Source.HandleFailure(exception);
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
            List<Record> records = _ghosts.OwnedBy(owner);
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
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
                            owner.HandleFailure(exception);
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

                    PresenceSource owner = record.Owner;
                    long generation = record.RegistrationGeneration;
                    try
                    {
                        _views.Refresh(record);
                    }
                    catch (Exception exception)
                    {
                        if (owner.IsRegistration(this, generation))
                        {
                            owner.HandleFailure(exception);
                        }
                        else
                        {
                            Debug.LogException(exception);
                        }
                    }
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
            public DispatchItem(PresenceSource source, long generation, Action action)
            {
                Source = source;
                Generation = generation;
                Action = action;
            }

            public readonly PresenceSource Source;
            public readonly long Generation;
            public readonly Action Action;
        }
    }
}
