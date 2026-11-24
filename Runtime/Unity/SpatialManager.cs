using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Saves one reference pose, places roots in Unity, and hides out-of-range views without removing entities.
    internal sealed class SpatialManager
    {
        private readonly Realm _realm;
        private readonly IdentityMap _identities;
        private readonly HashSet<Record> _projected = new HashSet<Record>();
        private readonly HashSet<Record> _visiting = new HashSet<Record>();
        private readonly SpatialClock _clock = new SpatialClock();
        private readonly List<Record> _chain = new List<Record>();
        private Record _preparedReference;

        internal Timestamp? ProjectedTime => _clock.ProjectedTime;

        internal SpatialManager(Realm realm, IdentityMap identities)
        {
            _realm = realm;
            _identities = identities;
        }

        internal void Project(IReadOnlyList<Record> records, ReferenceFrame frame)
        {
            // Use one display time and reference for all roots, including attachments.
            double timestamp = Time.realtimeSinceStartupAsDouble;
            float interpolationDelay = _realm.InterpolationDelay;
            try
            {
                // Update the shared SDK clock before predicting any root, including the followed reference.
                UpdateClock(records, frame, timestamp, interpolationDelay);
                ReferenceFrame.Projection projection = Capture(frame, timestamp, interpolationDelay);
                for (int index = 0; index < records.Count && !_realm.IsDisposed; index++)
                {
                    ProjectChain(records[index], projection, timestamp, interpolationDelay);
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
                double timestamp = Time.realtimeSinceStartupAsDouble;
                float interpolationDelay = _realm.InterpolationDelay;
                // Use timestamps from all anchors and the followed reference, even when updating only one root.
                UpdateClock(_identities.Snapshot(), frame, timestamp, interpolationDelay);
                ProjectChain(record, Capture(frame, timestamp, interpolationDelay), timestamp, interpolationDelay);
            }
            finally
            {
                ClearProjection();
            }
        }

        internal void ResetTime(IReadOnlyList<Record> records)
        {
            // When the SDK clock restarts, reset the shared clock and every root's stored history together.
            _clock.Reset();
            double timestamp = Time.realtimeSinceStartupAsDouble;
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                Record record = records[recordIndex];
                if (record.Ghost != null)
                {
                    Spatial spatial = record.Ghost.GetComponent<Spatial>();
                    if (spatial != null)
                    {
                        spatial.ResetTime(timestamp);
                    }
                }
            }
        }

        internal void RefreshSuppression(IReadOnlyList<Record> records)
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
                // Enable and destroy callbacks can add roots after this placement pass.
                spatial.SetInRange(false);
            }
        }

        private ReferenceFrame.Projection Capture(ReferenceFrame frame, double timestamp, float interpolationDelay)
        {
            // Predict the followed root first, then save its reference for placing all other entities.
            if (frame != null && frame.FollowedGhost.HasValue)
            {
                Record reference;
                Spatial spatial = null;
                if (_identities.TryGetValue(frame.FollowedGhost.Value, out reference) && CanProject(reference))
                {
                    spatial = reference.Ghost.GetComponent<Spatial>();
                }

                if (spatial != null)
                {
                    spatial.PreparePresentation(_clock, timestamp, interpolationDelay, frame.Coordinates);
                    _preparedReference = reference;
                }

                frame.UpdateFollowedPose(spatial);
            }

            return frame == null
                ? new ReferenceFrame.Projection(true, default(Double3), Vector3.zero, Quaternion.identity, CoordinateSystem.Unity, null)
                : frame.Capture();
        }

        private void PreparePresentation(Record record, double timestamp, float interpolationDelay, ReferenceFrame.Projection projection)
        {
            // Place the reference using the exact presentation pose already saved in the captured frame.
            if (CanProject(record) && record != _preparedReference)
            {
                Spatial spatial = record.Ghost.GetComponent<Spatial>();
                if (spatial != null)
                {
                    double? distance = record.Ghost.Anchor.PoseProcessingDistance;
                    bool directPose = distance.HasValue && spatial.HasPosition
                        && !projection.IsWithinDistance(spatial.Position, distance.Value);
                    spatial.PreparePresentation(_clock, timestamp, interpolationDelay, projection.Coordinates, directPose);
                }
            }
        }

        private void UpdateClock(IReadOnlyList<Record> records, ReferenceFrame frame, double timestamp, float interpolationDelay)
        {
            Spatial reference = null;
            Record record;
            if (frame != null && frame.FollowedGhost.HasValue
                && _identities.TryGetValue(frame.FollowedGhost.Value, out record) && CanProject(record))
            {
                reference = record.Ghost.GetComponent<Spatial>();
                if (reference != null && (!reference.enabled || (!reference.PositionTime.HasValue && !reference.RotationTime.HasValue)))
                {
                    reference = null;
                }
            }

            // Clock policy belongs to the reference; the followed Ghost supplies only observations.
            _clock.SetReference(reference, frame != null ? frame.TimestampClock : TimestampClock.Automatic, frame != null ? frame.ClockOffset : 0d);
            Timestamp? sampleTime = null;
            double receivedTime = 0;
            float predictionTime = 0;
            if (reference != null)
            {
                // Use one observation per reference packet, preferring the newest independent channel.
                // Equal channel timestamps use the first receipt instead of counting the packet twice.
                bool rotationNewest = reference.RotationTime.HasValue
                    && (!reference.PositionTime.HasValue || reference.RotationTime.Value.CompareTo(reference.PositionTime.Value) > 0);
                sampleTime = rotationNewest ? reference.RotationTime : reference.PositionTime;
                receivedTime = rotationNewest ? reference.RotationReceivedTime : reference.PositionReceivedTime;
                if (reference.PositionTime.HasValue && Nullable.Equals(reference.PositionTime, reference.RotationTime))
                {
                    receivedTime = Math.Min(reference.PositionReceivedTime, reference.RotationReceivedTime);
                }

                double utcCapturedAt = Time.realtimeSinceStartupAsDouble;
                _clock.ObserveReference(sampleTime.Value, receivedTime, utcCapturedAt, Timestamp.UtcNow());
                predictionTime = frame.PredictionTime;
            }
            if (!_clock.HasUnixClock)
            {
                ObserveTimes(records);
            }

            _clock.Capture(timestamp, interpolationDelay, predictionTime, sampleTime, receivedTime);
        }

        private void ObserveTimes(IReadOnlyList<Record> records)
        {
            // Use any valid pose timestamp to improve the shared clock, regardless of anchor or pose type.
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                Record record = records[recordIndex];
                if (CanProject(record))
                {
                    Spatial spatial = record.Ghost.GetComponent<Spatial>();
                    if (spatial != null && spatial.enabled)
                    {
                        if (spatial.PositionTime.HasValue)
                        {
                            _clock.Observe(spatial.PositionTime.Value, spatial.PositionReceivedTime);
                        }

                        if (spatial.RotationTime.HasValue)
                        {
                            _clock.Observe(spatial.RotationTime.Value, spatial.RotationReceivedTime);
                        }
                    }
                }
            }
        }

        private void ClearProjection()
        {
            _projected.Clear();
            _visiting.Clear();
            _chain.Clear();
            _preparedReference = null;
        }

        private void ProjectChain(Record record, ReferenceFrame.Projection projection, double timestamp, float interpolationDelay)
        {
            // Follow parent links and remember visited records. This detects loops without recursive calls.
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

            // Place parents first, regardless of discovery order. Avoid recursive calls for long parent chains.
            // If parent links form a loop, hide those roots and their children until the loop is fixed.
            for (int index = _chain.Count - 1; index >= 0 && !_realm.IsDisposed; index--)
            {
                Record item = _chain[index];
                PreparePresentation(item, timestamp, interpolationDelay, projection);
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
            // If one root cannot be placed, keep it tracked so a later update can recover.
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

            // Update view visibility only when the root's display state changes.
            if (_identities.Contains(record) && record.SpatialVisible != visible)
            {
                record.SpatialVisible = visible;
                record.ViewVersion++;
                record.ViewDirty = true;
            }
        }

        private bool CanProject(Record record)
        {
            // Scene callbacks can remove roots or transfer detector ownership during the same pass.
            return !_realm.IsDisposed && _identities.Contains(record) && record.Ghost != null && record.Owner != null
                && record.Owner.IsRegistration(_realm, record.RegistrationGeneration)
                && (record.Ghost.IsAvailable || record.PendingActivation);
        }
    }
}
