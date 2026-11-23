using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Emas
{
    /// <summary>Owns anchors, ghosts, blueprints, views and query subscriptions.</summary>
    /// <remarks>
    /// All operations require Unity's main thread. The application handles SDK threading before calling Emas.
    /// Dispose isolated realms when their owner stops; Unity advances Realm.Default automatically.
    /// </remarks>
    public sealed class Realm : IDisposable
    {
        private readonly struct DispatchCommand
        {
            internal readonly PresenceDetector Source;
            internal readonly long Generation;
            internal readonly Action Action;

            internal DispatchCommand(PresenceDetector source, long generation, Action action)
            {
                Source = source;
                Generation = generation;
                Action = action;
            }
        }

        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("Emas.Update");
        private static readonly ProfilerMarker TraitsMarker = new ProfilerMarker("Emas.Traits");
        private static readonly ProfilerMarker GhostsMarker = new ProfilerMarker("Emas.Ghosts");
        private static readonly ProfilerMarker ProjectionMarker = new ProfilerMarker("Emas.Projection");
        private static readonly ProfilerMarker ViewsMarker = new ProfilerMarker("Emas.Views");
        private static readonly ProfilerMarker QueriesMarker = new ProfilerMarker("Emas.Queries");
        private readonly Dictionary<string, Anchor> _anchors = new Dictionary<string, Anchor>();
        private readonly List<Anchor> _updateAnchors = new List<Anchor>();
        private readonly IdentityMap _identities = new IdentityMap();
        private readonly ManifestationBlueprintRegistry _blueprints = new ManifestationBlueprintRegistry();
        private readonly Subscriptions _subscriptions;
        private readonly Population _population;
        private readonly ViewManager _views;
        private readonly SpatialManager _spatial;
        private ReferenceFrame _referenceFrame;
        private float _interpolationDelay;
        private readonly SceneChangeQueue _sceneChanges = new SceneChangeQueue();
        private readonly CommandQueue<DispatchCommand> _dispatch;
        private readonly Func<long, bool> _hasPendingThrough;
        private const int MaxDispatchActionsPerUpdate = 256;
        private long _updateNumber;
        private int _sourceDepth;
        private bool _finalizing;
        private bool _updating;
        private bool _notifyingUpdateQueries;
        private bool _disposed;

        /// <summary>Gets the shared realm advanced automatically by Unity.</summary>
        /// <value>The live default realm; a disposed default is recreated on access.</value>
        public static Realm Default
        {
            get
            {
                return DefaultRuntime.Realm;
            }
        }

        /// <summary>Gets a copied, read-only snapshot of this realm's anchors.</summary>
        /// <remarks>
        /// Read on the Unity thread. Earlier snapshots do not change; disposed realms return an empty snapshot.
        /// The referenced anchors retain their own lifetimes. No ordering is guaranteed.
        /// </remarks>
        /// <value>A membership snapshot of the live anchors, or an empty snapshot after disposal.</value>
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

        /// <summary>Gets a copied, read-only snapshot of all live tracked Ghost roots in this realm, including unavailable ones.</summary>
        /// <remarks>
        /// Includes prepared, pending, grace-period and handover roots; check IsAvailable before reading current source
        /// data. New snapshots exclude removed or destroyed roots and are empty after disposal. Earlier snapshots keep
        /// their membership, but Unity components can be destroyed. Ordering is unspecified.
        /// </remarks>
        /// <value>A membership snapshot of live roots, regardless of availability or view state.</value>
        public IReadOnlyList<IGhost> Ghosts
        {
            get
            {
                if (_disposed)
                {
                    return Array.AsReadOnly(Array.Empty<IGhost>());
                }

                List<IGhost> result = new List<IGhost>();
                foreach (Record record in _identities.Values)
                {
                    if (record.Ghost != null)
                    {
                        result.Add(record.Ghost);
                    }
                }

                return result.AsReadOnly();
            }
        }

        /// <summary>Gets or sets the optional Cartesian or geographic reference used to project source poses into Unity world space.</summary>
        /// <remarks>
        /// Null uses identity Cartesian projection. Enabled Spatial components opt in; Anchor parenting is retained.
        /// Changes apply on the next update or an explicit Manifest outside query notification. Projection precedes
        /// activation and leaves raw data, activity and query membership unchanged.
        /// </remarks>
        /// <value>The reference frame, or null for identity Cartesian projection.</value>
        /// <exception cref="ObjectDisposedException">A value is assigned after this realm has been disposed.</exception>
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

        /// <summary>Gets or sets the shared playback delay for timestamped positions and orientations.</summary>
        /// <value>A finite, nonnegative delay in seconds; zero, the default, uses immediate presentation.</value>
        /// <remarks>
        /// Timed channels share one delayed SDK timeline; untimed channels remain immediate. Extrapolation fills gaps
        /// beyond recorded poses within its own horizon. Choose a delay covering relative packet jitter; transport
        /// latency is additional. Live edits blend over 0.25 unscaled seconds. Attachments inherit their parent's
        /// presentation.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative, NaN or infinite.</exception>
        /// <exception cref="ObjectDisposedException">This realm has been disposed.</exception>
        public float InterpolationDelay
        {
            get => _interpolationDelay;
            set
            {
                ThrowIfDisposed();
                Smoothing.ValidateTime(value, nameof(value));
                _interpolationDelay = value;
            }
        }

        internal bool IsDisposed
        {
            get
            {
                return _disposed;
            }
        }

        /// <summary>Creates an isolated realm advanced explicitly with Update.</summary>
        public Realm()
            : this(() => Time.realtimeSinceStartupAsDouble)
        {
        }

        internal Realm(Func<double> elapsedSeconds)
        {
            // Set up shared tracking and scene changes before queries can see this Realm.
            _dispatch = new CommandQueue<DispatchCommand>(ExecuteDispatch);
            _hasPendingThrough = _dispatch.HasPendingThrough;
            _subscriptions = new Subscriptions(this);
            _views = new ViewManager(_identities, _sceneChanges);
            _spatial = new SpatialManager(this, _identities);
            _population = new Population(this, _identities, elapsedSeconds, _views, _subscriptions, _sceneChanges);
            RealmRegistry.Register(this);
        }

        /// <summary>Resets SDK-to-local spatial clock alignment and sample ordering after the SDK clock restarts.</summary>
        /// <remarks>Retains raw poses and supplied motion, clears sample timestamps and filter histories.
        /// Call before publishing samples from the restarted clock. Each Realm requires one shared SDK clock.</remarks>
        /// <exception cref="ObjectDisposedException">This realm has been disposed.</exception>
        public void ResetSpatialTime()
        {
            ThrowIfDisposed();
            _spatial.ResetTime(_identities.Snapshot());
        }

        /// <summary>Registers a blueprint by kind.</summary>
        /// <param name="blueprint">The blueprint to register.</param>
        /// <remarks>
        /// Assets remain application-owned. Each kind has one blueprint mapping shared by all anchors in this realm.
        /// Each registration captures the asset's settings. Re-register after edits to refresh requested views on the next update;
        /// existing roots stay intact. Re-register after changing its kind to release the previous kind registration.
        /// </remarks>
        /// <exception cref="ArgumentException">The blueprint is null or its kind/view mappings are invalid.</exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public void RegisterManifestationBlueprint(ManifestationBlueprint blueprint)
        {
            ThrowIfDisposed();
            ValidateManifestationBlueprint(blueprint);
            // Copy the blueprint settings, then refresh views that use them. Keep existing roots.
            List<Kind> staleKinds = _blueprints.Register(blueprint);
            RebindManifestationBlueprintRegistration(staleKinds, blueprint.Kind);
        }

        /// <summary>Creates or returns an anchor at the root scene frame.</summary>
        /// <param name="id">The anchor identifier.</param>
        /// <param name="sources">The sources to attach and start, including on an existing anchor.</param>
        /// <returns>The existing or new anchor.</returns>
        /// <remarks>
        /// On failure, newly attached sources are removed and prepared ghosts are restored; existing sources remain.
        /// A newly created anchor is disposed.
        /// </remarks>
        /// <exception cref="ArgumentException">The anchor ID is empty.</exception>
        /// <exception cref="InvalidOperationException">An existing anchor uses another frame, or source attachment fails.</exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public Anchor GetOrCreateAnchor(string id, params PresenceDetector[] sources)
        {
            return GetOrCreateAnchor(id, null, sources);
        }

        /// <summary>Creates or returns an anchor under a scene frame.</summary>
        /// <param name="id">The anchor identifier.</param>
        /// <param name="frame">The optional parent transform.</param>
        /// <param name="sources">The sources to attach and start, including on an existing anchor.</param>
        /// <returns>The existing or new anchor.</returns>
        /// <remarks>
        /// On failure, newly attached sources are removed and prepared ghosts are restored; existing sources remain.
        /// A newly created anchor is disposed.
        /// </remarks>
        /// <exception cref="ArgumentException">The anchor ID is empty.</exception>
        /// <exception cref="InvalidOperationException">An existing anchor uses another frame, or source attachment fails.</exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public Anchor GetOrCreateAnchor(string id, Transform frame, params PresenceDetector[] sources)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An anchor requires an identifier.", nameof(id));
            }

            // Keep a list of new sources so failed startup can undo only those additions.
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
                // Reusing an Anchor must keep its original scene parent.
                if (anchor.Transform.parent != frame)
                {
                    if (frame != null || anchor.Transform.parent != null)
                    {
                        throw new InvalidOperationException("The existing anchor uses a different coordinate frame.");
                    }
                }

                if (sources != null)
                {
                    // Save the existing roots before a new source can take them over in OnStart.
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
                // Remove a new Anchor completely. For an existing Anchor, undo only the new sources.
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

        /// <summary>Removes and disposes an anchor.</summary>
        /// <param name="id">The anchor identifier.</param>
        /// <remarks>
        /// Unknown IDs are ignored.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The ID is null.</exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public void RemoveAnchor(string id)
        {
            ThrowIfDisposed();
            Anchor anchor;
            if (_anchors.TryGetValue(id, out anchor))
            {
                anchor.Dispose();
            }
        }

        /// <summary>Finds a tracked ghost by its exact identity, including unavailable ghosts.</summary>
        /// <param name="key">The case-sensitive anchor, kind and entity identity.</param>
        /// <param name="ghost">The tracked ghost, or null when no live object exists for this identity.</param>
        /// <returns>True when this realm contains the ghost; false for missing or invalid keys and disposed realms.</returns>
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

            // Find the root even while unavailable, but reject destroyed Unity objects.
            Record record;
            if (!_identities.TryGetValue(key, out record) || record.Ghost == null)
            {
                return false;
            }

            ghost = record.Ghost;
            return true;
        }

        /// <summary>Creates an immutable query description scoped to this Realm's available Ghosts.</summary>
        /// <param name="partialName">The optional case-insensitive display-name substring; null leaves names unrestricted. Entity IDs are separate.</param>
        /// <returns>A reusable description evaluated when counted, selected, enumerated or subscribed to.</returns>
        /// <remarks>Use on Unity's main thread. Does not advance tracking, create roots or request views. A disposed Realm yields empty query results; subscriptions reject disposal.</remarks>
        public Query Query(string partialName = null)
        {
            return new Query(this, partialName);
        }

        /// <summary>Uses an existing immutable query description in this realm.</summary>
        /// <param name="description">The query description.</param>
        /// <returns>A new description bound to this Realm, retaining all filters and leaving the original query and its scope unchanged.</returns>
        /// <remarks>Rebinds descriptions from Query.All or another Realm without changing their existing subscriptions. Query evaluation and notifications require Unity's main thread.</remarks>
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
        /// <param name="anchorId">The anchor identifier.</param>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="entityId">The source entity identifier.</param>
        /// <param name="variant">The appearance; null retains an existing value, and None clears it.</param>
        /// <returns>The prepared ghost.</returns>
        /// <remarks>
        /// The anchor must exist. Preparation never makes a ghost available or assigns a source.
        /// </remarks>
        /// <exception cref="ArgumentException">The kind or entity ID is invalid.</exception>
        /// <exception cref="InvalidOperationException">
        /// The anchor does not exist, another detector owns the identity, or the existing or authored root cannot provide TGhost.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public TGhost Prepare<TGhost>(string anchorId, Kind kind, string entityId, Variant? variant = null) where TGhost : Ghost
        {
            return GetOrCreate<TGhost>(null, anchorId, entityId, kind, variant, null);
        }

        /// <summary>Registers a Ghost root type and binds its configured traits to application data.</summary>
        /// <typeparam name="TGhost">The Ghost component used as the invisible root.</typeparam>
        /// <param name="kind">The kind this initializer handles.</param>
        /// <param name="initialize">Connects the Ghost's traits to their source-independent value readers.</param>
        /// <remarks>
        /// Register before roots of this Kind exist. Bind authored traits using the already assigned Presence.Source.
        /// Initialization repeats on source/capability changes, rediscovery and handover. An Anchor's GhostInitializer
        /// overrides this registration. The callback runs before trait reads and availability.
        /// </remarks>
        /// <exception cref="ArgumentException">The kind is invalid.</exception>
        /// <exception cref="ArgumentNullException">The initializer is null.</exception>
        /// <exception cref="InvalidOperationException">A root of this kind already exists, including a prepared root.</exception>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public void RegisterPresenceInitializer<TGhost>(Kind kind, Action<Presence, TGhost> initialize)
            where TGhost : Ghost
        {
            ThrowIfDisposed();
            _population.RegisterPresenceInitializer(kind, initialize);
        }

        /// <summary>Finds a stable presence by identity, including one that is temporarily unavailable.</summary>
        /// <param name="key">The anchor, kind and entity identity.</param>
        /// <param name="presence">The tracked presence, or null when absent.</param>
        /// <returns>True for a retained presence with a live root; false for absent identities or disposed realms.</returns>
        public bool TryGetPresence(Key key, out Presence presence)
        {
            presence = null;
            return !_disposed && _population.TryGetPresence(key, out presence);
        }

        /// <summary>Requests a manifestation for a detected presence.</summary>
        /// <param name="presence">A presence owned by this realm; null, foreign and removed handles are ignored.</param>
        /// <returns>The current view, or null while no view can be shown.</returns>
        /// <remarks>Uses the same request lifetime and deferred refresh behavior as <see cref="Manifest(IGhost)"/>.</remarks>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public View Manifest(Presence presence)
        {
            return Manifest(_population.FindPresenceRoot(presence));
        }

        /// <summary>Removes the view of a presence while retaining its detection and Ghost root.</summary>
        /// <param name="presence">The presence to demanifest.</param>
        /// <remarks>Null, foreign and removed presences are ignored. Cancels the request until Manifest is called again.</remarks>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public void Demanifest(Presence presence)
        {
            Demanifest(_population.FindPresenceRoot(presence));
        }

        /// <summary>Requests or refreshes the view selected by the ghost's variant.</summary>
        /// <param name="ghost">A tracked root owned by this realm; null, foreign and removed roots are ignored.</param>
        /// <returns>The view component, or null when no view can be created yet.</returns>
        /// <remarks>
        /// Requests persist until Demanifest or removal, including while unavailable or out of range. Mutation-time
        /// requests wait for complete data; query callbacks use the current shared projection. This does not read
        /// traits. Null, foreign or removed roots return null. Presentation failures are logged; call again after fixing
        /// the cause to retry.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public View Manifest(IGhost ghost)
        {
            ThrowIfDisposed();
            Record record = _identities.Find(ghost);
            if (record == null)
            {
                return null;
            }

            // Keep the view request so it can be shown later when its data, range and assets allow it.
            if (!record.ViewRequested)
            {
                _views.Request(record);
            }

            RefreshView(record);
            return record.View;
        }

        /// <summary>Removes a ghost's view while retaining its ghost.</summary>
        /// <param name="ghost">The ghost to demanifest.</param>
        /// <remarks>
        /// Cancels the persistent view request. Null, foreign and removed ghosts are ignored; source availability is unchanged.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The realm was disposed.</exception>
        public void Demanifest(IGhost ghost)
        {
            ThrowIfDisposed();
            Record record = _identities.Find(ghost);
            if (record != null)
            {
                _views.Cancel(record);
            }
        }

        /// <summary>Applies a bounded source batch, refreshes traits and Ghost behavior, projects spatial poses, then finalizes availability, views and queries.</summary>
        /// <remarks>
        /// Processes at most 256 queued actions present at update entry, then ticks attached detectors.
        /// Newly queued actions wait for a later update. Expiry runs after publications, and all trait reads finish
        /// before Ghost.OnUpdate hooks and spatial projection. Availability and views finalize before query notifications.
        /// A disposed realm does nothing.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Called inside another update, source callback or finalization phase.</exception>
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
            UpdateMarker.Begin();
            try
            {
                // Run only work queued before this update. New work waits for the next update.
                _dispatch.ExecutePending(MaxDispatchActionsPerUpdate);

                // Copy the Anchor list because source callbacks may remove Anchors as we go.
                List<Anchor> anchors = _updateAnchors;
                anchors.AddRange(_anchors.Values);
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
                    // Apply fresh reports before checking which Ghosts have gone quiet for too long.
                    _population.RemoveUnpublishedHandoverGhosts(_updateNumber, _hasPendingThrough);
                    _population.RemoveExpiredGhosts();
                    // Finish data and views before telling observers about changes.
                    FinalizeChanges(null, updateGhosts: true);
                    // Arrival callbacks may request views. Use the reference pose already chosen for this update.
                    _notifyingUpdateQueries = true;
                    using (QueriesMarker.Auto())
                    {
                        _subscriptions.NotifyAll();
                    }
                }
            }
            finally
            {
                _updateAnchors.Clear();
                _notifyingUpdateQueries = false;
                _updating = false;
                UpdateMarker.End();
            }
        }

        /// <summary>Disposes all anchors, ghosts, views and subscriptions.</summary>
        /// <remarks>
        /// Repeated disposal is safe. Update becomes a no-op; lookups and query results are empty.
        /// Operations that configure tracking, request views or subscribe throw ObjectDisposedException.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // Reject queued work before source cleanup can call back into this Realm.
            _disposed = true;
            RealmRegistry.Unregister(this);
            _dispatch.Clear();
            _subscriptions.Clear();
            List<Anchor> anchors = new List<Anchor>(_anchors.Values);
            for (int index = 0; index < anchors.Count; index++)
            {
                anchors[index].Dispose();
            }

            // Remove any roots left after Anchor cleanup, then release blueprint and reference settings.
            IReadOnlyList<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                _population.RemoveRecord(records[index]);
            }

            _blueprints.Clear();
            _referenceFrame = null;
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
            // Count matches and keep the first one so Single does not need to build a result list.
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

        internal Presence DetectPresence(PresenceDetector detector, string anchorId, string entityId,
            Kind kind, string name, Variant? variant, IEnumerable<Type> capabilities, object source)
        {
            ThrowIfDisposed();
            if (detector == null || !detector.IsActive)
            {
                throw new InvalidOperationException("Only an active detector can detect a presence.");
            }

            if (!kind.IsValid || string.IsNullOrEmpty(anchorId) || string.IsNullOrEmpty(entityId))
            {
                throw new ArgumentException("Anchor, kind and entity identifiers are required.");
            }

            // Check and copy the supplied capabilities before passing them to a setup callback.
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
            // Finish connecting trait readers before callbacks can use or activate their roots.
            _sourceDepth++;
            try
            {
                return _population.ApplyPresenceDetection(detector, anchorId, entityId, kind, name, variant, capabilitySnapshot, source,
                    GetAnchor(anchorId), ResolveManifestationBlueprint(kind), _anchors[anchorId].InitializeGhost);
            }
            catch (Exception exception)
            {
                string context = PresenceDetector.DescribeError(sourceContext, "Detect", kind, entityId);
                detector.RecordError(exception, generation, context);
                // OnStart, OnUpdate and Dispatch callers handle their own errors and undo failed startup.
                // For a direct detection call, stop the source here before its new roots can activate.
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
                GetAnchor(anchorId), ResolveManifestationBlueprint(kind));
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

        internal void ApplySourceChanges(Action action)
        {
            // Finish all source callbacks, including callbacks started by other callbacks, before updating views.
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

        private void RebindManifestationBlueprintRegistration(List<Kind> staleKinds, Kind kind)
        {
            if (staleKinds != null)
            {
                for (int index = 0; index < staleKinds.Count; index++)
                {
                    RebindManifestationBlueprints(staleKinds[index]);
                }
            }

            RebindManifestationBlueprints(kind);
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

        private void RollbackAnchorSources(Anchor anchor, List<PresenceDetector> sources, HashSet<Record> previous)
        {
            // Undo new sources in reverse order. Cleanup callbacks may remove the Anchor as we go.
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

        private void ExecuteDispatch(DispatchCommand command)
        {
            // Check the source version again when work runs. Work from before a restart is no longer valid.
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

        private ManifestationBlueprintSnapshot ResolveManifestationBlueprint(Kind kind)
        {
            ManifestationBlueprintSnapshot blueprint;
            _blueprints.TryGet(kind.Id, out blueprint);
            return blueprint;
        }

        private void RebindManifestationBlueprints(Kind kind)
        {
            // Copy the root list before view callbacks can change tracking.
            IReadOnlyList<Record> records = _identities.Snapshot();
            for (int index = 0; index < records.Count; index++)
            {
                Record record = records[index];
                if (record.Key.Kind != kind)
                {
                    continue;
                }

                record.ManifestationBlueprint = ResolveManifestationBlueprint(kind);
                if (record.ViewRequested)
                {
                    record.ViewVersion++;
                    record.ViewDirty = true;
                }
            }
        }

        private Anchor GetAnchor(string anchorId)
        {
            Anchor anchor;
            return _anchors.TryGetValue(anchorId, out anchor) ? anchor : null;
        }

        private void RefreshView(Record record)
        {
            record.ViewDirty = true;
            if (_sourceDepth == 0 && !_finalizing)
            {
                // Arrival observers may request several views. Keep the same reference pose for all of them.
                if (!_notifyingUpdateQueries)
                {
                    _spatial.Project(record, _referenceFrame);
                }
                _views.Refresh(record);
                _spatial.RefreshSuppression(record);
            }
        }

        private void FinalizeChanges(PresenceDetector onlyOwner, bool updateGhosts = false)
        {
            _finalizing = true;
            try
            {
                IReadOnlyList<Record> records = _identities.Snapshot();
                // Work on one copy of the root list. New roots wait for the next pass.
                // Read all traits before any Ghost.OnUpdate can use another root's data.
                using (TraitsMarker.Auto())
                {
                    _population.RefreshTraits(records, onlyOwner);
                }
                if (updateGhosts)
                {
                    using (GhostsMarker.Auto())
                    {
                        _population.UpdateGhosts(records);
                    }
                }

                // Place roots using their finished data before activating roots or views.
                using (ProjectionMarker.Auto())
                {
                    _spatial.Project(records, _referenceFrame);
                }
                using (ViewsMarker.Auto())
                {
                    _population.ActivateRoots(records, onlyOwner);
                    // Activation callbacks may cancel or replace a view request. Refresh only requests still needing work.
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
            }
            finally
            {
                _finalizing = false;
            }
        }
    }
}
