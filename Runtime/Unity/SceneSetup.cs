using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Registers Inspector-assigned blueprints and owns one scene origin and its optional automatic views.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Scene Setup")]
    public sealed class SceneSetup : MonoBehaviour
    {
        [SerializeField] private string _originId = "default";
        [SerializeField] private Blueprint[] _blueprints = new Blueprint[0];
        [SerializeField] private bool _automaticViews = true;
        private readonly HashSet<Kind> _viewKinds = new HashSet<Kind>();
        private Origin _origin;
        private IDisposable _subscription;
        private bool _starting;
        private int _lifetime;

        /// <summary>Gets the owned origin, or null when not tracking.</summary>
        public Origin Origin
        {
            get { return _origin; }
        }

        /// <summary>Starts sources under this transform using the Inspector settings.</summary>
        /// <param name="coordinators">Application sources to attach.</param>
        /// <returns>The owned origin, also usable for source replacement.</returns>
        /// <remarks>Call once while enabled. Disabling cleans up; call again after re-enabling to restart.</remarks>
        public Origin Track(params Coordinator[] coordinators)
        {
            if ((!enabled || !gameObject.activeInHierarchy) || _starting || _origin != null)
            {
                throw new InvalidOperationException("SceneSetup must be enabled and not already tracking.");
            }
            if (string.IsNullOrEmpty(_originId))
            {
                throw new InvalidOperationException("SceneSetup requires a non-empty origin ID.");
            }
            var realm = Realm.Default;
            if (realm.ContainsOrigin(_originId))
            {
                throw new InvalidOperationException("SceneSetup requires an unused origin ID: " + _originId);
            }
            _viewKinds.Clear();
            var blueprints = _blueprints ?? new Blueprint[0];
            foreach (var blueprint in blueprints)
            {
                if (blueprint == null || !blueprint.Kind.IsValid || !_viewKinds.Add(blueprint.Kind))
                {
                    _viewKinds.Clear();
                    throw new InvalidOperationException("SceneSetup blueprints must be valid and have distinct kinds.");
                }
            }

            _starting = true;
            var lifetime = ++_lifetime;
            Origin created = null;
            try
            {
                foreach (var blueprint in blueprints)
                {
                    realm.RegisterBlueprint(blueprint);
                }
                created = realm.CreateOriginFor(_originId, transform);
                _origin = created;
                if (_automaticViews)
                {
                    _subscription = realm.Query().InOrigin(_originId).OnAvailable(ghost =>
                    {
                        if (_lifetime == lifetime && _viewKinds.Contains(ghost.Key.Kind))
                        {
                            realm.Manifest(ghost);
                        }
                    });
                }
                if (coordinators != null)
                {
                    foreach (var coordinator in coordinators)
                    {
                        if (_lifetime != lifetime || (!enabled || !gameObject.activeInHierarchy))
                        {
                            throw new InvalidOperationException("SceneSetup stopped during source startup.");
                        }
                        created.AddCoordinator(coordinator);
                    }
                }
                if (_lifetime != lifetime || (!enabled || !gameObject.activeInHierarchy))
                {
                    throw new InvalidOperationException("SceneSetup stopped during source startup.");
                }
                return created;
            }
            catch
            {
                Release();
                if (created != null)
                {
                    created.Dispose();
                }
                throw;
            }
            finally
            {
                _starting = false;
            }
        }

        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            _lifetime++;
            var subscription = _subscription;
            var origin = _origin;
            _subscription = null;
            _origin = null;
            _viewKinds.Clear();
            if (subscription != null)
            {
                subscription.Dispose();
            }
            if (origin != null)
            {
                origin.Dispose();
            }
        }
    }
}
