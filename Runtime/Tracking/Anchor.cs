using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Represents one scene coordinate frame and its sources.</summary>
    /// <remarks>Owned by its realm; all operations use the Unity thread. Dispose stops sources and removes owned and prepared ghosts.</remarks>
    public sealed class Anchor : IDisposable
    {
        private readonly List<PresenceSource> _sources = new List<PresenceSource>();
        private readonly GameObject _gameObject;
        private bool _disposed;

        internal Anchor(Realm realm, string id, Transform frame)
        {
            Realm = realm;
            Id = id;
            _gameObject = new GameObject("[Emas Anchor] " + id);
            Transform = _gameObject.transform;
            var lifetime = _gameObject.AddComponent<AnchorLifetime>();
            lifetime.Initialize(this);
            if (frame != null)
            {
                Transform.SetParent(frame, false);
            }
        }

        /// <summary>Gets the owning realm.</summary>
        /// <value>The realm that owns this anchor.</value>
        public Realm Realm { get; private set; }

        /// <summary>Gets the stable anchor identifier.</summary>
        /// <value>The exact anchor identifier.</value>
        public string Id { get; private set; }

        /// <summary>Gets the scene transform for this anchor.</summary>
        /// <value>The anchor scene transform.</value>
        public Transform Transform { get; private set; }

        /// <summary>Gets a copied, read-only snapshot of the registered sources, including failed ones.</summary>
        /// <remarks>Read on the Unity thread. Earlier snapshots do not change; disposed anchors return an empty snapshot.</remarks>
        public IReadOnlyList<PresenceSource> Sources
        {
            get { return new List<PresenceSource>(_sources).AsReadOnly(); }
        }

        /// <summary>Adds and starts a source.</summary>
        /// <param name="source">The source to add.</param>
        /// <remarks>Already present on this anchor is a no-op. Startup failure rolls back new ghosts, detaches the source and rethrows the primary error.</remarks>
        /// <exception cref="ArgumentNullException">The source is null.</exception>
        /// <exception cref="InvalidOperationException">The source belongs to another anchor, or startup rejects its configuration.</exception>
        /// <exception cref="ObjectDisposedException">The anchor or realm was disposed.</exception>
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
            var previous = Realm.CaptureGhosts();
            _sources.Add(source);
            var generation = source.RegistrationGeneration + 1;
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

        /// <summary>Stops and removes a source and its owned ghosts.</summary>
        /// <param name="source">The source to remove.</param>
        /// <remarks>Null and unknown sources are ignored. Cleanup errors are logged and retained in LastError; removal still completes.</remarks>
        /// <exception cref="ObjectDisposedException">The anchor or realm was disposed.</exception>
        public void RemoveSource(PresenceSource source)
        {
            ThrowIfDisposed();
            if (source == null || !_sources.Remove(source))
            {
                return;
            }

            try
            {
                source.Detach();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            Realm.RemoveSourceGhosts(source);
        }

        /// <summary>Replaces one source while preserving its compatible ghosts.</summary>
        /// <param name="current">The source being replaced.</param>
        /// <param name="replacement">The replacement source.</param>
        /// <remarks>Transferred ghosts become unavailable until republished. Failed startup retains the replacement and unavailable identities for recovery.</remarks>
        /// <exception cref="ArgumentNullException">Either source is null.</exception>
        /// <exception cref="ArgumentException">Both arguments refer to the same source.</exception>
        /// <exception cref="InvalidOperationException">The current source is absent, replacement is attached, or replacement startup fails.</exception>
        /// <exception cref="ObjectDisposedException">The anchor or realm was disposed.</exception>
        public void ReplaceSource(PresenceSource current, PresenceSource replacement)
        {
            ThrowIfDisposed();
            if (current == null || replacement == null)
            {
                throw new ArgumentNullException(current == null ? nameof(current) : nameof(replacement));
            }

            var index = _sources.IndexOf(current);
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
            try
            {
                current.Detach();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (_disposed || !_sources.Contains(replacement))
            {
                Realm.RemoveSourceGhosts(current);
                return;
            }
            Realm.TransferSource(current, replacement);
            if (_disposed || !_sources.Contains(replacement))
            {
                Realm.RemoveSourceGhosts(replacement);
                return;
            }
            var generation = replacement.RegistrationGeneration + 1;
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

        /// <summary>Stops the anchor and destroys its scene objects.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var sources = new List<PresenceSource>(_sources);
            _sources.Clear();
            // Remove registration and records before scene callbacks can reenter the realm.
            Realm.NotifyAnchorDisposed(this);
            for (var index = sources.Count - 1; index >= 0; index--)
            {
                try
                {
                    sources[index].Detach();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
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
                var anchor = _anchor;
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
            var sources = new List<PresenceSource>(_sources);
            for (var index = 0; index < sources.Count; index++)
            {
                if (_disposed)
                {
                    break;
                }
                if (_sources.Contains(sources[index]))
                {
                    sources[index].Tick();
                }
            }
        }
    }
}
