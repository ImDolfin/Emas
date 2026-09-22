using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Base class for one network or simulation integration.</summary>
    public abstract class Coordinator
    {
        private readonly object _registrationLock = new object();
        private Origin _origin;
        private bool _started;
        private long _registrationGeneration;

        /// <summary>Gets the origin currently hosting this coordinator.</summary>
        /// <value>The attached origin, or null while detached.</value>
        protected Origin Origin
        {
            get
            {
                return _origin;
            }
        }

        /// <summary>Gets ghosts owned by this coordinator.</summary>
        /// <value>A snapshot including unavailable ghosts.</value>
        protected IReadOnlyList<IGhost> OwnedGhosts
        {
            get
            {
                return _origin == null ? new List<IGhost>() : _origin.GetOwnedGhosts(this);
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
            if (_origin == null || !_started)
            {
                throw new InvalidOperationException("The coordinator is not active on an origin.");
            }
            _origin.ThrowIfDisposed();
            return _origin.Realm.GetOrCreate<TGhost>(this, _origin.Id, entityId, kind, variant, name);
        }

        /// <summary>Removes one ghost owned by this coordinator.</summary>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="entityId">The source entity ID.</param>
        protected void Remove(Kind kind, string entityId)
        {
            if (_origin != null && _started)
            {
                _origin.ThrowIfDisposed();
                _origin.Realm.RemoveGhost(this, new Key(_origin.Id, kind, entityId));
            }
        }

        /// <summary>Queues an action for a bounded batch on the Emas update thread.</summary>
        /// <remarks>Late calls on stopped or detached registrations are ignored. Actions queued during an update wait for a later update.</remarks>
        /// <param name="action">The action to execute.</param>
        protected void Dispatch(Action action)
        {
            lock (_registrationLock)
            {
                if (_origin == null || !_started)
                {
                    return;
                }
                _origin.Realm.Dispatch(this, _registrationGeneration, action);
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
                    return _origin != null;
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

        internal bool IsAttachedTo(Origin origin)
        {
            lock (_registrationLock)
            {
                return _origin == origin;
            }
        }

        internal bool IsRegistration(Realm realm, long generation)
        {
            lock (_registrationLock)
            {
                return _started && _origin != null && _origin.Realm == realm
                    && _registrationGeneration == generation;
            }
        }

        internal void Attach(Origin origin)
        {
            origin.ThrowIfDisposed();
            lock (_registrationLock)
            {
                if (_origin != null)
                {
                    throw new InvalidOperationException("The coordinator is already attached to an origin.");
                }
                _origin = origin;
                _registrationGeneration++;
                _started = true;
            }
            var generation = RegistrationGeneration;
            try
            {
                origin.Realm.ApplySourceChanges(OnStart);
                if (IsRegistration(origin.Realm, generation))
                {
                    origin.Realm.FinalizeCoordinator(this);
                }
            }
            catch
            {
                if (IsAttachedTo(origin) && RegistrationGeneration == generation)
                {
                    StopAfterFailure();
                }
                throw;
            }
        }

        internal void Tick()
        {
            var origin = _origin;
            var generation = RegistrationGeneration;
            if (origin == null || !IsRegistration(origin.Realm, generation))
            {
                return;
            }
            try
            {
                origin.Realm.ApplySourceChanges(OnUpdate);
                if (IsRegistration(origin.Realm, generation))
                {
                    origin.Realm.FinalizeCoordinator(this);
                }
            }
            catch (Exception exception)
            {
                if (IsRegistration(origin.Realm, generation))
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
            Origin origin;
            lock (_registrationLock)
            {
                if (!_started)
                {
                    return;
                }
                _started = false;
                origin = _origin;
            }
            if (origin != null)
            {
                origin.Realm.MarkUnavailable(this);
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
                    _origin = null;
                }
            }
        }
    }
}
