using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Represents one detected entity independently of its optional visual manifestation.
    /// </summary>
    /// <remarks>
    /// A realm owns this stable handle from the first detection until removal. Detection metadata and
    /// capability interfaces originate with the detector; the realm attaches a Ghost root and entity modules.
    /// </remarks>
    public sealed class Presence
    {
        private readonly Realm _realm;
        private readonly Key _key;
        private readonly List<Type> _capabilities = new List<Type>();
        private readonly List<EntityModule> _modules = new List<EntityModule>();
        private string _name;
        private Variant _variant;
        private Ghost _root;
        private bool _available;
        private bool _removed;

        internal Presence(Realm realm, Key key, string name, Variant variant)
        {
            _realm = realm;
            _key = key;
            _name = name ?? key.EntityId;
            _variant = variant;
        }

        /// <summary>
        /// Gets the stable anchor, kind and entity identity.
        /// </summary>
        public Key Key
        {
            get
            {
                return _key;
            }
        }

        /// <summary>
        /// Gets the detector's label for this entity.
        /// </summary>
        public string Name
        {
            get
            {
                return _name;
            }
        }

        /// <summary>
        /// Gets the detected appearance used to select a view.
        /// </summary>
        public Variant Variant
        {
            get
            {
                return _variant;
            }
        }

        /// <summary>
        /// Gets whether the detector currently reports this entity as present and initialized.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                return _available;
            }
        }

        /// <summary>
        /// Gets whether this presence has been removed from its realm.
        /// </summary>
        public bool IsRemoved
        {
            get
            {
                return _removed;
            }
        }

        /// <summary>
        /// Gets the realm-initialized Ghost root, or null before initialization or after removal.
        /// </summary>
        public Ghost Root
        {
            get
            {
                return _root;
            }
        }

        /// <summary>
        /// Gets a snapshot of capability interfaces reported by the detector.
        /// </summary>
        public IReadOnlyList<Type> Capabilities
        {
            get
            {
                return new List<Type>(_capabilities).AsReadOnly();
            }
        }

        /// <summary>
        /// Gets a snapshot of the modules installed by the realm initializer.
        /// </summary>
        public IReadOnlyList<EntityModule> Modules
        {
            get
            {
                return new List<EntityModule>(_modules).AsReadOnly();
            }
        }

        /// <summary>
        /// Checks whether the detector reported a capability interface.
        /// </summary>
        /// <typeparam name="T">The capability interface.</typeparam>
        /// <returns>True when the exact interface was reported.</returns>
        public bool HasCapability<T>() where T : class
        {
            return _capabilities.Contains(typeof(T));
        }

        /// <summary>
        /// Adds a module that converts SDK data for this presence.
        /// </summary>
        /// <param name="module">A new, unbound module instance.</param>
        /// <remarks>Call from the registered realm initializer on Unity's main thread.</remarks>
        public void AddModule(EntityModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (_root == null || _removed)
            {
                throw new InvalidOperationException("A presence must have an active root before adding modules.");
            }

            module.Bind(this);
            _modules.Add(module);
        }

        /// <summary>
        /// Finds one installed module by type.
        /// </summary>
        /// <typeparam name="T">The module type.</typeparam>
        /// <param name="module">Receives the first matching module, if any.</param>
        /// <returns>True when a matching module is installed.</returns>
        public bool TryGetModule<T>(out T module) where T : EntityModule
        {
            foreach (EntityModule current in _modules)
            {
                if (current is T)
                {
                    module = (T)current;
                    return true;
                }
            }

            module = null;
            return false;
        }

        internal Realm Realm
        {
            get
            {
                return _realm;
            }
        }

        internal void BindRoot(Ghost root)
        {
            _root = root;
        }

        internal bool SetMetadata(string name, Variant? variant, IEnumerable<Type> capabilities)
        {
            if (name != null)
            {
                _name = name;
            }

            if (variant.HasValue)
            {
                _variant = variant.Value;
            }

            if (capabilities != null)
            {
                List<Type> next = new List<Type>();
                foreach (Type capability in capabilities)
                {
                    if (capability == null || !capability.IsInterface)
                    {
                        throw new ArgumentException("Presence capabilities must be non-null interface types.", nameof(capabilities));
                    }

                    if (!next.Contains(capability))
                    {
                        next.Add(capability);
                    }
                }

                bool changed = next.Count != _capabilities.Count;
                if (!changed)
                {
                    foreach (Type capability in next)
                    {
                        if (!_capabilities.Contains(capability))
                        {
                            changed = true;
                            break;
                        }
                    }
                }

                if (changed)
                {
                    _capabilities.Clear();
                    _capabilities.AddRange(next);
                }

                return changed;
            }

            return false;
        }

        internal bool ApplyData(object data)
        {
            bool applied = false;
            foreach (EntityModule module in _modules)
            {
                applied |= module.TryApply(data);
            }

            return applied;
        }

        internal void SetAvailable(bool available)
        {
            _available = available;
        }

        internal void MarkRemoved()
        {
            _available = false;
            _removed = true;
            _root = null;
            _modules.Clear();
        }
    }
}