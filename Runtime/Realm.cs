using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Owns origins, ghosts, blueprints, views and query subscriptions.</summary>
    public sealed class Realm : IDisposable
    {
        /// <summary>Gets the shared realm advanced automatically by Unity.</summary>
        /// <value>The live default realm; a disposed default is recreated on access.</value>
        public static Realm Default
        {
            get { return DefaultRuntime.Realm; }
        }

        private readonly Dictionary<string, Origin> _origins = new Dictionary<string, Origin>();
        private readonly Registry _ghosts = new Registry();
        private readonly Dictionary<string, Blueprint> _blueprints = new Dictionary<string, Blueprint>(StringComparer.Ordinal);
        private readonly Subscriptions _subscriptions;
        private readonly ViewManager _views;
        private readonly SceneEffects _scene = new SceneEffects();
        private readonly Queue<DispatchItem> _dispatch = new Queue<DispatchItem>();
        private readonly object _dispatchLock = new object();
        // Deterministic action budget: newly queued work waits for the following update.
        internal const int MaxDispatchActionsPerUpdate = 256;
        private int _sourceDepth;
        private bool _finalizing;
        private bool _updating;
        private bool _disposed;

        /// <summary>Creates an isolated realm advanced explicitly with Update.</summary>
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

        internal bool ContainsOrigin(string id)
        {
            return _origins.ContainsKey(id);
        }

        /// <summary>Registers a blueprint by kind.</summary>
        /// <param name="blueprint">The blueprint to register.</param>
        public void RegisterBlueprint(Blueprint blueprint)
        {
            ThrowIfDisposed();
            if (blueprint == null || !blueprint.Kind.IsValid)
            {
                throw new ArgumentException("A valid blueprint is required.", nameof(blueprint));
            }

            _blueprints[blueprint.Kind.Id] = blueprint;
        }

        /// <summary>Creates or returns an origin at the scene origin.</summary>
        /// <param name="id">The origin identifier.</param>
        /// <param name="coordinators">The coordinators to attach.</param>
        /// <returns>The existing or new origin.</returns>
        public Origin CreateOriginFor(string id, params Coordinator[] coordinators)
        {
            return CreateOriginFor(id, null, coordinators);
        }

        /// <summary>Creates or returns an origin under a scene frame.</summary>
        /// <param name="id">The origin identifier.</param>
        /// <param name="frame">The optional parent transform.</param>
        /// <param name="coordinators">The coordinators to attach.</param>
        /// <returns>The existing or new origin.</returns>
        public Origin CreateOriginFor(string id, Transform frame, params Coordinator[] coordinators)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An origin requires an identifier.", nameof(id));
            }

            Origin origin;
            var created = false;
            if (!_origins.TryGetValue(id, out origin))
            {
                origin = new Origin(this, id, frame);
                _origins.Add(id, origin);
                created = true;
            }

            try
            {
                if (origin.Transform.parent != frame)
                {
                    if (frame != null || origin.Transform.parent != null)
                    {
                        throw new InvalidOperationException("The existing origin uses a different coordinate frame.");
                    }
                }

                if (coordinators != null)
                {
                    for (var index = 0; index < coordinators.Length; index++)
                    {
                        origin.AddCoordinator(coordinators[index]);
                    }
                }

                return origin;
            }
            catch
            {
                if (created)
                {
                    origin.Dispose();
                }

                throw;
            }
        }

        /// <summary>Removes and disposes an origin.</summary>
        /// <param name="id">The origin identifier.</param>
        public void RemoveOrigin(string id)
        {
            ThrowIfDisposed();
            Origin origin;
            if (_origins.TryGetValue(id, out origin))
            {
                origin.Dispose();
            }
        }

        /// <summary>Creates a query by partial display name.</summary>
        /// <param name="partialName">The optional case-insensitive partial name.</param>
        /// <returns>The query.</returns>
        public Query Query(string partialName = null)
        {
            return new Query(this, partialName);
        }

        /// <summary>Uses an existing immutable query description in this realm.</summary>
        /// <param name="description">The query description.</param>
        /// <returns>A query bound to this realm.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the description is null.</exception>
        public Query Query(Query description)
        {
            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            return description.Rebind(this);
        }

        /// <summary>Prepares an unavailable typed ghost before source discovery.</summary>
        /// <typeparam name="TGhost">The ghost component type.</typeparam>
        /// <param name="originId">The origin identifier.</param>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="entityId">The source entity identifier.</param>
        /// <param name="variant">The appearance; null retains an existing value, and None clears it.</param>
        /// <returns>The prepared ghost.</returns>
        public TGhost Prepare<TGhost>(string originId, Kind kind, string entityId, Variant? variant = null) where TGhost : Ghost
        {
            return GetOrCreate<TGhost>(null, originId, entityId, kind, variant, null);
        }

        /// <summary>Requests a full-degree view when no view request exists, or refreshes the existing request.</summary>
        /// <param name="ghost">The ghost.</param>
        /// <returns>The view component, or null when no prefab resolves.</returns>
        public View Manifest(IGhost ghost)
        {
            ThrowIfDisposed();
            var record = FindRecord(ghost);
            if (record != null && record.ViewRequested)
            {
                RefreshView(record);
                return record.View;
            }

            return Manifest(ghost, DetailLevel.Full);
        }

        /// <summary>Requests a view at a specific degree.</summary>
        /// <remarks>Requests made during source mutation or finalization are refreshed after source data is complete.</remarks>
        /// <param name="ghost">The ghost.</param>
        /// <param name="degree">The desired degree.</param>
        /// <returns>The view component, or null when no prefab resolves.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is negative.</exception>
        public View Manifest(IGhost ghost, DetailLevel degree)
        {
            ThrowIfDisposed();
            ValidateDegree(degree);
            var record = FindRecord(ghost);
            if (record == null)
            {
                return null;
            }

            record.ViewVersion++;
            record.ViewDirty = true;
            record.RequestedDegree = degree;
            record.ViewRequested = degree.Level > 0;
            if (!record.ViewRequested)
            {
                _views.Destroy(record);
                return null;
            }

            RefreshView(record);
            return record.View;
        }

        /// <summary>Removes a ghost's view while retaining its ghost.</summary>
        /// <param name="ghost">The ghost to demanifest.</param>
        public void Demanifest(IGhost ghost)
        {
            ThrowIfDisposed();
            var record = FindRecord(ghost);
            if (record == null)
            {
                return;
            }

            record.ViewVersion++;
            record.ViewDirty = false;
            record.ViewRequested = false;
            record.RequestedDegree = DetailLevel.None;
            _views.Destroy(record);
        }

        /// <summary>Changes the requested view degree.</summary>
        /// <param name="ghost">The ghost whose view should change.</param>
        /// <param name="degree">The desired degree.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is negative.</exception>
        public void SetDegree(IGhost ghost, DetailLevel degree)
        {
            ThrowIfDisposed();
            ValidateDegree(degree);
            var record = FindRecord(ghost);
            if (record == null)
            {
                return;
            }

            record.ViewVersion++;
            record.ViewDirty = true;
            record.RequestedDegree = degree;
            if (degree.Level <= 0)
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

        /// <summary>Applies a bounded source batch, finalizes availability, refreshes views, then notifies subscribers.</summary>
        /// <remarks>Processes at most 256 queued actions present at update entry. Newly queued actions wait for a later update. A disposed realm does nothing.</remarks>
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
                int dispatchCount;
                lock (_dispatchLock)
                {
                    dispatchCount = Math.Min(_dispatch.Count, MaxDispatchActionsPerUpdate);
                }
                for (var index = 0; index < dispatchCount && !_disposed; index++)
                {
                    DispatchItem item;
                    if (!TryDequeue(out item))
                    {
                        break;
                    }
                    ExecuteDispatch(item);
                }
                var origins = new List<Origin>(_origins.Values);
                for (var index = 0; index < origins.Count && !_disposed; index++)
                {
                    var origin = origins[index];
                    Origin current;
                    if (!_origins.TryGetValue(origin.Id, out current) || current != origin)
                    {
                        continue;
                    }
                    if (origin.Transform == null)
                    {
                        origin.Dispose();
                        continue;
                    }
                    origin.Tick();
                }
                if (!_disposed)
                {
                    FinalizeChanges(null);
                    _subscriptions.NotifyAll();
                }
            }
            finally
            {
                _updating = false;
            }
        }

        /// <summary>Disposes all origins, ghosts, views and subscriptions.</summary>
        /// <remarks>Repeated disposal is safe. Subsequent mutating operations throw ObjectDisposedException.</remarks>
        public void Dispose()
        {
            lock (_dispatchLock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _dispatch.Clear();
            }
            _subscriptions.Clear();
            var origins = new List<Origin>(_origins.Values);
            for (var index = 0; index < origins.Count; index++)
            {
                origins[index].Dispose();
            }
            var records = _ghosts.Snapshot();
            for (var index = 0; index < records.Count; index++)
            {
                RemoveRecord(records[index].Key, records[index]);
            }
            _blueprints.Clear();
        }

        internal List<IGhost> Evaluate(Query query)
        {
            var result = new List<IGhost>();
            foreach (var record in _ghosts.Values)
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

        internal void Dispatch(Coordinator coordinator, long generation, Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (_dispatchLock)
            {
                if (_disposed)
                {
                    return;
                }

                _dispatch.Enqueue(new DispatchItem(
                    coordinator,
                    generation,
                    action));
            }
        }

        internal TGhost GetOrCreate<TGhost>(
            Coordinator owner,
            string originId,
            string entityId,
            Kind kind,
            Variant? variant,
            string nameValue) where TGhost : Ghost
        {
            ThrowIfDisposed();
            if (owner != null && !owner.IsActive)
            {
                throw new InvalidOperationException("A stopped coordinator cannot publish ghosts.");
            }
            if (!kind.IsValid || string.IsNullOrEmpty(originId) || string.IsNullOrEmpty(entityId))
            {
                throw new ArgumentException("Origin, kind and entity identifiers are required.");
            }

            var key = new Key(originId, kind, entityId);
            Record record;
            if (_ghosts.TryGetValue(key, out record))
            {
                var existingTyped = RequireGhost<TGhost>(record.Ghost);
                if (owner != null && record.Owner != null && record.Owner != owner)
                {
                    throw new InvalidOperationException("The ghost is owned by another coordinator.");
                }

                if (record.Blueprint == null)
                {
                    _blueprints.TryGetValue(kind.Id, out record.Blueprint);
                }

                var variantChanged = variant.HasValue && record.Ghost.Variant != variant.Value;
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
            Transform originTransform = GetOriginTransform(originId);
            var staging = new GameObject("[Emas Staging]");
            staging.transform.SetParent(originTransform, false);
            staging.SetActive(false);

            TGhost typed;
            try
            {
                if (prefab == null)
                {
                    var gameObject = new GameObject("[Ghost] " + entityId);
                    gameObject.transform.SetParent(staging.transform, false);
                    typed = gameObject.AddComponent<TGhost>();
                }
                else
                {
                    var clone = UnityEngine.Object.Instantiate(prefab, staging.transform, false);
                    typed = clone.GetComponent<TGhost>();
                    if (typed == null)
                    {
                        throw new InvalidOperationException("Blueprint for kind " + kind.Id + " prefab " + prefab.name + " does not contain required component " + typeof(TGhost).FullName + ".");
                    }
                }

                typed.gameObject.SetActive(false);
                typed.Initialize(key, nameValue ?? entityId, variant ?? Variant.None);
                typed.transform.SetParent(originTransform, false);
                typed.gameObject.SetActive(false);
            }
            catch
            {
                UnityEngine.Object.Destroy(staging);
                throw;
            }

            UnityEngine.Object.Destroy(staging);
            typed.SetAvailable(false);
            var newRecord = new Record(typed, owner, blueprint);
            newRecord.PendingActivation = owner != null;
            newRecord.ViewDirty = true;
            _ghosts.Add(key, newRecord);
            return typed;
        }
        internal void FinalizeCoordinator(Coordinator owner)
        {
            if (!_updating && _sourceDepth == 0 && !_finalizing && !_disposed)
            {
                FinalizeChanges(owner);
            }
        }

        internal void RemoveGhost(Coordinator owner, Key key)
        {
            Record record;
            if (!_ghosts.TryGetValue(key, out record) || record.Owner != owner)
            {
                return;
            }

            RemoveRecord(key, record);
        }

        internal void RemoveCoordinatorGhosts(Coordinator owner)
        {
            var records = _ghosts.OwnedBy(owner);
            for (var index = 0; index < records.Count; index++)
            {
                if (records[index].Owner == owner)
                {
                    RemoveRecord(records[index].Key, records[index]);
                }
            }
        }

        internal void TransferCoordinator(Coordinator current, Coordinator replacement)
        {
            var records = _ghosts.OwnedBy(current);
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                record.Owner = replacement;
                record.RegistrationGeneration = -1;
                InvalidateAvailability(record);
            }
            DeactivateRecords(records, replacement);
        }

        internal void MarkUnavailable(Coordinator owner)
        {
            var records = _ghosts.OwnedBy(owner);
            for (var index = 0; index < records.Count; index++)
            {
                InvalidateAvailability(records[index]);
            }
            DeactivateRecords(records, owner);
        }

        internal void NotifyOriginDestroyed(Origin origin)
        {
            origin.Dispose();
        }
        internal IReadOnlyList<IGhost> GetOwnedGhosts(Coordinator owner)
        {
            var result = new List<IGhost>();
            foreach (var record in _ghosts.Values)
            {
                if (record.Owner == owner)
                {
                    result.Add(record.Ghost);
                }
            }

            return result;
        }

        private bool TryDequeue(out DispatchItem item)
        {
            lock (_dispatchLock)
            {
                if (_dispatch.Count == 0)
                {
                    item = null;
                    return false;
                }

                item = _dispatch.Dequeue();
                return true;
            }
        }

        private void ExecuteDispatch(DispatchItem item)
        {
            if (item.Coordinator != null && !item.Coordinator.IsRegistration(this, item.Generation))
            {
                return;
            }
            try
            {
                ApplySourceChanges(item.Action);
            }
            catch (Exception exception)
            {
                if (item.Coordinator == null)
                {
                    Debug.LogException(exception);
                }
                else if (item.Coordinator.IsRegistration(this, item.Generation))
                {
                    item.Coordinator.HandleFailure(exception);
                }
            }
        }
        private Transform GetOriginTransform(string originId)
        {
            Origin origin;
            if (!_origins.TryGetValue(originId, out origin) || origin.Transform == null)
            {
                throw new InvalidOperationException("The origin '" + originId + "' does not exist.");
            }

            return origin.Transform;
        }

        private TGhost RequireGhost<TGhost>(IGhost ghost) where TGhost : Ghost
        {
            var typed = ghost as TGhost;
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

        private void RemoveOriginGhosts(string originId)
        {
            var records = _ghosts.Snapshot();
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (string.Equals(record.Key.OriginId, originId, StringComparison.Ordinal))
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
            var component = ghost as Ghost;
            return component != null && FindRecord(ghost) != null;
        }

        internal void DestroySceneObject(GameObject target)
        {
            _scene.Destroy(target);
        }

        internal void NotifyOriginDisposed(Origin origin)
        {
            Origin current;
            if (_origins.TryGetValue(origin.Id, out current) && current == origin)
            {
                _origins.Remove(origin.Id);
                RemoveOriginGhosts(origin.Id);
            }
        }

        internal HashSet<Record> CaptureGhosts()
        {
            return new HashSet<Record>(_ghosts.Values);
        }

        internal void RollbackCoordinator(Coordinator owner, HashSet<Record> previous)
        {
            var records = _ghosts.OwnedBy(owner);
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
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

        private void DeactivateRecords(List<Record> records, Coordinator owner)
        {
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (_ghosts.Contains(record) && record.Owner == owner && record.Ghost != null
                    && !record.Ghost.IsAvailable)
                {
                    _scene.SetActive(record.Ghost.gameObject, false);
                }
            }
        }

        private bool CanFinalize(Record record, Coordinator onlyOwner)
        {
            return !_disposed && _ghosts.Contains(record) && record.Ghost != null
                && record.Owner != null && (onlyOwner == null || record.Owner == onlyOwner)
                && record.Owner.IsRegistration(this, record.RegistrationGeneration);
        }

        private void FinalizeChanges(Coordinator onlyOwner)
        {
            _finalizing = true;
            try
            {
                var records = _ghosts.Snapshot();
                // Phase 1: make initialized roots available and activate them.
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    if (!CanFinalize(record, onlyOwner) || !record.PendingActivation)
                    {
                        continue;
                    }
                    var owner = record.Owner;
                    var generation = record.RegistrationGeneration;
                    var ownership = record.OwnershipVersion;
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
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    if (!CanFinalize(record, onlyOwner) || !record.Ghost.IsAvailable || !record.ViewDirty)
                    {
                        continue;
                    }
                    var owner = record.Owner;
                    var generation = record.RegistrationGeneration;
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

        private static void ValidateDegree(DetailLevel degree)
        {
            if (degree.Level < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(degree), "A detail level cannot be negative.");
            }
        }

        private sealed class DispatchItem
        {
            public DispatchItem(Coordinator coordinator, long generation, Action action)
            {
                Coordinator = coordinator;
                Generation = generation;
                Action = action;
            }

            public readonly Coordinator Coordinator;
            public readonly long Generation;
            public readonly Action Action;
        }
    }
}
