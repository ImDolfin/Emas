using UnityEngine;

namespace Emas
{
    // Scene effects are isolated here; every activation/deactivation can reenter application code.
    internal sealed class ViewManager
    {
        private readonly Registry _ghosts;
        private readonly SceneEffects _scene;

        internal ViewManager(Registry ghosts, SceneEffects scene)
        {
            _ghosts = ghosts;
            _scene = scene;
        }

        internal void Refresh(Record record)
        {
            if (record.RefreshingView || !_ghosts.Contains(record) || record.Ghost == null)
            {
                return;
            }
            record.RefreshingView = true;
            var version = record.ViewVersion;
            record.ViewDirty = false;
            try
            {
                if (!record.ViewRequested || record.RequestedDegree.Level <= 0)
                {
                    Destroy(record);
                    return;
                }
                if (!record.Ghost.IsAvailable || record.Blueprint == null)
                {
                    return;
                }
                var prefab = record.Blueprint.GetView(record.Ghost.Variant, record.RequestedDegree);
                if (prefab == null)
                {
                    Destroy(record);
                    Debug.LogWarning("No Emas view prefab resolves for ghost " + record.Key + " at degree " + record.RequestedDegree + ".");
                    return;
                }
                if (record.View != null && record.ViewPrefab == prefab)
                {
                    record.View.Bind(record.Ghost, record.RequestedDegree);
                    if (record.Ghost.gameObject.activeInHierarchy)
                    {
                        _scene.SetActive(record.View.gameObject, true);
                    }
                    return;
                }
                Destroy(record);
                if (!CanContinue(record, version))
                {
                    return;
                }
                var staging = new GameObject("[Emas View Staging]");
                staging.SetActive(false);
                staging.transform.SetParent(record.Ghost.transform, false);
                GameObject instance = null;
                try
                {
                    instance = Object.Instantiate(prefab, staging.transform, false);
                    instance.SetActive(false);
                    var view = instance.GetComponent<View>();
                    if (view == null)
                    {
                        view = instance.AddComponent<View>();
                    }
                    view.Bind(record.Ghost, record.RequestedDegree);
                    instance.name = prefab.name;
                    instance.transform.SetParent(record.Ghost.transform, false);
                    if (!CanContinue(record, version))
                    {
                        Object.Destroy(instance);
                        return;
                    }
                    record.ViewPrefab = prefab;
                    record.View = view;
                    if (record.Ghost.gameObject.activeInHierarchy)
                    {
                        _scene.SetActive(instance, true);
                    }
                    // OnEnable may remove the record, destroy the view or change the request.
                    if (!_ghosts.Contains(record) && instance != null)
                    {
                        _scene.Destroy(instance);
                    }
                }
                catch
                {
                    if (record.View != null && record.View.gameObject == instance)
                    {
                        record.View = null;
                        record.ViewPrefab = null;
                    }
                    if (instance != null)
                    {
                        _scene.Destroy(instance);
                    }
                    throw;
                }
                finally
                {
                    Object.Destroy(staging);
                }
            }
            finally
            {
                record.RefreshingView = false;
            }
        }

        internal void Destroy(Record record)
        {
            var view = record.View;
            record.View = null;
            record.ViewPrefab = null;
            if (view != null)
            {
                var instance = view.gameObject;
                _scene.Destroy(instance);
            }
        }

        private bool CanContinue(Record record, long version)
        {
            return _ghosts.Contains(record) && record.Ghost != null && record.Ghost.IsAvailable
                && record.ViewVersion == version && record.ViewRequested;
        }
    }
}
