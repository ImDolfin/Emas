using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>
    /// Identifies an entity root and exposes contracts provided by its components.
    /// </summary>

    /// <remarks>
    /// Use this component directly on an authored prefab with reusable Trait components.
    /// Derive a custom Ghost only when the entity needs additional behavior; subclasses can use RequireComponent.
    /// Traits and other root components can implement read-only application contracts. Emas creates/destroys roots and sets their metadata.
    /// Created root GameObjects are named by Key.EntityId before application initialization; Name is separate display metadata.
    /// Roots activate after successful publication and deactivate on availability loss; Awake may run before source mapping.
    /// Initializers map source data to the configured traits. Views are optional children, independent of root behaviors.
    /// </remarks>
    [AddComponentMenu("Emas/Ghost")]
    public class Ghost : MonoBehaviour, IGhost
    {
        [SerializeField, HideInInspector]
        private string _anchorId;
        [SerializeField, HideInInspector]
        private string _entityId;
        [SerializeField, HideInInspector]
        private string _kindId;
        [SerializeField, HideInInspector]
        private string _name;
        [SerializeField, HideInInspector]
        private Variant _variant;
        [SerializeField, HideInInspector]
        private bool _isAvailable;
        private GhostPartResolver _partResolver;
        private List<Trait> _traitBuffer;
        private IReadOnlyList<Trait> _traits = Array.AsReadOnly(Array.Empty<Trait>());

        /// <inheritdoc />
        public Key Key
        {
            get
            {
                Kind kind = string.IsNullOrEmpty(_kindId) ? default(Kind) : new Kind(_kindId);
                return new Key(_anchorId, kind, _entityId);
            }
        }

        /// <inheritdoc />
        public string Name
        {
            get
            {
                return _name;
            }
        }

        /// <inheritdoc />
        public Variant Variant
        {
            get
            {
                return _variant;
            }
        }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get
            {
                return _isAvailable;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<Trait> Traits
        {
            get
            {
                if (_traitBuffer == null)
                {
                    _traitBuffer = new List<Trait>();
                }

                GetComponents(_traitBuffer);
                bool changed = _traitBuffer.Count != _traits.Count;
                for (int index = 0; !changed && index < _traitBuffer.Count; index++)
                {
                    changed = !ReferenceEquals(_traitBuffer[index], _traits[index]);
                }

                if (changed)
                {
                    // Replace the snapshot so trait readers can inspect a changed root without altering an active pass.
                    _traits = Array.AsReadOnly(_traitBuffer.ToArray());
                }

                return _traits;
            }
        }

        /// <inheritdoc />
        public bool TryGet<T>(out T part) where T : class
        {
            if (_partResolver == null)
            {
                _partResolver = new GhostPartResolver();
            }

            return _partResolver.TryGet(this, out part);
        }

        /// <summary>
        /// Runs entity-specific behavior after all trait readers and before spatial projection.
        /// </summary>
        /// <remarks>
        /// The realm calls this once per Update for each enabled, owned Ghost that is available or awaiting activation.
        /// It may run before the root's first activation. Prepared or disappeared Ghosts do not update.
        /// Startup finalization and view requests do not invoke this hook. Ordering between Ghost hooks is unspecified.
        /// An exception stops the owning detector and removes its population, as with trait update failures.
        /// </remarks>
        protected virtual void OnUpdate()
        {
        }

        internal void Tick()
        {
            OnUpdate();
        }

        /// <summary>
        /// Initializes Emas-owned identity and metadata.
        /// </summary>
        /// <param name="key">
        /// The ghost key.
        /// </param>
        /// <param name="nameValue">
        /// The display name.
        /// </param>
        /// <param name="variantValue">
        /// The visual variant.
        /// </param>
        internal void Initialize(Key key, string nameValue, Variant variantValue)
        {
            _anchorId = key.AnchorId;
            _entityId = key.EntityId;
            _kindId = key.Kind.Id;
            _name = nameValue ?? key.EntityId;
            _variant = variantValue;
            _isAvailable = false;
            if (_partResolver != null)
            {
                _partResolver.Reset();
            }
        }

        /// <summary>
        /// Sets the source availability state.
        /// </summary>
        /// <param name="available">
        /// The new availability state.
        /// </param>
        internal void SetAvailable(bool available)
        {
            _isAvailable = available;
        }

        /// <summary>
        /// Updates source-provided display metadata.
        /// </summary>
        /// <param name="nameValue">
        /// The new name, or null to retain the current name.
        /// </param>
        /// <param name="variantValue">
        /// The new variant, or null to retain the current variant.
        /// </param>
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
