using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Base component for application-defined ghost components.</summary>

    /// <remarks>Subclass to store application data and implement read-only contracts. Emas creates/destroys roots and sets their metadata.
    /// Roots activate after successful publication and deactivate on availability loss; Awake may run before source mapping.
    /// Keep source mutation methods on the concrete subclass. Views are optional children, independent of root behaviors.</remarks>
    public abstract class Ghost : MonoBehaviour, IGhost
    {
        [SerializeField] private string _anchorId;
        [SerializeField] private string _entityId;
        [SerializeField] private string _kindId;
        [SerializeField] private string _name;
        [SerializeField] private Variant _variant;
        [SerializeField] private bool _isAvailable;

        /// <inheritdoc />
        public Key Key
        {
            get
            {
                var kind = string.IsNullOrEmpty(_kindId) ? default(Kind) : new Kind(_kindId);
                return new Key(_anchorId, kind, _entityId);
            }
        }

        /// <inheritdoc />
        public string Name
        {
            get { return _name; }
        }

        /// <inheritdoc />
        public Variant Variant
        {
            get { return _variant; }
        }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get { return _isAvailable; }
        }

        /// <inheritdoc />
        public bool TryGet<T>(out T part) where T : class
        {
            part = null;
            var matches = GetComponents<MonoBehaviour>();
            var count = 0;
            for (var index = 0; index < matches.Length; index++)
            {
                var candidate = matches[index] as T;
                if (candidate == null)
                {
                    continue;
                }

                part = candidate;
                count++;
            }

            if (count > 1)
            {
                Debug.LogError("Ghost '" + name + "' has multiple providers for " + typeof(T).FullName + ".");
                part = null;
                return false;
            }

            return count == 1;
        }

        /// <summary>Initializes Emas-owned identity and metadata.</summary>
        /// <param name="key">The ghost key.</param>
        /// <param name="nameValue">The display name.</param>
        /// <param name="variantValue">The visual variant.</param>
        internal void Initialize(Key key, string nameValue, Variant variantValue)
        {
            _anchorId = key.AnchorId;
            _entityId = key.EntityId;
            _kindId = key.Kind.Id;
            _name = nameValue ?? key.EntityId;
            _variant = variantValue;
            _isAvailable = false;
        }

        /// <summary>Sets the source availability state.</summary>
        /// <param name="available">The new availability state.</param>
        internal void SetAvailable(bool available)
        {
            _isAvailable = available;
        }

        /// <summary>Updates source-provided display metadata.</summary>
        /// <param name="nameValue">The new name, or null to retain the current name.</param>
        /// <param name="variantValue">The new variant, or null to retain the current variant.</param>
        internal void SetMetadata(string nameValue, Variant? variantValue)
        {
            if (nameValue != null)
            {
                _name = nameValue;
            }

            if (variantValue.HasValue)
            {
                _variant = variantValue.Value;
            }
        }
    }
}
