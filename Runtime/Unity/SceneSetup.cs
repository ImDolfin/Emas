using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    /// <summary>Registers Inspector-assigned blueprints and owns one scene anchor and its optional automatic views.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Emas/Scene Setup")]
    public sealed class SceneSetup : MonoBehaviour
    {
        [SerializeField] private string _anchorId = "default";
        [SerializeField] private Blueprint[] _blueprints = new Blueprint[0];
        [SerializeField] private bool _automaticViews = true;
        private readonly HashSet<Kind> _viewKinds = new HashSet<Kind>();
        private Anchor _anchor;
        private IDisposable _subscription;
        private bool _starting;
        private int _lifetime;

        /// <summary>Gets the owned anchor, or null when not tracking.</summary>
        public Anchor Anchor
        {
            get { return _anchor; }
        }

        /// <summary>Starts sources under this transform using the Inspector settings.</summary>
        /// <param name="coordinators">Application sources to attach.</param>
        /// <returns>The owned anchor, also usable for source replacement.</returns>
        /// <remarks>Call once while enabled. Disabling cleans up; call again after re-enabling to restart.</remarks>
        public Anchor Track(params Coordinator[] coordinators)
        {
            if ((!enabled || !gameObject.activeInHierarchy) || _starting || _anchor != null)
            {
                throw new InvalidOperationException("SceneSetup must be enabled and not already tracking.");
            }
            if (string.IsNullOrEmpty(_anchorId))
            {
                throw new InvalidOperationException("SceneSetup requires a non-empty anchor ID.");
            }
            var realm = Realm.Default;
            if (realm.ContainsAnchor(_anchorId))
            {
                throw new InvalidOperationException("SceneSetup requires an unused anchor ID: " + _anchorId);
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
            Anchor created = null;
            try
            {
                foreach (var blueprint in blueprints)
                {
                    realm.RegisterBlueprint(blueprint);
                }
                created = realm.CreateAnchorFor(_anchorId, transform);
                _anchor = created;
                if (_automaticViews)
                {
                    _subscription = realm.Query().InAnchor(_anchorId).OnAvailable(ghost =>
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
            var anchor = _anchor;
            _subscription = null;
            _anchor = null;
            _viewKinds.Clear();
            if (subscription != null)
            {
                subscription.Dispose();
            }
            if (anchor != null)
            {
                anchor.Dispose();
            }
        }
    }
}
