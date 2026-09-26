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
        private readonly IdentityMap _identities = new IdentityMap();
        private readonly ManifestationBlueprintRegistry _blueprints = new ManifestationBlueprintRegistry();
        private readonly Subscriptions _subscriptions;
        private readonly Population _population;
        private readonly ViewManager _views;
        private readonly SpatialManager _spatial;
        private ReferenceFrame _referenceFrame;
        private readonly SceneChangeQueue _sceneChanges = new SceneChangeQueue();
        private readonly CommandQueue<DispatchCommand> _dispatch;
        private const int MaxDispatchActionsPerUpdate = 256;
        private long _updateNumber;
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
            _dispatch = new CommandQueue<DispatchCommand>(ExecuteDispatch);
            _subscriptions = new Subscriptions(this);
            _views = new ViewManager(_identities, _sceneChanges);
            _spatial = new SpatialManager(this, _identities);
            _population = new Population(this, _identities, elapsedSeconds, _views, _subscriptions, _sceneChanges);
            RealmRegistry.Register(this);
        }

        /// <summary>
        /// Gets or sets optional projection from shared double-precision Cartesian coordinates into Unity world space.
        /// </summary>
        /// <remarks>
        /// Null, the default, leaves transforms application-controlled. Spatial components opt individual ghosts in.
        /// Changes apply during the next realm update or an explicit Manifest request. Projection runs after source
        /// processing and before root/view activation, and does not change data, source activity or query membership.
        /// Anchor parenting is retained; its transform is compensated when assigning the projected world pose.
        /// </remarks>
        public ReferenceFrame ReferenceFrame
        {
            get
            {
                return _referenceFrame;
            }
            set
            {
                ThrowIfDisposed();
                _referenceFrame = value;
            }
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
        public void RegisterManifestationBlueprint(ManifestationBlueprint blueprint)
        {
            ThrowIfDisposed();
            ValidateManifestationBlueprint(blueprint);
            List<Kind> staleKinds = _blueprints.Register(blueprint);
            RebindManifestationBlueprintRegistration(staleKinds, blueprint.Kind, null);
        }

        internal void RegisterManifestationBlueprint(Anchor anchor, ManifestationBlueprint blueprint)
        {
            ThrowIfDisposed();
            anchor.ThrowIfDisposed();
            ValidateManifestationBlueprint(blueprint);
            List<Kind> staleKinds = anchor.SetManifestationBlueprint(blueprint);
            RebindManifestationBlueprintRegistration(staleKinds, blueprint.Kind, anchor.Id);
        }

        internal void UnregisterManifestationBlueprint(Anchor anchor, Kind kind)
        {
            ThrowIfDisposed();
            anchor.ThrowIfDisposed();
            if (!kind.IsValid)
            {
                throw new ArgumentException("The blueprint kind must be valid.", nameof(kind));
            }

            if (anchor.RemoveManifestationBlueprint(kind))
            {
                RebindManifestationBlueprints(kind, anchor.Id);
            }
        }

        private void RebindManifestationBlueprintRegistration(List<Kind> staleKinds, Kind kind, string anchorId)
        {
            if (staleKinds != null)
            {
                for (int index = 0; index < staleKinds.Count; index++)
                {
                    RebindManifestationBlueprints(staleKinds[index], anchorId);
                }
            }

            RebindManifestationBlueprints(kind, anchorId);
        }

        private static void ValidateManifestationBlueprint(ManifestationBlueprint blueprint)
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
        public Anchor GetOrCreateAnchor(string id, params PresenceDetector[] sources)
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
        public Anchor GetOrCreateAnchor(string id, Transform frame, params PresenceDetector[] sources)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An anchor requires an identifier.", nameof(id));
            }

            Anchor anchor;
            bool created = false;
            List<PresenceDetector> newSources = null;
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
                        PresenceDetector source = sources[index];
                        if (!created && !anchor.ContainsSource(source))
                        {
                            if (newSources == null)
                            {
                                newSources = new List<PresenceDetector>();
                                previousRecords = CaptureGhosts();
                            }

                            newSources.Add(source);
                        }

                        anchor.AddDetector(source);
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

        private void RollbackAnchorSources(Anchor anchor, List<PresenceDetector> sources, HashSet<Record> previous)
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
                    PresenceDetector.LogError(exception, "rolling back source attachment to anchor '" + anchor.Id + "'");
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
            if (!_identities.TryGetValue(key, out record) || record.Ghost == null)
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
        /// Registers the root type and module setup for one detected kind.
        /// </summary>
        /// <typeparam name="TGhost">The Ghost component used as the invisible root.</typeparam>
        /// <param name="kind">The kind this initializer handles.</param>
        /// <param name="initialize">Adds modules and configures a new or newly capable presence.</param>
        /// <remarks>
        /// Register before attaching detectors. The callback runs after root creation and before SDK data is
        /// applied or the Ghost becomes available. It runs again when reported capabilities change; use
        /// TryGetModule to avoid installing a duplicate module.
        /// </remarks>
        public void RegisterPresenceInitializer<TGhost>(Kind kind, Action<Presence, TGhost> initialize)
            where TGhost : Ghost
        {
            ThrowIfDisposed();
            _population.RegisterPresenceInitializer(kind, initialize);
        }

        /// <summary>
        /// Finds a stable presence by identity, including one that is temporarily unavailable.
        /// </summary>
        /// <param name="key">The anchor, kind and entity identity.</param>
        /// <param name="presence">The tracked presence, or null when absent.</param>
        /// <returns>True while the realm retains this presence.</returns>
        public bool TryGetPresence(Key key, out Presence presence)
        {
            presence = null;
            return !_disposed && _population.TryGetPresence(key, out presence);
        }

        /// <summary>
        /// Requests a manifestation for a detected presence at its current or Full detail level.
        /// </summary>
        /// <param name="presence">The presence to manifest.</param>
        /// <returns>The current view, or null while no view can be shown.</returns>
        public View Manifest(Presence presence)
        {
            return Manifest(_population.FindPresenceRoot(presence));
        }

        /// <summary>
        /// Requests a manifestation for a detected presence at a specific detail level.
        /// </summary>
        /// <param name="presence">The presence to manifest.</param>
        /// <param name="detailLevel">The desired detail level.</param>
        /// <returns>The current view, or null while no view can be shown.</returns>
        public View Manifest(Presence presence, DetailLevel detailLevel)
        {
            return Manifest(_population.FindPresenceRoot(presence), detailLevel);
        }

        /// <summary>
        /// Removes the view of a presence while retaining its detection and Ghost root.
        /// </summary>
        /// <param name="presence">The presence to demanifest.</param>
        public void Demanifest(Presence presence)
        {
            Demanifest(_population.FindPresenceRoot(presence));
        }

        /// <summary>
        /// Changes the requested detail level of a presence's view.
        /// </summary>
        /// <param name="presence">The presence whose view should change.</param>
        /// <param name="detailLevel">The desired detail level.</param>
        public void SetDetailLevel(Presence presence, DetailLevel detailLevel)
        {
            SetDetailLevel(_population.FindPresenceRoot(presence), detailLevel);
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
            Record record = _identities.Find(ghost);
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
            ViewManager.ValidateDetailLevel(detailLevel);
            Record record = _identities.Find(ghost);
            if (record == null)
            {
                return null;
            }

            _views.Request(record, detailLevel);
            if (detailLevel.Level <= 0)
            {
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
            Record record = _identities.Find(ghost);
            if (record != null)
            {
                _views.Cancel(record);
            }
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
            ViewManager.ValidateDetailLevel(detailLevel);
            Record record = _identities.Find(ghost);
            if (record != null && _views.SetDetailLevel(record, detailLevel))
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
                _dispatch.ExecutePending(MaxDispatchActionsPerUpdate);

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
                    _population.RemoveUnpublishedHandoverGhosts(_updateNumber, _dispatch.HasPendingThrough);
                    _population.RemoveExpiredGhosts();
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
            RealmRegistry.Unregister(this);
            _dispatch.Clear();
            _subscriptions.Clear();
            List<Anchor> anchors = new List<Anchor>(_anchors.Values);
            for (int index = 0; index < anchors.Count; index++)
            {
                anchors[index].Dispose();
            }

            List<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                _population.RemoveRecord(records[index]);
            }

            _blueprints.Clear();
            _referenceFrame = null;
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
            if (_disposed)
            {
                return;
            }

            foreach (Record record in _identities.Values)
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
            if (_disposed)
            {
                return 0;
            }

            int count = 0;
            foreach (Record record in _identities.Values)
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
            if (_disposed)
            {
                return null;
            }

            foreach (Record record in _identities.Values)
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

        internal void Dispatch(PresenceDetector source, long generation, Action action)
        {
            if (action == null || _disposed)
            {
                return;
            }

            _dispatch.Enqueue(new DispatchCommand(source, generation, action));
        }

        private void ExecuteDispatch(DispatchCommand command)
        {
            if (_disposed
                || (command.Source != null && !command.Source.IsRegistration(this, command.Generation)))
            {
                return;
            }

            string sourceContext = command.Source == null ? "realm" : command.Source.CaptureErrorContext();
            try
            {
                if (command.Source == null)
                {
                    ApplySourceChanges(command.Action);
                }
                else
                {
                    command.Source.ApplySourceChanges(this, command.Action);
                }
            }
            catch (Exception exception)
            {
                string context = PresenceDetector.DescribeError(sourceContext, "Dispatch");
                if (command.Source == null)
                {
                    PresenceDetector.LogError(exception, context);
                }
                else if (command.Source.IsRegistration(this, command.Generation))
                {
                    command.Source.HandleFailure(exception, context);
                }
            }
        }

        internal Presence ReportPresence(PresenceDetector detector, string anchorId, string entityId,
            Kind kind, string name, Variant? variant, IEnumerable<Type> capabilities, object data)
        {
            ThrowIfDisposed();
            if (detector == null || !detector.IsActive)
            {
                throw new InvalidOperationException("Only an active detector can report a presence.");
            }

            if (!kind.IsValid || string.IsNullOrEmpty(anchorId) || string.IsNullOrEmpty(entityId))
            {
                throw new ArgumentException("Anchor, kind and entity identifiers are required.");
            }

            List<Type> capabilitySnapshot = null;
            if (capabilities != null)
            {
                capabilitySnapshot = new List<Type>();
                foreach (Type capability in capabilities)
                {
                    if (capability == null || !capability.IsInterface)
                    {
                        throw new ArgumentException("Presence capabilities must be interface types.", nameof(capabilities));
                    }

                    if (!capabilitySnapshot.Contains(capability))
                    {
                        capabilitySnapshot.Add(capability);
                    }
                }
            }

            long generation = detector.RegistrationGeneration;
            string sourceContext = detector.CaptureErrorContext();
            // Initializers and modules must finish before callbacks can observe or activate their roots.
            _sourceDepth++;
            try
            {
                return _population.ApplyPresenceReport(detector, anchorId, entityId, kind, name, variant, capabilitySnapshot, data,
                    GetAnchorTransform(anchorId), ResolveManifestationBlueprint(anchorId, kind));
            }
            catch (Exception exception)
            {
                string context = PresenceDetector.DescribeError(sourceContext, "Report", kind, entityId);
                detector.RecordError(exception, generation, context);
                // Lifecycle and dispatched reports already have a failure boundary. Preserve startup rollback
                // and let that boundary log once, while direct reports stop before pending roots can activate.
                if (!detector.IsApplyingSourceChanges && detector.IsRegistration(this, generation))
                {
                    detector.HandleFailure(exception, context);
                }

                throw;
            }
            finally
            {
                _sourceDepth--;
            }
        }

        internal IReadOnlyList<Presence> GetOwnedPresences(PresenceDetector detector)
        {
            return _population.GetOwnedPresences(detector);
        }

        internal TGhost GetOrCreate<TGhost>(
            PresenceDetector owner,
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

            return _population.GetOrCreate<TGhost>(owner, anchorId, entityId, kind, variant, nameValue,
                GetAnchorTransform(anchorId), ResolveManifestationBlueprint(anchorId, kind));
        }

        internal void MarkPublished(PresenceDetector owner, IGhost ghost)
        {
            ThrowIfDisposed();
            _population.MarkPublished(owner, ghost);
        }

        internal void FinalizeSource(PresenceDetector owner)
        {
            if (!_updating && _sourceDepth == 0 && !_finalizing && !_disposed)
            {
                FinalizeChanges(owner);
            }
        }

        internal void RemoveGhost(PresenceDetector owner, Key key)
        {
            _population.RemoveGhost(owner, key);
        }

        internal void RemoveSourceGhosts(PresenceDetector owner)
        {
            _population.RemoveSourceGhosts(owner);
        }

        internal void CompleteSourceHandover(PresenceDetector owner)
        {
            _population.CompleteSourceHandover(owner, _updateNumber, _dispatch.LastSequence);
        }

        internal void TransferSource(PresenceDetector current, PresenceDetector replacement)
        {
            _population.TransferSource(current, replacement);
        }

        internal void MarkUnavailable(PresenceDetector owner)
        {
            _population.MarkUnavailable(owner);
        }

        internal void NotifyAnchorDestroyed(Anchor anchor)
        {
            anchor.Dispose();
        }

        internal IReadOnlyList<IGhost> GetOwnedGhosts(PresenceDetector owner)
        {
            return _population.GetOwnedGhosts(owner);
        }

        private ManifestationBlueprintSnapshot ResolveManifestationBlueprint(string anchorId, Kind kind)
        {
            Anchor anchor;
            ManifestationBlueprintSnapshot blueprint;
            if (_anchors.TryGetValue(anchorId, out anchor) && anchor.TryGetManifestationBlueprint(kind.Id, out blueprint))
            {
                return blueprint;
            }

            _blueprints.TryGet(kind.Id, out blueprint);
            return blueprint;
        }

        private void RebindManifestationBlueprints(Kind kind, string anchorId)
        {
            List<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (record.Key.Kind != kind || (anchorId != null && record.Key.AnchorId != anchorId))
                {
                    continue;
                }

                // Realm defaults do not replace a specific anchor's configuration.
                Anchor anchor;
                ManifestationBlueprintSnapshot ignored;
                if (anchorId == null && _anchors.TryGetValue(record.Key.AnchorId, out anchor)
                    && anchor.TryGetManifestationBlueprint(kind.Id, out ignored))
                {
                    continue;
                }

                record.ManifestationBlueprint = ResolveManifestationBlueprint(record.Key.AnchorId, kind);
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
            return _anchors.TryGetValue(anchorId, out anchor) ? anchor.Transform : null;
        }

        private void RefreshView(Record record)
        {
            record.ViewDirty = true;
            if (_sourceDepth == 0 && !_finalizing)
            {
                _spatial.Project(record, _referenceFrame);
                _views.Refresh(record);
                _spatial.RefreshSuppression(record);
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
            return component != null && _identities.Find(ghost) != null;
        }

        internal void DestroySceneObject(GameObject target)
        {
            _sceneChanges.Destroy(target);
        }

        internal void NotifyAnchorDisposed(Anchor anchor)
        {
            Anchor current;
            if (_anchors.TryGetValue(anchor.Id, out current) && current == anchor)
            {
                _anchors.Remove(anchor.Id);
                _population.RemoveAnchorGhosts(anchor.Id);
            }
        }

        internal HashSet<Record> CaptureGhosts()
        {
            return _population.CaptureGhosts();
        }

        internal void RollbackSource(PresenceDetector owner, HashSet<Record> previous)
        {
            _population.RollbackSource(owner, previous);
        }

        private void FinalizeChanges(PresenceDetector onlyOwner)
        {
            _finalizing = true;
            try
            {
                List<Record> records = _identities.Snapshot();
                // Project complete source data before activating any roots or views.
                _spatial.Project(records, _referenceFrame);
                _population.ActivateRoots(records, onlyOwner);
                for (int index = 0; index < records.Count; index++)
                {
                    Record record = records[index];
                    if (!_population.CanFinalize(record, onlyOwner) || !record.Ghost.IsAvailable || !record.ViewDirty)
                    {
                        continue;
                    }

                    _views.Refresh(record);
                }

                _spatial.RefreshSuppression(records);
            }
            finally
            {
                _finalizing = false;
            }
        }

        private readonly struct DispatchCommand
        {
            internal DispatchCommand(PresenceDetector source, long generation, Action action)
            {
                Source = source;
                Generation = generation;
                Action = action;
            }

            internal readonly PresenceDetector Source;
            internal readonly long Generation;
            internal readonly Action Action;
        }
    }
}
