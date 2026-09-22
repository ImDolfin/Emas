using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Base class for one network or simulation integration.</summary>
    public abstract class Coordinator
    {
        private readonly object _registrationLock = new object();
        private Anchor _anchor;
        private bool _started;
        private long _registrationGeneration;

        /// <summary>Gets the anchor currently hosting this coordinator.</summary>
        /// <value>The attached anchor, or null while detached.</value>
        protected Anchor Anchor
        {
            get
            {
                return _anchor;
            }
        }

        /// <summary>Gets ghosts owned by this coordinator.</summary>
        /// <value>A snapshot including unavailable ghosts.</value>
        protected IReadOnlyList<IGhost> OwnedGhosts
        {
            get
            {
                return _anchor == null ? new List<IGhost>() : _anchor.GetOwnedGhosts(this);
            }
        }

        /// <summary>Starts the source integration.</summary>
        protected virtual void OnStart()
        {
        }

        /// <summary>Processes one source update.</summary>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>Stops the source integration.</summary>
        protected virtual void OnStop()
        {
        }

        /// <summary>Obtains or creates a typed ghost owned by this coordinator.</summary>
        /// <typeparam name="TGhost">The ghost component type.</typeparam>
        /// <param name="entityId">The source entity ID.</param>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="variant">The appearance; null retains an existing value, and None clears it.</param>
        /// <returns>The stable ghost component.</returns>
        protected TGhost GetOrCreate<TGhost>(string entityId, Kind kind, Variant? variant = null) where TGhost : Ghost
        {
            return GetOrCreate<TGhost>(entityId, kind, variant, null);
        }

        /// <summary>Obtains or creates a typed ghost and sets its source display name.</summary>
        /// <typeparam name="TGhost">The ghost component type.</typeparam>
        /// <param name="entityId">The source entity ID.</param>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="variant">The appearance; null retains an existing value, and None clears it.</param>
        /// <param name="name">The source display name, or null to retain the current name.</param>
        /// <returns>The stable ghost component.</returns>
        protected TGhost GetOrCreate<TGhost>(string entityId, Kind kind, Variant? variant, string name) where TGhost : Ghost
        {
            if (_anchor == null || !_started)
            {
                throw new InvalidOperationException("The coordinator is not active on an anchor.");
            }
            _anchor.ThrowIfDisposed();
            return _anchor.Realm.GetOrCreate<TGhost>(this, _anchor.Id, entityId, kind, variant, name);
        }

        /// <summary>Removes one ghost owned by this coordinator.</summary>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="entityId">The source entity ID.</param>
        protected void Remove(Kind kind, string entityId)
        {
            if (_anchor != null && _started)
            {
                _anchor.ThrowIfDisposed();
                _anchor.Realm.RemoveGhost(this, new Key(_anchor.Id, kind, entityId));
            }
        }

        /// <summary>Queues an action for a bounded batch on the Emas update thread.</summary>
        /// <remarks>Late calls on stopped or detached registrations are ignored. Actions queued during an update wait for a later update.</remarks>
        /// <param name="action">The action to execute.</param>
        protected void Dispatch(Action action)
        {
            lock (_registrationLock)
            {
                if (_anchor == null || !_started)
                {
                    return;
                }
                _anchor.Realm.Dispatch(this, _registrationGeneration, action);
            }
        }

        internal bool IsActive
        {
            get
            {
                lock (_registrationLock)
                {
                    return _started;
                }
            }
        }

        internal bool IsAttached
        {
            get
            {
                lock (_registrationLock)
                {
                    return _anchor != null;
                }
            }
        }

        internal long RegistrationGeneration
        {
            get
            {
                lock (_registrationLock)
                {
                    return _registrationGeneration;
                }
            }
        }

        internal bool IsAttachedTo(Anchor anchor)
        {
            lock (_registrationLock)
            {
                return _anchor == anchor;
            }
        }

        internal bool IsRegistration(Realm realm, long generation)
        {
            lock (_registrationLock)
            {
                return _started && _anchor != null && _anchor.Realm == realm
                    && _registrationGeneration == generation;
            }
        }

        internal void Attach(Anchor anchor)
        {
            anchor.ThrowIfDisposed();
            lock (_registrationLock)
            {
                if (_anchor != null)
                {
                    throw new InvalidOperationException("The coordinator is already attached to an anchor.");
                }
                _anchor = anchor;
                _registrationGeneration++;
                _started = true;
            }
            var generation = RegistrationGeneration;
            try
            {
                anchor.Realm.ApplySourceChanges(OnStart);
                if (IsRegistration(anchor.Realm, generation))
                {
                    anchor.Realm.FinalizeCoordinator(this);
                }
            }
            catch
            {
                if (IsAttachedTo(anchor) && RegistrationGeneration == generation)
                {
                    StopAfterFailure();
                }
                throw;
            }
        }

        internal void Tick()
        {
            var anchor = _anchor;
            var generation = RegistrationGeneration;
            if (anchor == null || !IsRegistration(anchor.Realm, generation))
            {
                return;
            }
            try
            {
                anchor.Realm.ApplySourceChanges(OnUpdate);
                if (IsRegistration(anchor.Realm, generation))
                {
                    anchor.Realm.FinalizeCoordinator(this);
                }
            }
            catch (Exception exception)
            {
                if (IsRegistration(anchor.Realm, generation))
                {
                    HandleFailure(exception);
                }
                else
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }

        internal void HandleFailure(Exception exception)
        {
            if (!IsActive)
            {
                return;
            }
            UnityEngine.Debug.LogException(exception);
            StopAfterFailure();
        }

        private void StopAfterFailure()
        {
            Anchor anchor;
            lock (_registrationLock)
            {
                if (!_started)
                {
                    return;
                }
                _started = false;
                anchor = _anchor;
            }
            if (anchor != null)
            {
                anchor.Realm.MarkUnavailable(this);
            }
            try
            {
                OnStop();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        internal void Detach()
        {
            bool wasStarted;
            lock (_registrationLock)
            {
                wasStarted = _started;
                _started = false;
            }
            try
            {
                if (wasStarted)
                {
                    OnStop();
                }
            }
            finally
            {
                lock (_registrationLock)
                {
                    _anchor = null;
                }
            }
        }
    }
}
