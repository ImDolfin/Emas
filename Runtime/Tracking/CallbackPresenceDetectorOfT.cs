using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>Reports individual SDK changes and disappearances through the realm update queue.</summary>
    /// <typeparam name="TSource">The SDK item type.</typeparam>
    /// <remarks>
    /// All callbacks run on Unity's main thread. Copy mutable SDK data before invoking a publish callback,
    /// because its item is processed during a later realm update.
    /// </remarks>
    public sealed class CallbackPresenceDetector<TSource> : PresenceDetector
    {
        private readonly Kind _kind;
        private Func<TSource, string> _identify;
        private Func<TSource, string> _name;
        private Func<TSource, Variant> _variant;
        private Func<TSource, IEnumerable<Type>> _capabilities;
        private Func<Action<TSource>, Action<string>, Action> _subscribe;
        private Action _unsubscribe;
        private long _unsubscribeGeneration;
        private string _unsubscribeContext;
        private int _subscribeDepth;

        /// <summary>Creates a callback detector for one entity kind.</summary>
        /// <param name="kind">The kind assigned to every detected item.</param>
        public CallbackPresenceDetector(Kind kind)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("A callback detector requires a valid kind.", nameof(kind));
            }

            _kind = kind;
        }

        /// <summary>Sets the stable identity selector, executed during a realm update.</summary>
        /// <param name="identify">Returns a nonempty ID; repeated IDs update the same presence.</param>
        /// <returns>This detector.</returns>
        public CallbackPresenceDetector<TSource> IdentifyBy(Func<TSource, string> identify)
        {
            ThrowIfConfiguring();
            _identify = identify ?? throw new ArgumentNullException(nameof(identify));
            return this;
        }

        /// <summary>Optionally selects the entity display name.</summary>
        /// <param name="name">Returns a label; null retains the prior label.</param>
        /// <returns>This detector.</returns>
        public CallbackPresenceDetector<TSource> WithName(Func<TSource, string> name)
        {
            ThrowIfConfiguring();
            _name = name ?? throw new ArgumentNullException(nameof(name));
            return this;
        }

        /// <summary>Optionally selects the entity's visual variant.</summary>
        /// <param name="variant">Returns a variant; Variant.None clears the prior variant.</param>
        /// <returns>This detector.</returns>
        public CallbackPresenceDetector<TSource> WithVariant(Func<TSource, Variant> variant)
        {
            ThrowIfConfiguring();
            _variant = variant ?? throw new ArgumentNullException(nameof(variant));
            return this;
        }

        /// <summary>Optionally selects capability interfaces reported by the SDK.</summary>
        /// <param name="capabilities">Returns interface types; null retains prior capabilities.</param>
        /// <returns>This detector.</returns>
        public CallbackPresenceDetector<TSource> WithCapabilities(Func<TSource, IEnumerable<Type>> capabilities)
        {
            ThrowIfConfiguring();
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            return this;
        }

        /// <summary>Sets the SDK subscription and its cleanup action.</summary>
        /// <param name="subscribe">
        /// Receives publish and disappear-by-ID callbacks on Unity's main thread.
        /// Returns an unsubscribe action, or null when cleanup is unnecessary.
        /// </param>
        /// <returns>This detector.</returns>
        /// <remarks>
        /// Initial callbacks may run during subscription and are queued for a later realm update.
        /// The application owns its SDK client and must undo partial subscriptions before throwing.
        /// </remarks>
        public CallbackPresenceDetector<TSource> Listen(Func<Action<TSource>, Action<string>, Action> subscribe)
        {
            ThrowIfConfiguring();
            _subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            return this;
        }

        private void ThrowIfConfiguring()
        {
            if (IsAttached || _subscribeDepth > 0)
            {
                throw new InvalidOperationException("Configure callback detectors before attaching to an anchor.");
            }
        }

        /// <inheritdoc />
        protected override void OnStart()
        {
            List<string> missing = new List<string>();
            if (_identify == null)
            {
                missing.Add(nameof(IdentifyBy));
            }

            if (_subscribe == null)
            {
                missing.Add(nameof(Listen));
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException("Callback detector is missing required steps: "
                    + string.Join(", ", missing) + ". Configure them before tracking.");
            }

            Realm realm = Anchor.Realm;
            long generation = RegistrationGeneration;
            string context = CaptureErrorContext();
            _subscribeDepth++;
            try
            {
                Action previousCleanup = _unsubscribe;
                _unsubscribe = null;
                Cleanup(previousCleanup, _unsubscribeGeneration, _unsubscribeContext);
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                Action cleanup = _subscribe(
                    item => Queue(realm, generation, () => Publish(realm, generation, item)),
                    id => Queue(realm, generation, () => RemovePublished(id)));
                if (IsRegistration(realm, generation))
                {
                    _unsubscribe = cleanup;
                    _unsubscribeGeneration = generation;
                    _unsubscribeContext = context;
                }
                else
                {
                    Cleanup(cleanup, generation, context);
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(context, "Listen", _kind));
                throw;
            }
            finally
            {
                _subscribeDepth--;
            }
        }

        /// <inheritdoc />
        protected override void OnStop()
        {
            if (IsActive)
            {
                return;
            }

            Action cleanup = _unsubscribe;
            _unsubscribe = null;
            Cleanup(cleanup, _unsubscribeGeneration, _unsubscribeContext);
        }

        private void Cleanup(Action cleanup, long generation, string context)
        {
            try
            {
                if (cleanup != null)
                {
                    cleanup();
                }
            }
            catch (Exception exception)
            {
                string errorContext = DescribeError(context, "Unsubscribe", _kind);
                RecordError(exception, generation, errorContext);
                LogError(exception, errorContext);
            }
        }

        private void Queue(Realm realm, long generation, Action action)
        {
            if (IsRegistration(realm, generation))
            {
                realm.Dispatch(this, generation, action);
            }
        }

        private void Publish(Realm realm, long generation, TSource item)
        {
            string context = CaptureErrorContext();
            string operation = "Publish";
            string id = null;
            try
            {
                if ((object)item == null)
                {
                    throw new InvalidOperationException("A callback detector cannot report a null item.");
                }

                operation = nameof(IdentifyBy);
                id = _identify(item);
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                ValidateId(id);
                operation = nameof(WithName);
                string name = _name == null ? null : _name(item);
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                operation = nameof(WithVariant);
                Variant? variant = _variant == null ? (Variant?)null : _variant(item);
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                operation = nameof(WithCapabilities);
                List<Type> capabilities = SnapshotCapabilities(_capabilities == null ? null : _capabilities(item));
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                operation = "Report";
                Report(id, _kind, item, name, variant, capabilities);
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(context, operation, _kind, id));
                throw;
            }
        }

        private void RemovePublished(string id)
        {
            long generation = RegistrationGeneration;
            string context = CaptureErrorContext();
            try
            {
                ValidateId(id);
                Disappear(_kind, id);
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(context, "Disappear", _kind, id));
                throw;
            }
        }

        private static List<Type> SnapshotCapabilities(IEnumerable<Type> selected)
        {
            if (selected == null)
            {
                return null;
            }

            List<Type> snapshot = new List<Type>();
            foreach (Type capability in selected)
            {
                if (capability == null || !capability.IsInterface)
                {
                    throw new ArgumentException("Presence capabilities must be non-null interface types.", nameof(selected));
                }

                if (!snapshot.Contains(capability))
                {
                    snapshot.Add(capability);
                }
            }

            return snapshot;
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException("A callback detector requires a nonempty entity ID.");
            }
        }
    }
}
