using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Base class for detecting entities from one external SDK or simulation feed.
    /// </summary>
    /// <remarks>
    /// Use all detector operations on Unity's main thread. The application handles SDK threading before calling Emas.
    /// Attach through an Anchor. Report detections and SDK updates to the Realm; EntityModules apply updates to Ghost roots. SDK clients remain application-owned.
    /// </remarks>
    public abstract class PresenceDetector
    {
        private Anchor _anchor;
        private bool _started;
        private long _registrationGeneration;
        private Exception _lastError;
        private string _lastErrorContext;
        private string _name;
        private string _sourceContext;
        private int _lifecycleDepth;
        private TimeSpan? _inactivityTimeout;
        private TimeSpan _disappearanceGracePeriod;

        /// <summary>
        /// Gets the anchor currently hosting this source.
        /// </summary>
        /// <value>
        /// The attached anchor, or null while detached.
        /// </value>
        protected Anchor Anchor
        {
            get
            {
                return _anchor;
            }
        }

        /// <summary>
        /// Gets ghosts owned by this source.
        /// </summary>
        /// <value>
        /// A snapshot including unavailable ghosts.
        /// </value>
        protected IReadOnlyList<IGhost> OwnedGhosts
        {
            get
            {
                return _anchor == null ? new List<IGhost>() : _anchor.GetOwnedGhosts(this);
            }
        }

        /// <summary>
        /// Gets a snapshot of presences monitored by this detector, including unavailable ones.
        /// </summary>
        protected IReadOnlyList<Presence> OwnedPresences
        {
            get
            {
                return _anchor == null ? new List<Presence>() : _anchor.Realm.GetOwnedPresences(this);
            }
        }
        /// <summary>
        /// Starts one attachment; acquire subscriptions and publish initial data here.
        /// </summary>
        /// <remarks>
        /// OnStop follows even when startup throws. Undo partial external subscriptions before throwing if cleanup cannot access them.
        /// </remarks>
        protected virtual void OnStart()
        {
        }

        /// <summary>
        /// Processes one realm update on the Unity thread.
        /// </summary>
        /// <remarks>
        /// Exceptions stop this registration and remove its ghosts. Other sources continue.
        /// </remarks>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>
        /// Releases this attachment's subscriptions and source-owned resources.
        /// </summary>
        /// <remarks>
        /// Called once per started attachment, including failures. Do not dispose application-owned SDK clients.
        /// </remarks>
        protected virtual void OnStop()
        {
        }

        /// <summary>
        /// Detects an entity so the realm can create its stable Presence, root and modules.
        /// </summary>
        /// <param name="entityId">The stable SDK entity ID.</param>
        /// <param name="kind">The detected kind.</param>
        /// <param name="name">An optional entity label.</param>
        /// <param name="variant">An optional visual variant.</param>
        /// <param name="capabilities">Typed capability interfaces reported by the SDK.</param>
        /// <returns>The stable realm-owned presence handle.</returns>
        protected Presence Detect(string entityId, Kind kind, string name = null, Variant? variant = null,
            IEnumerable<Type> capabilities = null)
        {
            return ReportPresence(entityId, kind, name, variant, capabilities, null);
        }

        /// <summary>
        /// Reports an SDK update for the realm to apply through the presence's entity modules.
        /// </summary>
        /// <typeparam name="TData">The SDK update type.</typeparam>
        /// <param name="entityId">The stable SDK entity ID.</param>
        /// <param name="kind">The detected kind.</param>
        /// <param name="data">The SDK data forwarded to compatible entity modules.</param>
        /// <param name="name">An optional entity label.</param>
        /// <param name="variant">An optional visual variant.</param>
        /// <param name="capabilities">Typed capability interfaces reported by the SDK.</param>
        /// <returns>The stable realm-owned presence handle.</returns>
        protected Presence Report<TData>(string entityId, Kind kind, TData data, string name = null,
            Variant? variant = null, IEnumerable<Type> capabilities = null)
        {
            if ((object)data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return ReportPresence(entityId, kind, name, variant, capabilities, data);
        }

        private Presence ReportPresence(string entityId, Kind kind, string name, Variant? variant,
            IEnumerable<Type> capabilities, object data)
        {
            if (_anchor == null || !_started)
            {
                throw new InvalidOperationException("The detector is not active on an anchor.");
            }

            _anchor.ThrowIfDisposed();
            return _anchor.Realm.ReportPresence(this, _anchor.Id, entityId, kind, name, variant, capabilities, data);
        }
        /// <summary>
        /// Obtains or creates a typed ghost owned by this source.
        /// </summary>
        /// <typeparam name="TGhost">
        /// The ghost component type.
        /// </typeparam>
        /// <param name="entityId">
        /// The source entity ID.
        /// </param>
        /// <param name="kind">
        /// The ghost kind.
        /// </param>
        /// <param name="variant">
        /// The appearance; null retains an existing value, and None clears it.
        /// </param>
        /// <returns>
        /// The stable ghost component.
        /// </returns>
        /// <remarks>
        /// Call on Unity's main thread while this source is active. Complete data before a lifecycle or dispatched callback returns.
        /// A new or unavailable ghost obtained outside those callbacks becomes available on the next realm update; finish its data first.
        /// A compatible replacement reuses the root; an incompatible ghost type is rejected.
        /// Each successful call records activity for InactivityTimeout, including partial data publications.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The source is inactive, another source owns the identity, or the existing/prefab ghost has an incompatible type.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The kind or entity ID is invalid.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The owning anchor or realm was disposed.
        /// </exception>
        protected TGhost GetOrCreate<TGhost>(string entityId, Kind kind, Variant? variant = null) where TGhost : Ghost
        {
            return GetOrCreate<TGhost>(entityId, kind, variant, null);
        }

        /// <summary>
        /// Obtains or creates a typed ghost and sets its source display name.
        /// </summary>
        /// <typeparam name="TGhost">
        /// The ghost component type.
        /// </typeparam>
        /// <param name="entityId">
        /// The source entity ID.
        /// </param>
        /// <param name="kind">
        /// The ghost kind.
        /// </param>
        /// <param name="variant">
        /// The appearance; null retains an existing value, and None clears it.
        /// </param>
        /// <param name="name">
        /// The source display name, or null to retain the current name.
        /// </param>
        /// <returns>
        /// The stable ghost component.
        /// </returns>
        /// <remarks>
        /// Call on Unity's main thread while this source is active. Complete data before a lifecycle or dispatched callback returns.
        /// A new or unavailable ghost obtained outside those callbacks becomes available on the next realm update; finish its data first.
        /// A compatible replacement reuses the root; an incompatible ghost type is rejected.
        /// Each successful call records activity for InactivityTimeout, including partial data publications.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The source is inactive, another source owns the identity, or the existing/prefab ghost has an incompatible type.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The kind or entity ID is invalid.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The owning anchor or realm was disposed.
        /// </exception>
        protected TGhost GetOrCreate<TGhost>(string entityId, Kind kind, Variant? variant, string name) where TGhost : Ghost
        {
            if (_anchor == null || !_started)
            {
                throw new InvalidOperationException("The source is not active on an anchor.");
            }

            _anchor.ThrowIfDisposed();
            return _anchor.Realm.GetOrCreate<TGhost>(this, _anchor.Id, entityId, kind, variant, name);
        }

        /// <summary>
        /// Records fresh data written to a ghost cached by this source.
        /// </summary>
        /// <param name="ghost">
        /// The current ghost owned by this attachment.
        /// </param>
        /// <remarks>
        /// Call after updating cached ghost data to reset its inactivity deadline. Any partial data update counts as activity.
        /// GetOrCreate already records activity, so no additional call is needed when publishing through it.
        /// This does not notify consumers or change ghost data.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The source is inactive.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// The ghost is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The ghost is removed, belongs to another source, or was not claimed by this attachment.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The owning anchor or realm was disposed.
        /// </exception>
        protected void MarkPublished(IGhost ghost)
        {
            if (_anchor == null || !_started)
            {
                throw new InvalidOperationException("The source is not active on an anchor.");
            }

            _anchor.ThrowIfDisposed();
            _anchor.Realm.MarkPublished(this, ghost);
        }

        /// <summary>
        /// Reports that one owned presence has disappeared.
        /// </summary>
        /// <param name="kind">
        /// The ghost kind.
        /// </param>
        /// <param name="entityId">
        /// The source entity ID.
        /// </param>
        /// <remarks>
        /// Unknown IDs and inactive registrations are ignored. The Realm hides the presence immediately and removes it after DisappearanceGracePeriod.
        /// </remarks>
        protected void Disappear(Kind kind, string entityId)
        {
            if (_anchor != null && _started)
            {
                _anchor.ThrowIfDisposed();
                _anchor.Realm.RemoveGhost(this, new Key(_anchor.Id, kind, entityId));
            }
        }

        /// <summary>
        /// Defers an action to a bounded batch in a later realm update.
        /// </summary>
        /// <param name="action">
        /// The action to execute on the Unity thread; null is ignored.
        /// </param>
        /// <remarks>
        /// Call only on Unity's main thread. This defers work; it does not transfer work between threads.
        /// Stopped/detached calls are ignored; actions queued during an update wait until a later update.
        /// Up to 256 queued actions run per update across the realm.
        /// For SDK callbacks, use CaptureDispatcher to reject calls retained from a previous attachment; release subscriptions in OnStop.
        /// </remarks>
        protected void Dispatch(Action action)
        {
            if (_anchor == null || !_started)
            {
                return;
            }

            _anchor.Realm.Dispatch(this, _registrationGeneration, action);
        }

        /// <summary>
        /// Captures a dispatcher bound to this source's current attachment.
        /// </summary>
        /// <returns>
        /// A callback that queues work while this attachment remains active and ignores calls after it ends.
        /// </returns>
        /// <remarks>
        /// Capture during OnStart and pass the returned callback to an external subscription. Invoke it only on Unity's main thread.
        /// Queued actions run during a later realm update, subject to the realm's dispatch budget.
        /// This does not transfer work from background threads.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The source is not active on an anchor.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The owning anchor or realm was disposed.
        /// </exception>
        protected Action<Action> CaptureDispatcher()
        {
            if (_anchor == null || !_started)
            {
                throw new InvalidOperationException("The source is not active on an anchor.");
            }

            _anchor.ThrowIfDisposed();
            Realm realm = _anchor.Realm;
            long generation = _registrationGeneration;
            return action =>
            {
                if (IsRegistration(realm, generation))
                {
                    realm.Dispatch(this, generation, action);
                }
            };
        }

        /// <summary>
        /// Gets or sets how long an owned presence may go without publication before it disappears.
        /// </summary>
        /// <value>
        /// A positive timeout, or null to disable inactivity expiry, which is the default.
        /// </value>
        /// <remarks>
        /// Configure while detached. Uses unscaled real time and removes expired ghosts during the next realm update,
        /// after queued publications and detector updates. Report, GetOrCreate and MarkPublished reset the individual presence's deadline.
        /// Any partial data update counts as activity. Enable only for feeds that publish often enough to establish continued presence;
        /// feeds that publish only changed values should normally leave expiry disabled.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The timeout is zero or negative.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or running a lifecycle callback.
        /// </exception>
        public TimeSpan? InactivityTimeout
        {
            get
            {
                return _inactivityTimeout;
            }
            set
            {
                if (IsAttached || IsInLifecycle)
                {
                    throw new InvalidOperationException("Configure inactivity expiry before attaching the source to an anchor.");
                }

                if (value.HasValue && value.Value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The inactivity timeout must be positive or null.");
                }

                _inactivityTimeout = value;
            }
        }

        /// <summary>
        /// Gets or sets how long a disappeared presence remains unavailable before removal.
        /// </summary>
        /// <remarks>
        /// Zero, the default, removes immediately. A later report during the grace period reuses the same
        /// Presence and Ghost root. Configure while detached on Unity's main thread.
        /// </remarks>
        public TimeSpan DisappearanceGracePeriod
        {
            get
            {
                return _disappearanceGracePeriod;
            }
            set
            {
                if (IsAttached || IsInLifecycle)
                {
                    throw new InvalidOperationException("Configure disappearance grace before attaching the detector to an anchor.");
                }

                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Disappearance grace cannot be negative.");
                }

                _disappearanceGracePeriod = value;
            }
        }
        /// <summary>
        /// Gets whether this registration is starting or accepting updates.
        /// </summary>
        /// <remarks>
        /// False after failure or detachment. Failure leaves the source attached for retry but removes its ghosts.
        /// </remarks>
        public bool IsActive
        {
            get
            {
                return _started;
            }
        }

        /// <summary>
        /// Gets whether an anchor currently owns this source, including a failed registration.
        /// </summary>
        public bool IsAttached
        {
            get
            {
                return _anchor != null;
            }
        }

        /// <summary>
        /// Gets the first error from the most recent attachment attempt, or null.
        /// </summary>
        /// <remarks>
        /// Cleared before OnStart. Retained after stopping or detachment; cleanup errors do not replace a primary error.
        /// Errors from superseded registrations cannot change this value.
        /// </remarks>
        public Exception LastError
        {
            get
            {
                return _lastError;
            }
        }

        /// <summary>
        /// Gets or sets an application label used in diagnostics and failure reports.
        /// </summary>
        /// <remarks>
        /// Null, empty or whitespace labels use the source type name. Labels do not affect identity or ownership.
        /// Change this value only on Unity's main thread; recorded failure context retains its original label.
        /// </remarks>
        public string Name
        {
            get
            {
                return string.IsNullOrWhiteSpace(_name) ? GetType().Name.Split('`')[0] : _name;
            }
            set
            {
                _name = value;
                _sourceContext = null;
            }
        }

        /// <summary>
        /// Gets the anchor, source and operation associated with LastError, or null when no error is recorded.
        /// </summary>
        /// <remarks>
        /// Built-in sources include the kind and entity ID when known. Captured with the primary error and cleared
        /// before each attachment attempt; cleanup and stale registrations cannot replace that context.
        /// </remarks>
        public string LastErrorContext
        {
            get
            {
                return _lastErrorContext;
            }
        }

        internal bool IsInLifecycle
        {
            get
            {
                return _lifecycleDepth > 0;
            }
        }

        internal string CaptureErrorContext()
        {
            if (_sourceContext == null)
            {
                _sourceContext = "anchor '" + (_anchor == null ? "<detached>" : _anchor.Id)
                    + "', source '" + Name + "' (" + GetType().Name.Split('`')[0] + ")";
            }

            return _sourceContext;
        }

        internal static string DescribeError(string sourceContext, string operation, Kind? kind = null, string entityId = null)
        {
            string context = sourceContext + ", operation '" + operation + "'";
            if (kind.HasValue)
            {
                context += ", kind '" + kind.Value.Id + "'";
            }

            if (entityId != null)
            {
                context += ", entity '" + entityId + "'";
            }

            return context;
        }

        internal void RecordError(Exception exception, long generation, string context)
        {
            if (_registrationGeneration == generation && _lastError == null)
            {
                _lastError = exception;
                _lastErrorContext = context;
            }
        }

        internal static void LogError(Exception exception, string context)
        {
            // Unity unwraps LogException's inner exceptions; format explicitly to keep context on the first line.
            UnityEngine.Debug.LogFormat(UnityEngine.LogType.Exception, UnityEngine.LogOption.NoStacktrace, null,
                "Emas: {0}. {1}: {2}\n{3}", context, exception.GetType().Name, exception.Message, exception);
        }

        private string ErrorContextFor(Exception exception, long generation, string fallback)
        {
            return _registrationGeneration == generation && ReferenceEquals(_lastError, exception)
                ? _lastErrorContext : fallback;
        }

        private void InvokeLifecycle(Action callback)
        {
            _lifecycleDepth++;
            try
            {
                callback();
            }
            finally
            {
                _lifecycleDepth--;
            }
        }

        internal long RegistrationGeneration
        {
            get
            {
                return _registrationGeneration;
            }
        }

        internal bool IsAttachedTo(Anchor anchor)
        {
            return _anchor == anchor;
        }

        internal bool IsRegistration(Realm realm, long generation)
        {
            return _started && _anchor != null && _anchor.Realm == realm
                && _registrationGeneration == generation;
        }

        internal void Attach(Anchor anchor)
        {
            anchor.ThrowIfDisposed();
            if (_anchor != null)
            {
                throw new InvalidOperationException("The source is already attached to an anchor.");
            }

            // Startup callbacks observe the new attachment and a cleared error state.
            _anchor = anchor;
            _sourceContext = null;
            _registrationGeneration++;
            _lastError = null;
            _lastErrorContext = null;
            _started = true;
            long generation = RegistrationGeneration;
            string context = DescribeError(CaptureErrorContext(), "OnStart");
            try
            {
                anchor.Realm.ApplySourceChanges(() => InvokeLifecycle(OnStart));
                if (IsRegistration(anchor.Realm, generation))
                {
                    anchor.Realm.FinalizeSource(this);
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, context);
                if (IsAttachedTo(anchor) && RegistrationGeneration == generation)
                {
                    StopAfterFailure(generation, false);
                }

                throw;
            }
        }

        internal void Tick()
        {
            Anchor anchor = _anchor;
            long generation = RegistrationGeneration;
            if (anchor == null || !IsRegistration(anchor.Realm, generation))
            {
                return;
            }

            string sourceContext = CaptureErrorContext();
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
                    HandleFailure(exception, DescribeError(sourceContext, "OnUpdate"));
                }
                else
                {
                    LogError(exception, DescribeError(sourceContext, "OnUpdate"));
                }
            }
        }

        internal void HandleFailure(Exception exception, string context)
        {
            if (!IsActive)
            {
                return;
            }

            long generation = RegistrationGeneration;
            RecordError(exception, generation, context);
            LogError(exception, ErrorContextFor(exception, generation, context));
            StopAfterFailure(generation);
        }

        private void StopAfterFailure(long generation, bool removeGhosts = true)
        {
            if (!_started || _registrationGeneration != generation)
            {
                return;
            }

            string context = DescribeError(CaptureErrorContext(), "OnStop");
            // Finish old cleanup before deactivation can reattach this source through scene callbacks.
            _started = false;
            _lifecycleDepth++;
            try
            {
                Anchor anchor = _anchor;
                try
                {
                    OnStop();
                }
                catch (Exception exception)
                {
                    RecordError(exception, generation, context);
                    LogError(exception, context);
                }

                if (_registrationGeneration == generation && _anchor == anchor && anchor != null)
                {
                    if (removeGhosts)
                    {
                        anchor.Realm.RemoveSourceGhosts(this);
                    }
                    else
                    {
                        // The caller must first restore any prepared roots claimed during startup.
                        anchor.Realm.MarkUnavailable(this);
                    }
                }
            }
            finally
            {
                _lifecycleDepth--;
            }
        }

        internal void Detach()
        {
            long generation = RegistrationGeneration;
            bool wasStarted = _started;
            string context = DescribeError(CaptureErrorContext(), "OnStop");
            _started = false;
            try
            {
                if (wasStarted)
                {
                    InvokeLifecycle(OnStop);
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, context);
                LogError(exception, context);
            }
            finally
            {
                // Cleanup may remove and reattach the instance; leave a newer registration intact.
                if (_registrationGeneration == generation)
                {
                    _anchor = null;
                    _sourceContext = null;
                }
            }
        }
    }
}
