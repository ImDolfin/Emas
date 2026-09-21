using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Represents one scene coordinate frame and its coordinators.</summary>
    public sealed class Origin : IDisposable
    {
        private readonly List<Coordinator> _coordinators = new List<Coordinator>();
        private readonly GameObject _gameObject;
        private bool _disposed;

        internal Origin(Context context, string id, Transform frame)
        {
            Context = context;
            Id = id;
            _gameObject = new GameObject("[Emas Origin] " + id);
            Transform = _gameObject.transform;
            var lifetime = _gameObject.AddComponent<OriginLifetime>();
            lifetime.Initialize(this);
            if (frame != null)
            {
                Transform.SetParent(frame, false);
            }
        }

        /// <summary>Gets the owning context.</summary>
        /// <value>The context that owns this origin.</value>
        public Context Context { get; private set; }

        /// <summary>Gets the stable origin identifier.</summary>
        /// <value>The exact origin identifier.</value>
        public string Id { get; private set; }

        /// <summary>Gets the scene transform for this origin.</summary>
        /// <value>The origin scene transform.</value>
        public Transform Transform { get; private set; }

        /// <summary>Adds and starts a coordinator.</summary>
        /// <param name="coordinator">The coordinator to add.</param>
        public void AddCoordinator(Coordinator coordinator)
        {
            ThrowIfDisposed();
            if (coordinator == null)
            {
                throw new ArgumentNullException(nameof(coordinator));
            }

            if (_coordinators.Contains(coordinator))
            {
                return;
            }

            if (coordinator.IsAttached)
            {
                throw new InvalidOperationException("The coordinator is already attached to an origin.");
            }
            var previous = Context.CaptureGhosts();
            _coordinators.Add(coordinator);
            try
            {
                coordinator.Attach(this);
            }
            catch
            {
                _coordinators.Remove(coordinator);
                if (coordinator.IsAttachedTo(this))
                {
                    coordinator.Detach();
                    Context.RollbackCoordinator(coordinator, previous);
                }
                throw;
            }
        }

        /// <summary>Stops and removes a coordinator and its owned ghosts.</summary>
        /// <param name="coordinator">The coordinator to remove.</param>
        public void RemoveCoordinator(Coordinator coordinator)
        {
            ThrowIfDisposed();
            if (coordinator == null || !_coordinators.Remove(coordinator))
            {
                return;
            }

            try
            {
                coordinator.Detach();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            Context.RemoveCoordinatorGhosts(coordinator);
        }

        /// <summary>Replaces one coordinator while preserving its compatible ghosts.</summary>
        /// <param name="current">The coordinator being replaced.</param>
        /// <param name="replacement">The replacement coordinator.</param>
        public void ReplaceCoordinator(Coordinator current, Coordinator replacement)
        {
            ThrowIfDisposed();
            if (current == null || replacement == null)
            {
                throw new ArgumentNullException(current == null ? nameof(current) : nameof(replacement));
            }

            var index = _coordinators.IndexOf(current);
            if (index < 0)
            {
                throw new InvalidOperationException("The current coordinator is not attached to this origin.");
            }

            if (current == replacement)
            {
                throw new ArgumentException("The replacement must be a different coordinator.", nameof(replacement));
            }

            if (_coordinators.Contains(replacement) || replacement.IsAttached)
            {
                throw new InvalidOperationException("The replacement coordinator is already registered with an origin.");
            }

            _coordinators[index] = replacement;
            try
            {
                current.Detach();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (_disposed || !_coordinators.Contains(replacement))
            {
                Context.RemoveCoordinatorGhosts(current);
                return;
            }
            Context.TransferCoordinator(current, replacement);
            if (_disposed || !_coordinators.Contains(replacement))
            {
                Context.RemoveCoordinatorGhosts(replacement);
                return;
            }
            try
            {
                replacement.Attach(this);
            }
            catch
            {
                // Preserve transferred identities, but never expose partially initialized data.
                if (replacement.IsAttachedTo(this))
                {
                    Context.MarkUnavailable(replacement);
                }
                throw;
            }
        }

        /// <summary>Stops the origin and destroys its scene objects.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var coordinators = new List<Coordinator>(_coordinators);
            _coordinators.Clear();
            // Remove registration and records before scene callbacks can reenter the context.
            Context.NotifyOriginDisposed(this);
            for (var index = coordinators.Count - 1; index >= 0; index--)
            {
                try
                {
                    coordinators[index].Detach();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                Context.RemoveCoordinatorGhosts(coordinators[index]);
            }
            if (_gameObject != null)
            {
                Context.DestroySceneObject(_gameObject);
            }
        }

        internal void ThrowIfDisposed()
        {
            Context.ThrowIfDisposed();
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Origin));
            }
        }

        private sealed class OriginLifetime : MonoBehaviour
        {
            private Origin _origin;

            internal void Initialize(Origin origin)
            {
                _origin = origin;
            }

            private void OnDestroy()
            {
                var origin = _origin;
                _origin = null;
                if (origin != null)
                {
                    origin.Context.NotifyOriginDestroyed(origin);
                }
            }
        }

        internal IReadOnlyList<IGhost> GetOwnedGhosts(Coordinator coordinator)
        {
            return Context.GetOwnedGhosts(coordinator);
        }

        internal void Tick()
        {
            if (_disposed)
            {
                return;
            }
            var coordinators = new List<Coordinator>(_coordinators);
            for (var index = 0; index < coordinators.Count; index++)
            {
                if (_disposed)
                {
                    break;
                }
                if (_coordinators.Contains(coordinators[index]))
                {
                    coordinators[index].Tick();
                }
            }
        }
    }
}
