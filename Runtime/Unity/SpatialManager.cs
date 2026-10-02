using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Captures one reference pose, projects eligible roots, and coordinates view suppression without changing availability.
    internal sealed class SpatialManager
    {
        private readonly Realm _realm;
        private readonly IdentityMap _identities;
        private readonly HashSet<Record> _projected = new HashSet<Record>();
        private readonly HashSet<Record> _visiting = new HashSet<Record>();
        private readonly List<Record> _chain = new List<Record>();

        internal SpatialManager(Realm realm, IdentityMap identities)
        {
            _realm = realm;
            _identities = identities;
        }

        private ReferenceFrame.Projection Capture(ReferenceFrame frame)
        {
            if (frame != null && frame.FollowedGhost.HasValue)
            {
                Record reference;
                Spatial spatial = null;
                if (_identities.TryGetValue(frame.FollowedGhost.Value, out reference) && CanProject(reference))
                {
                    spatial = reference.Ghost.GetComponent<Spatial>();
                }

                frame.UpdateFollowedPose(spatial);
            }

            return frame == null
                ? new ReferenceFrame.Projection(true, default(Double3), Vector3.zero, Quaternion.identity, CoordinateSystem.Unity, null)
                : frame.Capture();
        }

        internal void Project(List<Record> records, ReferenceFrame frame)
        {
            ReferenceFrame.Projection projection = Capture(frame);
            try
            {
                for (int index = 0; index < records.Count && !_realm.IsDisposed; index++)
                {
                    ProjectChain(records[index], projection);
                }
            }
            finally
            {
                ClearProjection();
            }
        }

        internal void Project(Record record, ReferenceFrame frame)
        {
            try
            {
                ProjectChain(record, Capture(frame));
            }
            finally
            {
                ClearProjection();
            }
        }

        private void ClearProjection()
        {
            _projected.Clear();
            _visiting.Clear();
            _chain.Clear();
        }

        private void ProjectChain(Record record, ReferenceFrame.Projection projection)
        {
            _chain.Clear();
            _visiting.Clear();
            Record current = record;
            while (CanProject(current) && !_projected.Contains(current) && _visiting.Add(current))
            {
                _chain.Add(current);
                Spatial spatial = current.Ghost.GetComponent<Spatial>();
                if (spatial == null || !spatial.enabled || !spatial.AttachedTo.HasValue
                    || !_identities.TryGetValue(spatial.AttachedTo.Value, out current))
                {
                    break;
                }
            }

            // Resolve ancestors first, independent of discovery order, without recursion for deep chains.
            // A cycle has no projected ancestor: its members and descendants remain hidden until the cycle is broken.
            for (int index = _chain.Count - 1; index >= 0 && !_realm.IsDisposed; index--)
            {
                Record item = _chain[index];
                Project(item, projection);
                _projected.Add(item);
            }
        }

        private void Project(Record record, ReferenceFrame.Projection projection)
        {
            if (!CanProject(record))
            {
                return;
            }

            Spatial spatial = record.Ghost.GetComponent<Spatial>();
            bool visible = true;
            try
            {
                if (spatial != null && spatial.enabled)
                {
                    if (spatial.AttachedTo.HasValue)
                    {
                        Record parent;
                        visible = _identities.TryGetValue(spatial.AttachedTo.Value, out parent)
                            && CanProject(parent) && _projected.Contains(parent) && parent.SpatialVisible
                            && spatial.ApplyAttachment(projection, parent.Ghost.transform);
                        if (!visible)
                        {
                            spatial.SetInRange(false);
                        }
                    }
                    else
                    {
                        visible = spatial.ApplyProjection(projection);
                    }
                }
                else if (spatial != null)
                {
                    spatial.SetInRange(true);
                }
            }
            catch (Exception exception)
            {
                visible = false;
                PresenceDetector.LogError(exception, "spatial projection for " + record.Key);
            }

            if (_identities.Contains(record) && record.SpatialVisible != visible)
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
            return !_realm.IsDisposed && _identities.Contains(record) && record.Ghost != null && record.Owner != null
                && record.Owner.IsRegistration(_realm, record.RegistrationGeneration)
                && (record.Ghost.IsAvailable || record.PendingActivation);
        }
    }
}
