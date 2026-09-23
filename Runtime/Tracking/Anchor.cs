using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Represents one scene coordinate frame and its sources.
    /// </summary>
    /// <remarks>
    /// Owned by its realm; all operations use the Unity thread. Dispose stops sources and removes owned and prepared ghosts.
    /// </remarks>
    public sealed class Anchor : IDisposable
    {
        private readonly List<PresenceSource> _sources = new List<PresenceSource>();
        private readonly HashSet<PresenceSource> _restarting = new HashSet<PresenceSource>();
        private readonly BlueprintRegistry _blueprints = new BlueprintRegistry();
        private readonly GameObject _gameObject;
        private bool _disposed;

        private struct SourceTick
        {
            internal SourceTick(PresenceSource source)
            {
                Source = source;
                Generation = source.RegistrationGeneration;
            }

            internal readonly PresenceSource Source;
            internal readonly long Generation;
        }

        internal Anchor(Realm realm, string id, Transform frame)
        {
            Realm = realm;
            Id = id;
            _gameObject = new GameObject("[Emas Anchor] " + id);
            Transform = _gameObject.transform;
            AnchorLifetime lifetime = _gameObject.AddComponent<AnchorLifetime>();
            lifetime.Initialize(this);
            if (frame != null)
            {
                Transform.SetParent(frame, false);
            }
        }

        /// <summary>
        /// Gets the owning realm.
        /// </summary>
        /// <value>
        /// The realm that owns this anchor.
        /// </value>
        public Realm Realm
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the stable anchor identifier.
        /// </summary>
        /// <value>
        /// The exact anchor identifier.
        /// </value>
        public string Id
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the scene transform for this anchor.
        /// </summary>
        /// <value>
        /// The anchor scene transform.
        /// </value>
        public Transform Transform
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a copied, read-only snapshot of the registered sources, including failed ones.
        /// </summary>
        /// <remarks>
        /// Read on the Unity thread. Earlier snapshots do not change; disposed anchors return an empty snapshot.
        /// </remarks>
        public IReadOnlyList<PresenceSource> Sources
        {
            get
            {
                return new List<PresenceSource>(_sources).AsReadOnly();
            }
        }

        /// <summary>
        /// Registers or replaces a blueprint for this anchor's ghost kind.
        /// </summary>
        /// <param name="blueprint">
        /// The blueprint to use before any realm-wide blueprint for the same kind.
        /// </param>
        /// <remarks>
        /// This registration captures the asset's settings. Re-register after edits to refresh requested views on the next realm update.
        /// Existing ghost roots remain unchanged. Re-register after changing the kind to release the previous kind registration.
        /// The registration is released when this anchor is disposed.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The blueprint or its configuration is invalid.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The anchor or realm was disposed.
        /// </exception>
        public void RegisterBlueprint(Blueprint blueprint)
        {
            ThrowIfDisposed();
            Realm.RegisterBlueprint(this, blueprint);
        }

        /// <summary>
        /// Removes this anchor's blueprint override so the realm default can apply.
        /// </summary>
        /// <param name="kind">
        /// The kind whose override should be removed.
        /// </param>
        /// <remarks>
        /// Missing overrides are ignored. Existing ghost roots remain unchanged; requested views refresh on the next realm update.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The kind is invalid.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The anchor or realm was disposed.
        /// </exception>
        public void UnregisterBlueprint(Kind kind)
        {
            ThrowIfDisposed();
            Realm.UnregisterBlueprint(this, kind);
        }

        internal List<Kind> SetBlueprint(Blueprint blueprint)
        {
            return _blueprints.Register(blueprint);
        }

        internal bool RemoveBlueprint(Kind kind)
        {
            return _blueprints.Remove(kind);
        }

        internal bool TryGetBlueprint(string kindId, out BlueprintSnapshot blueprint)
        {
            return _blueprints.TryGet(kindId, out blueprint);
        }

        internal bool ContainsSource(PresenceSource source)
        {
            return _sources.Contains(source);
        }

        /// <summary>
        /// Adds and starts a source.
        /// </summary>
        /// <param name="source">
        /// The source to add.
        /// </param>
        /// <remarks>
        /// Already present on this anchor is a no-op. Startup failure rolls back new ghosts, detaches the source and rethrows the primary error.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The source is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source belongs to another anchor, or startup rejects its configuration.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The anchor or realm was disposed.
        /// </exception>
        public void AddSource(PresenceSource source)
        {
            ThrowIfDisposed();
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (_sources.Contains(source))
            {
                return;
            }

            if (source.IsAttached)
            {
                throw new InvalidOperationException("The source is already attached to an anchor.");
            }

            HashSet<Record> previous = Realm.CaptureGhosts();
            _sources.Add(source);
            long generation = source.RegistrationGeneration + 1;
            try
            {
                source.Attach(this);
            }
            catch
            {
                // Startup callbacks may remove and reattach the same instance before throwing.
                if (source.RegistrationGeneration == generation)
                {
                    _sources.Remove(source);
                    if (source.IsAttachedTo(this))
                    {
                        source.Detach();
                        Realm.RollbackSource(source, previous);
                    }
                }

                throw;
            }
        }

        internal void RollbackAddedSource(PresenceSource source, HashSet<Record> previous)
        {
            if (!_sources.Remove(source) || !source.IsAttachedTo(this))
            {
                return;
            }

            long generation = source.RegistrationGeneration;
            source.Detach();
            if (source.RegistrationGeneration == generation && !source.IsAttached)
            {
                // Keep records that were prepared before this batch instead of destroying them.
                Realm.RollbackSource(source, previous);
            }
        }

        /// <summary>
        /// Stops and removes a source and its owned ghosts.
        /// </summary>
        /// <param name="source">
        /// The source to remove.
        /// </param>
        /// <remarks>
        /// Null and unknown sources are ignored. Cleanup errors are logged and retained in LastError; removal still completes.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The anchor or realm was disposed.
        /// </exception>
        public void RemoveSource(PresenceSource source)
        {
            ThrowIfDisposed();
            if (source == null || !_sources.Remove(source))
            {
                return;
            }

            source.Detach();

            Realm.RemoveSourceGhosts(source);
        }

        /// <summary>
        /// Restarts an attached source while retaining its ghost identities and view requests.
        /// </summary>
        /// <param name="source">
        /// The active or failed source to restart with its current configuration.
        /// </param>
        /// <remarks>
        /// Stops the old registration once and discards its queued work. Retained ghosts become unavailable until republished.
        /// Startup errors propagate and leave the source attached with unavailable ghosts for another retry.
        /// Callback sources retain unreported identities; polling removes identities absent from a successful complete read.
        /// Cleanup errors are logged; the new attachment clears LastError and LastErrorContext before startup.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The source is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is absent, is starting or stopping, or is already being restarted.
        /// Source startup can also throw application-specific exceptions.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The anchor or realm was disposed.
        /// </exception>
        public void RestartSource(PresenceSource source)
        {
            ThrowIfDisposed();
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!_sources.Contains(source) || !source.IsAttachedTo(this))
            {
                throw new InvalidOperationException("The source is not attached to this anchor.");
            }

            if (source.IsInLifecycle || !_restarting.Add(source))
            {
                throw new InvalidOperationException("A source cannot restart during startup, cleanup or another restart.");
            }

            long generation = source.RegistrationGeneration;
            try
            {
                source.Detach();
                if (!CanContinueRestart(source, generation))
                {
                    return;
                }

                // Retain roots, but require publication from the new registration before exposing their data.
                Realm.MarkUnavailable(source);
                if (!CanContinueRestart(source, generation))
                {
                    return;
                }

                try
                {
                    source.Attach(this);
                }
                catch
                {
                    if (source.IsAttachedTo(this) && source.RegistrationGeneration == generation + 1)
                    {
                        Realm.MarkUnavailable(source);
                    }

                    throw;
                }
            }
            finally
            {
                _restarting.Remove(source);
            }
        }

        private bool CanContinueRestart(PresenceSource source, long generation)
        {
            return !_disposed && _sources.Contains(source) && !source.IsAttached
                && source.RegistrationGeneration == generation;
        }

        /// <summary>
        /// Replaces one source while preserving its compatible ghosts.
        /// </summary>
        /// <param name="current">
        /// The source being replaced.
        /// </param>
        /// <param name="replacement">
        /// The replacement source.
        /// </param>
        /// <remarks>
        /// Transferred ghosts become unavailable until republished. Failed startup retains the replacement and unavailable identities for recovery.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Either source is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Both arguments refer to the same source.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The current source is absent, replacement is attached, or replacement startup fails.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The anchor or realm was disposed.
        /// </exception>
        public void ReplaceSource(PresenceSource current, PresenceSource replacement)
        {
            ThrowIfDisposed();
            if (current == null || replacement == null)
            {
                throw new ArgumentNullException(current == null ? nameof(current) : nameof(replacement));
            }

            int index = _sources.IndexOf(current);
            if (index < 0)
            {
                throw new InvalidOperationException("The current source is not attached to this anchor.");
            }

            if (current == replacement)
            {
                throw new ArgumentException("The replacement must be a different source.", nameof(replacement));
            }

            if (_sources.Contains(replacement) || replacement.IsAttached)
            {
                throw new InvalidOperationException("The replacement source is already registered with an anchor.");
            }

            _sources[index] = replacement;
            current.Detach();

            if (_disposed || !_sources.Contains(replacement))
            {
                Realm.RemoveSourceGhosts(current);
                return;
            }

            // Transfer existing roots; the replacement makes them available as it republishes.
            Realm.TransferSource(current, replacement);
            if (_disposed || !_sources.Contains(replacement))
            {
                Realm.RemoveSourceGhosts(replacement);
                return;
            }

            long generation = replacement.RegistrationGeneration + 1;
            try
            {
                replacement.Attach(this);
            }
            catch
            {
                // Preserve transferred identities, but never expose partially initialized data.
                if (replacement.IsAttachedTo(this) && replacement.RegistrationGeneration == generation)
                {
                    Realm.MarkUnavailable(replacement);
                }

                throw;
            }
        }

        /// <summary>
        /// Stops the anchor and destroys its scene objects.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _blueprints.Clear();
            List<PresenceSource> sources = new List<PresenceSource>(_sources);
            _sources.Clear();
            // Remove registration and records before scene callbacks can reenter the realm.
            Realm.NotifyAnchorDisposed(this);
            for (int index = sources.Count - 1; index >= 0; index--)
            {
                sources[index].Detach();

                Realm.RemoveSourceGhosts(sources[index]);
            }

            if (_gameObject != null)
            {
                Realm.DestroySceneObject(_gameObject);
            }
        }

        internal void ThrowIfDisposed()
        {
            Realm.ThrowIfDisposed();
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Anchor));
            }
        }

        private sealed class AnchorLifetime : MonoBehaviour
        {
            private Anchor _anchor;

            internal void Initialize(Anchor anchor)
            {
                _anchor = anchor;
            }

            private void OnDestroy()
            {
                Anchor anchor = _anchor;
                _anchor = null;
                if (anchor != null)
                {
                    anchor.Realm.NotifyAnchorDestroyed(anchor);
                }
            }
        }

        internal IReadOnlyList<IGhost> GetOwnedGhosts(PresenceSource source)
        {
            return Realm.GetOwnedGhosts(source);
        }

        internal void Tick()
        {
            if (_disposed)
            {
                return;
            }

            List<SourceTick> sources = new List<SourceTick>(_sources.Count);
            for (int index = 0; index < _sources.Count; index++)
            {
                sources.Add(new SourceTick(_sources[index]));
            }

            for (int index = 0; index < sources.Count; index++)
            {
                if (_disposed)
                {
                    break;
                }

                SourceTick current = sources[index];
                if (_sources.Contains(current.Source) && current.Source.RegistrationGeneration == current.Generation)
                {
                    current.Source.Tick();
                }
            }
        }
    }
}
