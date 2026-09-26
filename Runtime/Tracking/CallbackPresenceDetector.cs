using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Publishes individual source changes and explicit removals through the realm update queue.
    /// </summary>
    /// <typeparam name="TSource">
    /// The source item type.
    /// </typeparam>
    /// <typeparam name="TGhost">
    /// The application ghost component.
    /// </typeparam>
    /// <remarks>
    /// All calls, including publish/remove callbacks, require Unity's main thread; the application handles SDK threading.
    /// Configure while detached. Published items must remain unchanged until processed; copy mutable SDK data before publishing.
    /// </remarks>
    public sealed class CallbackPresenceDetector<TSource, TGhost> : PresenceDetector where TGhost : Ghost
    {
        private readonly Kind _kind;
        private Func<TSource, string> _identify;
        private Action<TSource, TGhost> _apply;
        private Func<TSource, Variant> _variant;
        private Func<Action<TSource>, Action<string>, Action> _subscribe;
        private Action _unsubscribe;
        private long _unsubscribeGeneration;
        private string _unsubscribeContext;
        private int _subscribeDepth;

        /// <summary>
        /// Creates a callback source for one entity kind. Configure it before tracking.
        /// </summary>
        /// <param name="kind">
        /// The kind assigned to every ghost from this source.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The kind is empty or invalid.
        /// </exception>
        public CallbackPresenceDetector(Kind kind)
        {
            if (!kind.IsValid)
            {
                throw new ArgumentException("A callback source requires a valid kind.", nameof(kind));
            }

            _kind = kind;
        }

        /// <summary>
        /// Sets the stable identity selector, executed on the realm update thread.
        /// </summary>
        /// <param name="identify">
        /// Returns a non-empty entity ID. Repeated IDs update the same ghost.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public CallbackPresenceDetector<TSource, TGhost> IdentifyBy(Func<TSource, string> identify)
        {
            ThrowIfConfiguringWhileTracking();
            _identify = identify ?? throw new ArgumentNullException(nameof(identify));
            return this;
        }

        /// <summary>
        /// Sets the callback that copies a source item's data into its ghost on the realm update thread.
        /// </summary>
        /// <param name="apply">
        /// Receives the source item first and its stable ghost second.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public CallbackPresenceDetector<TSource, TGhost> Apply(Action<TSource, TGhost> apply)
        {
            ThrowIfConfiguringWhileTracking();
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            return this;
        }

        /// <summary>
        /// Optionally selects each published ghost's appearance. Omit to preserve existing appearances.
        /// </summary>
        /// <param name="variant">
        /// Runs on the realm update thread; Variant.None clears the appearance.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public CallbackPresenceDetector<TSource, TGhost> WithVariant(Func<TSource, Variant> variant)
        {
            ThrowIfConfiguringWhileTracking();
            _variant = variant ?? throw new ArgumentNullException(nameof(variant));
            return this;
        }

        /// <summary>
        /// Sets the subscription started when tracking begins and its cleanup action.
        /// </summary>
        /// <param name="subscribe">
        /// Receives publish and remove-by-ID callbacks that must be called on Unity's main thread. Returns an unsubscribe action, or null if cleanup is unnecessary.
        /// </param>
        /// <returns>
        /// This source for further configuration.
        /// </returns>
        /// <remarks>
        /// Subscription and cleanup run on the Unity thread. Initial items may be published during subscription; all events are deferred. Undo partial subscriptions before throwing. The SDK client remains application-owned.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// The callback is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The source is attached or a read/subscription is still executing.
        /// </exception>
        public CallbackPresenceDetector<TSource, TGhost> Listen(Func<Action<TSource>, Action<string>, Action> subscribe)
        {
            ThrowIfConfiguringWhileTracking();
            _subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            return this;
        }

        private void ThrowIfConfiguringWhileTracking()
        {
            if (IsAttached || _subscribeDepth > 0)
            {
                throw new InvalidOperationException("Configure callback sources before attaching to an anchor and outside subscription startup.");
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

            if (_apply == null)
            {
                missing.Add(nameof(Apply));
            }

            if (_subscribe == null)
            {
                missing.Add(nameof(Listen));
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException("Callback source is missing required steps: "
                    + string.Join(", ", missing) + ". Configure them before tracking.");
            }

            // Retained delegates continue to identify the attachment that created them.
            Realm realm = Anchor.Realm;
            long generation = RegistrationGeneration;
            string sourceContext = CaptureErrorContext();
            _subscribeDepth++;
            try
            {
                // Availability-loss callbacks can reattach us before the old failure finishes stopping.
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
                    _unsubscribeContext = sourceContext;
                }
                else
                {
                    // Startup may synchronously stop or restart tracking before returning cleanup.
                    Cleanup(cleanup, generation, sourceContext);
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(sourceContext, "Listen", _kind));
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
            // A superseded stop must not clean up a newer active attachment.
            if (IsActive)
            {
                return;
            }

            Action cleanup = _unsubscribe;
            _unsubscribe = null;
            Cleanup(cleanup, _unsubscribeGeneration, _unsubscribeContext);
        }

        private void Cleanup(Action cleanup, long generation, string sourceContext)
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
                string context = DescribeError(sourceContext, "Unsubscribe", _kind);
                RecordError(exception, generation, context);
                LogError(exception, context);
            }
        }

        private void Queue(Realm realm, long generation, Action action)
        {
            if (IsRegistration(realm, generation))
            {
                // Keep the subscription's generation so queued events cannot enter a later attachment.
                realm.Dispatch(this, generation, action);
            }
        }

        private void Publish(Realm realm, long generation, TSource item)
        {
            string sourceContext = CaptureErrorContext();
            string operation = "Publish";
            string id = null;
            try
            {
                if ((object)item == null)
                {
                    throw new InvalidOperationException("A callback source cannot publish a null item.");
                }

                // Selectors can stop or replace the source before mapping begins.
                operation = nameof(IdentifyBy);
                id = _identify(item);
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                ValidateId(id);
                operation = nameof(WithVariant);
                Variant? variant = _variant == null ? (Variant?)null : _variant(item);
                if (!IsRegistration(realm, generation))
                {
                    return;
                }

                operation = "GetOrCreate";
                TGhost ghost = GetOrCreate<TGhost>(id, _kind, variant);
                if (IsRegistration(realm, generation))
                {
                    operation = nameof(Apply);
                    _apply(item, ghost);
                }
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(sourceContext, operation, _kind, id));
                throw;
            }
        }

        private void RemovePublished(string id)
        {
            long generation = RegistrationGeneration;
            string sourceContext = CaptureErrorContext();
            try
            {
                ValidateId(id);
                Disappear(_kind, id);
            }
            catch (Exception exception)
            {
                RecordError(exception, generation, DescribeError(sourceContext, "Remove", _kind, id));
                throw;
            }
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException("A callback source requires a non-empty entity ID for publication or removal.");
            }
        }
    }
}
