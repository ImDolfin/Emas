using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Receives detections, creates Ghost roots, and applies SDK data through entity modules.
    /// </summary>
    public sealed partial class Realm
    {
        private readonly Dictionary<string, IPresenceInitializer> _presenceInitializers =
            new Dictionary<string, IPresenceInitializer>(StringComparer.Ordinal);

        /// <summary>
        /// Registers the root type and module setup for one detected kind.
        /// </summary>
        /// <typeparam name="TGhost">The Ghost component used as the invisible root.</typeparam>
        /// <param name="kind">The kind this initializer handles.</param>
        /// <param name="initialize">Adds modules and configures a new or newly capable presence.</param>
        /// <remarks>
        /// Register before attaching detectors. The callback runs after root creation and before SDK data is
        /// applied or the Ghost becomes available. It runs again when reported capabilities change; use
        /// TryGetModule to avoid installing a duplicate module.
        /// </remarks>
        public void RegisterPresenceInitializer<TGhost>(Kind kind, Action<Presence, TGhost> initialize)
            where TGhost : Ghost
        {
            ThrowIfDisposed();
            if (!kind.IsValid)
            {
                throw new ArgumentException("A presence initializer requires a valid kind.", nameof(kind));
            }

            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            foreach (Record record in _ghosts.Values)
            {
                if (record.Key.Kind == kind)
                {
                    throw new InvalidOperationException("Register a presence initializer before creating ghosts of its kind.");
                }
            }

            _presenceInitializers[kind.Id] = new PresenceInitializer<TGhost>(initialize);
        }

        /// <summary>
        /// Finds a stable presence by identity, including one that is temporarily unavailable.
        /// </summary>
        /// <param name="key">The anchor, kind and entity identity.</param>
        /// <param name="presence">The tracked presence, or null when absent.</param>
        /// <returns>True while the realm retains this presence.</returns>
        public bool TryGetPresence(Key key, out Presence presence)
        {
            presence = null;
            if (_disposed)
            {
                return false;
            }

            Record record;
            if (!_ghosts.TryGetValue(key, out record) || record.Ghost == null || record.Presence == null)
            {
                return false;
            }

            presence = EnsurePresence(record);
            return true;
        }

        /// <summary>
        /// Requests a manifestation for a detected presence at its current or Full detail level.
        /// </summary>
        /// <param name="presence">The presence to manifest.</param>
        /// <returns>The current view, or null while no view can be shown.</returns>
        public View Manifest(Presence presence)
        {
            return Manifest(FindPresenceRoot(presence));
        }

        /// <summary>
        /// Requests a manifestation for a detected presence at a specific detail level.
        /// </summary>
        /// <param name="presence">The presence to manifest.</param>
        /// <param name="detailLevel">The desired detail level.</param>
        /// <returns>The current view, or null while no view can be shown.</returns>
        public View Manifest(Presence presence, DetailLevel detailLevel)
        {
            return Manifest(FindPresenceRoot(presence), detailLevel);
        }

        /// <summary>
        /// Removes the view of a presence while retaining its detection and Ghost root.
        /// </summary>
        /// <param name="presence">The presence to demanifest.</param>
        public void Demanifest(Presence presence)
        {
            Demanifest(FindPresenceRoot(presence));
        }

        /// <summary>
        /// Changes the requested detail level of a presence's view.
        /// </summary>
        /// <param name="presence">The presence whose view should change.</param>
        /// <param name="detailLevel">The desired detail level.</param>
        public void SetDetailLevel(Presence presence, DetailLevel detailLevel)
        {
            SetDetailLevel(FindPresenceRoot(presence), detailLevel);
        }

        internal Presence ReportPresence(PresenceDetector detector, string anchorId, string entityId,
            Kind kind, string name, Variant? variant, IEnumerable<Type> capabilities, object data)
        {
            ThrowIfDisposed();
            if (detector == null || !detector.IsActive)
            {
                throw new InvalidOperationException("Only an active detector can report a presence.");
            }

            if (!kind.IsValid || string.IsNullOrEmpty(anchorId) || string.IsNullOrEmpty(entityId))
            {
                throw new ArgumentException("Anchor, kind and entity identifiers are required.");
            }

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

            Key key = new Key(anchorId, kind, entityId);
            IPresenceInitializer initializer;
            Ghost root;
            if (_presenceInitializers.TryGetValue(kind.Id, out initializer))
            {
                root = initializer.GetOrCreate(this, detector, key, variant, name);
            }
            else
            {
                Record existing;
                ManifestationBlueprintSnapshot blueprint = ResolveManifestationBlueprint(anchorId, kind);
                if (_ghosts.TryGetValue(key, out existing) || (blueprint != null && blueprint.GhostPrefab != null))
                {
                    root = GetOrCreate<Ghost>(detector, anchorId, entityId, kind, variant, name);
                }
                else
                {
                    root = GetOrCreate<DefaultGhost>(detector, anchorId, entityId, kind, variant, name);
                }
            }

            Record record = FindRecord(root);
            Presence presence = EnsurePresence(record);
            bool capabilitiesChanged = presence.SetMetadata(root.Name, root.Variant, capabilitySnapshot);
            if (!record.PresenceInitialized || capabilitiesChanged)
            {
                if (initializer != null)
                {
                    initializer.Initialize(presence);
                }

                record.PresenceInitialized = true;
            }

            if (data != null)
            {
                presence.ApplyData(data);
            }

            return presence;
        }

        internal IReadOnlyList<Presence> GetOwnedPresences(PresenceDetector detector)
        {
            List<Presence> result = new List<Presence>();
            foreach (Record record in _ghosts.Values)
            {
                if (record.Owner == detector && record.Presence != null)
                {
                    result.Add(EnsurePresence(record));
                }
            }

            return result.AsReadOnly();
        }
        private Presence EnsurePresence(Record record)
        {
            if (record.Presence == null)
            {
                record.Presence = new Presence(this, record.Key, record.Ghost.Name, record.Ghost.Variant);
                record.Presence.BindRoot(record.Ghost);
            }
            else
            {
                record.Presence.SetMetadata(record.Ghost.Name, record.Ghost.Variant, null);
            }

            record.Presence.SetAvailable(record.Ghost.IsAvailable);
            return record.Presence;
        }

        private IGhost FindPresenceRoot(Presence presence)
        {
            if (presence == null || !ReferenceEquals(presence.Realm, this) || presence.IsRemoved)
            {
                return null;
            }

            Record record = FindRecord(presence.Root);
            return record != null && ReferenceEquals(record.Presence, presence) ? record.Ghost : null;
        }

        private interface IPresenceInitializer
        {
            Ghost GetOrCreate(Realm realm, PresenceDetector detector, Key key, Variant? variant, string name);

            void Initialize(Presence presence);
        }

        private sealed class PresenceInitializer<TGhost> : IPresenceInitializer where TGhost : Ghost
        {
            private readonly Action<Presence, TGhost> _initialize;

            internal PresenceInitializer(Action<Presence, TGhost> initialize)
            {
                _initialize = initialize;
            }

            public Ghost GetOrCreate(Realm realm, PresenceDetector detector, Key key, Variant? variant, string name)
            {
                return realm.GetOrCreate<TGhost>(detector, key.AnchorId, key.EntityId, key.Kind, variant, name);
            }

            public void Initialize(Presence presence)
            {
                _initialize(presence, (TGhost)presence.Root);
            }
        }
    }
}