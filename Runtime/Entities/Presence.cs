using System;
using System.Collections.Generic;

namespace Emas
{
    /// <summary>
    /// Represents one detected entity independently of its optional visual manifestation.
    /// </summary>
    /// <remarks>
    /// A realm owns this stable handle from the first detection until removal. Detection metadata and
    /// capability interfaces originate with the detector; the realm attaches a Ghost root whose components own its data updates.
    /// Grace periods and successful detector handovers retain this handle. After removal, detecting the same Key creates a new handle.
    /// </remarks>
    public sealed class Presence
    {
        private readonly Realm _realm;
        private readonly Key _key;
        private readonly List<Type> _capabilities = new List<Type>();
        private string _name;
        private Variant _variant;
        private Ghost _root;
        private WeakReference<object> _source;
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
        /// Gets the application source supplied by Detect, held through a weak reference.
        /// </summary>
        /// <remarks>
        /// May be a proxy, SDK client or another application object. Returns null when unassigned,
        /// collected, destroyed as a Unity object, or released on disappearance, handover or removal.
        /// Emas does not own or dispose it. Resolve this property inside module readers rather than
        /// capturing its target if the binding should also avoid retaining the source.
        /// </remarks>
        public object Source
        {
            get
            {
                object source;
                if (_source == null || !_source.TryGetTarget(out source))
                {
                    return null;
                }

                if (source is UnityEngine.Object unityObject && unityObject == null)
                {
                    return null;
                }

                return source;
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
        /// Checks whether the detector reported a capability interface.
        /// </summary>
        /// <typeparam name="T">The capability interface.</typeparam>
        /// <returns>True when the exact interface was reported.</returns>
        /// <remarks>This checks advertised metadata, not root components. Use Root.TryGet to resolve a component contract.</remarks>
        public bool HasCapability<T>() where T : class
        {
            return _capabilities.Contains(typeof(T));
        }

        internal Realm Realm
        {
            get
            {
                return _realm;
            }
        }

        internal bool SetSource(object source)
        {
            if (ReferenceEquals(Source, source))
            {
                return false;
            }

            _source = source == null ? null : new WeakReference<object>(source);
            return true;
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

                // Capabilities form a set; enumeration order alone must not rerun the initializer.
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

        internal void SetAvailable(bool available)
        {
            _available = available;
        }

        internal void MarkRemoved()
        {
            _available = false;
            _removed = true;
            _root = null;
            _source = null;
        }
    }
}
