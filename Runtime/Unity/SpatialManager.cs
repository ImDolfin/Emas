using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    internal sealed class SpatialManager
    {
        private readonly Realm _realm;
        private readonly Registry _ghosts;

        internal SpatialManager(Realm realm, Registry ghosts)
        {
            _realm = realm;
            _ghosts = ghosts;
        }

        private ReferenceFrame.Projection Capture(ReferenceFrame frame)
        {
            if (frame != null && frame.FollowedGhost.HasValue)
            {
                Record reference;
                Spatial spatial = null;
                if (_ghosts.TryGetValue(frame.FollowedGhost.Value, out reference) && CanProject(reference))
                {
                    spatial = reference.Ghost.GetComponent<Spatial>();
                }

                frame.UpdateFollowedPose(spatial);
            }

            return frame == null ? default(ReferenceFrame.Projection) : frame.Capture();
        }

        internal void Project(List<Record> records, ReferenceFrame frame)
        {
            ReferenceFrame.Projection projection = Capture(frame);
            for (int index = 0; index < records.Count && !_realm.IsDisposed; index++)
            {
                Project(records[index], frame != null, projection);
            }
        }

        internal void Project(Record record, ReferenceFrame frame)
        {
            Project(record, frame != null, Capture(frame));
        }

        private void Project(Record record, bool enabled, ReferenceFrame.Projection projection)
        {
            if (!CanProject(record))
            {
                return;
            }

            Spatial spatial = record.Ghost.GetComponent<Spatial>();
            bool visible = true;
            try
            {
                if (enabled && spatial != null && spatial.enabled)
                {
                    Vector3 position = default(Vector3);
                    visible = spatial.HasPosition && projection.TryToUnityPosition(spatial.Position, out position);
                    if (visible)
                    {
                        // Assign world pose so existing anchor transforms do not introduce a second offset.
                        if (spatial.HasRotation)
                        {
                            record.Ghost.transform.SetPositionAndRotation(position, projection.ToUnityRotation(spatial.Rotation));
                        }
                        else
                        {
                            record.Ghost.transform.position = position;
                        }
                    }

                    spatial.SetInRange(visible);
                }
                else if (spatial != null)
                {
                    spatial.SetInRange(true);
                }
            }
            catch (Exception exception)
            {
                visible = false;
                PresenceSource.LogError(exception, "spatial projection for " + record.Key);
            }

            if (_ghosts.Contains(record) && record.SpatialVisible != visible)
            {
                record.SpatialVisible = visible;
                record.ViewVersion++;
                record.ViewDirty = true;
            }
        }

        internal void RefreshSuppression(List<Record> records)
        {
            for (int index = 0; index < records.Count && !_realm.IsDisposed; index++)
            {
                RefreshSuppression(records[index]);
            }
        }

        internal void RefreshSuppression(Record record)
        {
            if (!CanProject(record) || record.SpatialVisible)
            {
                return;
            }

            Spatial spatial = record.Ghost.GetComponent<Spatial>();
            if (spatial != null && spatial.enabled)
            {
                // Activation/destruction callbacks can add presentation after the projection pass.
                spatial.SetInRange(false);
            }
        }

        private bool CanProject(Record record)
        {
            return _ghosts.Contains(record) && record.Ghost != null && record.Owner != null
                && record.Owner.IsRegistration(_realm, record.RegistrationGeneration)
                && (record.Ghost.IsAvailable || record.PendingActivation);
        }
    }
}
