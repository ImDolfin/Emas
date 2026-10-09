using UnityEngine;

namespace Emas
{
    // Stores raw channels independently of presentation; duplicate and older timed packets are idempotent.
    internal sealed class PoseData
    {
        internal Double3 Position { get; private set; }
        internal Quaternion Rotation { get; private set; } = Quaternion.identity;
        internal bool HasPosition { get; private set; }
        internal bool HasRotation { get; private set; }
        internal Timestamp? PositionTime { get; private set; }
        internal Timestamp? RotationTime { get; private set; }
        internal double PositionReceivedTime { get; private set; }
        internal double RotationReceivedTime { get; private set; }
        internal long PositionVersion { get; private set; }

        internal bool SetPosition(Double3 position, Timestamp? sampleTime, double receivedTime)
        {
            ReferenceFrame.ValidatePosition(position, nameof(position));
            if (!Accepts(PositionTime, sampleTime))
            {
                return false;
            }

            // Fresh stationary observations refresh prediction age. Re-reading an unchanged untimed cache does not.
            if (!HasPosition || Position != position || sampleTime.HasValue || PositionTime.HasValue)
            {
                PositionReceivedTime = receivedTime;
                PositionVersion++;
            }

            Position = position;
            PositionTime = sampleTime;
            HasPosition = true;
            return true;
        }

        internal bool SetRotation(Quaternion rotation, Timestamp? sampleTime, double receivedTime)
        {
            if (!Accepts(RotationTime, sampleTime))
            {
                return false;
            }

            Rotation = rotation;
            RotationTime = sampleTime;
            HasRotation = true;
            RotationReceivedTime = receivedTime;
            return true;
        }

        internal void ResetTime(double receivedTime)
        {
            PositionTime = null;
            RotationTime = null;
            PositionReceivedTime = receivedTime;
            RotationReceivedTime = receivedTime;
            PositionVersion++;
        }

        internal static bool Accepts(Timestamp? previous, Timestamp? incoming)
        {
            // Ordering applies only within a timed channel; callers may intentionally switch to or from untimed input.
            return !previous.HasValue || !incoming.HasValue || incoming.Value.CompareTo(previous.Value) > 0;
        }
    }
}
