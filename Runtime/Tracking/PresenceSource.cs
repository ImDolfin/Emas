using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Base class for one network or simulation integration.</summary>
    /// <remarks>Attach through an Anchor on the Unity thread. Sources own ghost data, not SDK clients.
    /// Lifecycle hooks and mapping run on the realm update thread; only Dispatch and callback-source publications accept worker calls.</remarks>
    public abstract class PresenceSource
    {
        private readonly object _registrationLock = new object();
        private Anchor _anchor;
        private bool _started;
        private long _registrationGeneration;
        private Exception _lastError;

        /// <summary>Gets the anchor currently hosting this source.</summary>
        /// <value>The attached anchor, or null while detached.</value>
        protected Anchor Anchor
        {
            get
            {
                return _anchor;
            }
        }

        /// <summary>Gets ghosts owned by this source.</summary>
        /// <value>A snapshot including unavailable ghosts.</value>
        protected IReadOnlyList<IGhost> OwnedGhosts
        {
            get
            {
                return _anchor == null ? new List<IGhost>() : _anchor.GetOwnedGhosts(this);
            }
        }

        /// <summary>Starts one attachment; acquire subscriptions and publish initial data here.</summary>
        /// <remarks>OnStop follows even when startup throws. Undo partial external subscriptions before throwing if cleanup cannot access them.</remarks>
        protected virtual void OnStart()
        {
        }

        /// <summary>Processes one realm update on the Unity thread.</summary>
        /// <remarks>Exceptions stop this registration and retain its ghosts as unavailable. Other sources continue.</remarks>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>Releases this attachment's subscriptions and source-owned resources.</summary>
        /// <remarks>Called once per started attachment, including failures. Do not dispose application-owned SDK clients.</remarks>
        protected virtual void OnStop()
        {
        }

        /// <summary>Obtains or creates a typed ghost owned by this source.</summary>
        /// <typeparam name="TGhost">The ghost component type.</typeparam>
        /// <param name="entityId">The source entity ID.</param>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="variant">The appearance; null retains an existing value, and None clears it.</param>
        /// <returns>The stable ghost component.</returns>
        /// <remarks>Use only from lifecycle or dispatched callbacks on the Unity thread. Data must be complete before the callback returns.
        /// A compatible replacement reuses the root; an incompatible ghost type is rejected.</remarks>
        /// <exception cref="InvalidOperationException">The source is inactive, another source owns the identity, or the existing/prefab ghost has an incompatible type.</exception>
        /// <exception cref="ArgumentException">The kind or entity ID is invalid.</exception>
        /// <exception cref="ObjectDisposedException">The owning anchor or realm was disposed.</exception>
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
        /// <remarks>Use only from lifecycle or dispatched callbacks on the Unity thread. Data must be complete before the callback returns.
        /// A compatible replacement reuses the root; an incompatible ghost type is rejected.</remarks>
        /// <exception cref="InvalidOperationException">The source is inactive, another source owns the identity, or the existing/prefab ghost has an incompatible type.</exception>
        /// <exception cref="ArgumentException">The kind or entity ID is invalid.</exception>
        /// <exception cref="ObjectDisposedException">The owning anchor or realm was disposed.</exception>
        protected TGhost GetOrCreate<TGhost>(string entityId, Kind kind, Variant? variant, string name) where TGhost : Ghost
        {
            if (_anchor == null || !_started)
            {
                throw new InvalidOperationException("The source is not active on an anchor.");
            }
            _anchor.ThrowIfDisposed();
            return _anchor.Realm.GetOrCreate<TGhost>(this, _anchor.Id, entityId, kind, variant, name);
        }

        /// <summary>Removes one ghost owned by this source.</summary>
        /// <param name="kind">The ghost kind.</param>
        /// <param name="entityId">The source entity ID.</param>
        /// <remarks>Unknown IDs and inactive registrations are ignored. An active call destroys the owned root and view.</remarks>
        protected void Remove(Kind kind, string entityId)
        {
            if (_anchor != null && _started)
            {
                _anchor.ThrowIfDisposed();
                _anchor.Realm.RemoveGhost(this, new Key(_anchor.Id, kind, entityId));
            }
        }

        /// <summary>Queues an action for a bounded batch on the Emas update thread.</summary>
        /// <param name="action">The action to execute on the Unity thread; null is ignored.</param>
        /// <remarks>May be called from worker threads. Stopped/detached calls are ignored; actions queued during an update wait until a later update.
        /// Up to 256 queued actions run per update across the realm.
        /// Custom sources must invalidate old SDK callbacks in OnStop before reattachment; CallbackPresenceSource handles subscription generations automatically.</remarks>
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

        /// <summary>Gets whether this registration is starting or accepting updates.</summary>
        /// <remarks>False after failure or detachment. Failure can leave the source attached with unavailable ghosts.</remarks>
        public bool IsActive
        {
            get
            {
                lock (_registrationLock)
                {
                    return _started;
                }
            }
        }

        /// <summary>Gets whether an anchor currently owns this source, including a failed registration.</summary>
        public bool IsAttached
        {
            get
            {
                lock (_registrationLock)
                {
                    return _anchor != null;
                }
            }
        }

        /// <summary>Gets the first error from the most recent attachment attempt, or null.</summary>
        /// <remarks>Cleared before OnStart. Retained after stopping or detachment; cleanup errors do not replace a primary error.
        /// Errors from superseded registrations cannot change this value.</remarks>
        public Exception LastError
        {
            get
            {
                lock (_registrationLock)
                {
                    return _lastError;
                }
            }
        }

        internal void RecordError(Exception exception, long generation)
        {
            lock (_registrationLock)
            {
                if (_registrationGeneration == generation && _lastError == null)
                {
                    _lastError = exception;
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
                    throw new InvalidOperationException("The source is already attached to an anchor.");
                }
                _anchor = anchor;
                _registrationGeneration++;
                _lastError = null;
                _started = true;
            }
            var generation = RegistrationGeneration;
            try
            {
                anchor.Realm.ApplySourceChanges(OnStart);
                if (IsRegistration(anchor.Realm, generation))
                {
                    anchor.Realm.FinalizeSource(this);
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation);
                if (IsAttachedTo(anchor) && RegistrationGeneration == generation)
                {
                    StopAfterFailure(generation);
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
                    anchor.Realm.FinalizeSource(this);
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
            var generation = RegistrationGeneration;
            RecordError(exception, generation);
            UnityEngine.Debug.LogException(exception);
            StopAfterFailure(generation);
        }

        private void StopAfterFailure(long generation)
        {
            Anchor anchor;
            lock (_registrationLock)
            {
                if (!_started || _registrationGeneration != generation)
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
                RecordError(exception, generation);
                UnityEngine.Debug.LogException(exception);
            }
        }

        internal void Detach()
        {
            var generation = RegistrationGeneration;
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
            catch (Exception exception)
            {
                RecordError(exception, generation);
                throw;
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
