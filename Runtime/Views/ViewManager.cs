using UnityEngine;

namespace Emas
{
    // Handles scene changes. Enabling or disabling an object can call application code.
    internal sealed class ViewManager
    {
        private readonly IdentityMap _identities;
        private readonly SceneChangeQueue _sceneChanges;

        internal ViewManager(IdentityMap identities, SceneChangeQueue sceneChanges)
        {
            _identities = identities;
            _sceneChanges = sceneChanges;
        }

        internal void Request(Record record)
        {
            record.ViewVersion++;
            record.ViewDirty = true;
            record.ViewRequested = true;
        }

        internal void Cancel(Record record)
        {
            record.ViewVersion++;
            record.ViewDirty = false;
            record.ViewRequested = false;
            Destroy(record);
        }

        internal void Refresh(Record record)
        {
            if (record.RefreshingView || !_identities.Contains(record) || record.Ghost == null)
            {
                return;
            }

            record.RefreshingView = true;
            // Save this request's version so we can stop if a scene callback cancels or replaces it.
            long version = record.ViewVersion;
            record.ViewDirty = false;
            string context = "view refresh for " + record.Key;
            try
            {
                // Keep the request while the view is hidden so it can be shown again when ready.
                if (!record.ViewRequested || !record.SpatialVisible)
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

                // Use the saved blueprint settings. Asset edits take effect after registration is repeated.
                GameObject prefab = record.ManifestationBlueprint.ResolveViewPrefab(record.Ghost.Variant);
                if (prefab == null)
                {
                    Destroy(record);
                    if (record.ManifestationBlueprint.HasManifestationPrefab)
                    {
                        Debug.LogWarning("No Emas view prefab resolves for ghost " + record.Key + " with variant '" + record.Ghost.Variant + "'.");
                    }

                    return;
                }

                context += " (prefab '" + prefab.name + "')";

                // Reuse the child when the requested appearance uses the same prefab.
                if (record.View != null && record.ViewPrefab == prefab)
                {
                    record.View.Bind(record.Ghost);
                    if (record.Ghost.gameObject.activeInHierarchy)
                    {
                        _sceneChanges.SetActive(record.View.gameObject, true);
                    }

                    return;
                }

                // Destroying the old child can run OnDisable, which may change the request before we create its replacement.
                Destroy(record);
                if (!CanContinue(record, version))
                {
                    return;
                }

                CreateView(record, prefab, version);
            }
            catch (System.Exception exception)
            {
                // Report this view's failure without stopping the source.
                // Keep the request so changing the request or appearance can try again.
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
            // Clear the saved view before OnDisable can request another one for the same root.
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

        private void CreateView(Record record, GameObject prefab, long version)
        {
            // Create under an inactive parent so OnEnable waits until the view is connected to its Ghost.
            // Refresh blocks nested refreshes while this method cleans up any failed creation.
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

                // Connect the view under the inactive parent, then move it to the root. Enable it after saving its details.
                view.Bind(record.Ghost);
                instance.name = prefab.name;
                instance.transform.SetParent(record.Ghost.transform, false);
                if (!CanContinue(record, version))
                {
                    Object.Destroy(instance);
                    return;
                }

                // Save the instance before OnEnable can query or cancel its own view.
                record.ViewPrefab = prefab;
                record.View = view;
                if (record.Ghost.gameObject.activeInHierarchy)
                {
                    _sceneChanges.SetActive(instance, true);
                }

                // OnEnable may remove the record, destroy the view or change the request.
                if (!_identities.Contains(record) && instance != null)
                {
                    _sceneChanges.Destroy(instance);
                }
            }
            catch
            {
                // Remove only this failed instance. A callback may already have created a different view for this root.
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

        private bool CanContinue(Record record, long version)
        {
            return _identities.Contains(record) && record.Ghost != null && record.Ghost.IsAvailable
                && record.ViewVersion == version && record.ViewRequested && record.SpatialVisible;
        }
    }
}
