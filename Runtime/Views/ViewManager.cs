using UnityEngine;

namespace Emas
{
    // Scene changes are isolated here; every activation/deactivation can reenter application code.
    internal sealed class ViewManager
    {
        private readonly Registry _ghosts;
        private readonly SceneChangeQueue _sceneChanges;

        internal ViewManager(Registry ghosts, SceneChangeQueue sceneChanges)
        {
            _ghosts = ghosts;
            _sceneChanges = sceneChanges;
        }

        internal void Refresh(Record record)
        {
            if (record.RefreshingView || !_ghosts.Contains(record) || record.Ghost == null)
            {
                return;
            }

            record.RefreshingView = true;
            long version = record.ViewVersion;
            record.ViewDirty = false;
            string context = "view refresh for " + record.Key;
            try
            {
                if (!record.ViewRequested || record.RequestedDetailLevel.Level <= 0 || !record.SpatialVisible)
                {
                    Destroy(record);
                    return;
                }

                if (!record.Ghost.IsAvailable)
                {
                    return;
                }

                if (record.ManifestationBlueprint == null || record.ManifestationBlueprint.Asset == null)
                {
                    Destroy(record);
                    return;
                }

                GameObject prefab = record.ManifestationBlueprint.ResolveViewPrefab(record.Ghost.Variant, record.RequestedDetailLevel);
                if (prefab == null)
                {
                    Destroy(record);
                    if (record.ManifestationBlueprint.HasManifestationPrefab)
                    {
                        Debug.LogWarning("No Emas view prefab resolves for ghost " + record.Key + " at detail level " + record.RequestedDetailLevel + ".");
                    }
                    return;
                }

                context += " (prefab '" + prefab.name + "', detail " + record.RequestedDetailLevel + ")";

                // Rebind the existing child when the requested appearance resolves to the same prefab.
                if (record.View != null && record.ViewPrefab == prefab)
                {
                    record.View.Bind(record.Ghost, record.RequestedDetailLevel);
                    if (record.Ghost.gameObject.activeInHierarchy)
                    {
                        _sceneChanges.SetActive(record.View.gameObject, true);
                    }

                    return;
                }

                Destroy(record);
                if (!CanContinue(record, version))
                {
                    return;
                }

                // Bind the ghost before activation lets view components consume its data.
                GameObject staging = null;
                GameObject instance = null;
                try
                {
                    staging = new GameObject("[Emas View Staging]");
                    staging.SetActive(false);
                    staging.transform.SetParent(record.Ghost.transform, false);
                    instance = Object.Instantiate(prefab, staging.transform, false);
                    instance.SetActive(false);
                    View view = instance.GetComponent<View>();
                    if (view == null)
                    {
                        view = instance.AddComponent<View>();
                    }

                    view.Bind(record.Ghost, record.RequestedDetailLevel);
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
                        _sceneChanges.SetActive(instance, true);
                    }

                    // OnEnable may remove the record, destroy the view or change the request.
                    if (!_ghosts.Contains(record) && instance != null)
                    {
                        _sceneChanges.Destroy(instance);
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
                        _sceneChanges.Destroy(instance);
                    }

                    throw;
                }
                finally
                {
                    if (staging != null)
                    {
                        Object.Destroy(staging);
                    }
                }
            }
            catch (System.Exception exception)
            {
                // Presentation failures belong to this view, not to the source population.
                // Keep the request so a new request or appearance change can retry it.
                Destroy(record);
                PresenceDetector.LogError(exception, context);
            }
            finally
            {
                record.RefreshingView = false;
            }
        }

        internal void Destroy(Record record)
        {
            View view = record.View;
            record.View = null;
            record.ViewPrefab = null;
            try
            {
                if (view != null)
                {
                    GameObject instance = view.gameObject;
                    _sceneChanges.Destroy(instance);
                }
            }
            catch (System.Exception exception)
            {
                PresenceDetector.LogError(exception, "view removal for " + record.Key);
            }
        }

        private bool CanContinue(Record record, long version)
        {
            return _ghosts.Contains(record) && record.Ghost != null && record.Ghost.IsAvailable
                && record.ViewVersion == version && record.ViewRequested && record.SpatialVisible;
        }
    }
}
