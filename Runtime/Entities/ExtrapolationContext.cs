using UnityEngine;

namespace Emas
{
    /// <summary>Provides pose, motion, timing and coordinate information for one channel's extrapolation.</summary>
    /// <remarks>Created by Emas on the main thread. Motion is sampled at ObservationTime independently
    /// for position and rotation extrapolation; future-only motion is unavailable. Pose properties retain
    /// the latest accepted observations and their own timestamps, which may differ from ObservationTime.
    /// Null motion means unavailable; a supplied zero remains available and overrides estimation.</remarks>
    public readonly struct ExtrapolationContext
    {
        /// <summary>Gets the latest accepted source position.</summary>
        /// <value>Cartesian source distance units or ECEF metres; null before position input.</value>
        public Double3? Position { get; }

        /// <summary>Gets the latest accepted orientation.</summary>
        /// <value>A normalized quaternion represented by RotationSpace; null before orientation input.</value>
        public Quaternion? Rotation { get; }

        /// <summary>Gets the original timestamp of Position.</summary>
        /// <value>Shared SDK time, or null for an untimed or unavailable position.</value>
        public Timestamp? PositionTime { get; }

        /// <summary>Gets the original timestamp of Rotation.</summary>
        /// <value>Shared SDK time, or null for an untimed or unavailable orientation.</value>
        public Timestamp? RotationTime { get; }

        /// <summary>Gets the timestamp from which this channel is being extrapolated.</summary>
        /// <value>PositionTime for linear extrapolation, RotationTime for angular extrapolation, or null for untimed input.</value>
        public Timestamp? ObservationTime { get; }

        /// <summary>Gets position sampled at ObservationTime for body-frame and geographic calculations.</summary>
        /// <value>A buffered position, the newest position when no later observation exists, or null when only future positions are available.</value>
        public Double3? ObservationPosition { get; }

        /// <summary>Gets orientation sampled at ObservationTime for coupled body-frame models.</summary>
        /// <value>An interpolated or held orientation in RotationSpace, or null when only future orientations are available.</value>
        /// <remarks>Unlike Rotation, this property excludes future-only timed attitude. Untimed attitude is treated as current.</remarks>
        public Quaternion? ObservationRotation { get; }

        /// <summary>Gets velocity sampled at ObservationTime, or the available position-derived estimate.</summary>
        /// <value>Source distance units per second, or ECEF metres per second; null when unavailable.</value>
        public Double3? Velocity { get; }

        /// <summary>Gets optional acceleration sampled at ObservationTime.</summary>
        /// <value>Source distance units per second squared, or ECEF metres per second squared; null when unavailable.</value>
        public Double3? Acceleration { get; }

        /// <summary>Gets angular velocity sampled at ObservationTime, or an observed rotation-arc estimate.</summary>
        /// <value>Radians per second with Quaternion.AngleAxis signs; null when unavailable.</value>
        /// <remarks>Body rates use model right/up/forward axes. World rates use ECEF axes for EarthCentered
        /// rotation, otherwise local east/up/north axes. Emas converts the returned offset into RotationSpace.</remarks>
        public Double3? AngularVelocity { get; }

        /// <summary>Gets whether Velocity was estimated from timed positions rather than supplied by the SDK.</summary>
        /// <value>True only when a position-derived velocity is available.</value>
        public bool VelocityIsEstimated { get; }

        /// <summary>Gets whether AngularVelocity was estimated from timed rotations rather than supplied by the SDK.</summary>
        /// <value>True only when an observed-arc rate is available.</value>
        public bool AngularVelocityIsEstimated { get; }

        /// <summary>Gets whether angular velocity is expressed in world axes.</summary>
        /// <value>True for world rates composed before attitude; false for body rates composed after attitude.</value>
        public bool AngularVelocityIsWorld { get; }

        /// <summary>Gets the newest accepted SDK velocity timestamp, independently of the sampling time.</summary>
        /// <value>The supplied channel's latest timestamp; null when no timed SDK velocity is retained.
        /// May be later than ObservationTime; Velocity still excludes future-only observations.</value>
        public Timestamp? LatestVelocityTime { get; }

        /// <summary>Gets the newest accepted SDK acceleration timestamp, independently of the sampling time.</summary>
        /// <value>The supplied channel's latest timestamp, or null for untimed or cleared acceleration.</value>
        public Timestamp? LatestAccelerationTime { get; }

        /// <summary>Gets the newest accepted SDK angular-velocity timestamp, independently of the sampling time.</summary>
        /// <value>The supplied channel's latest timestamp, or null when no timed SDK angular velocity is retained.</value>
        public Timestamp? LatestAngularVelocityTime { get; }

        /// <summary>Gets the representation of the observed orientation.</summary>
        /// <value>Source axes, local east/up/north or body-to-ECEF.</value>
        public RotationSpace RotationSpace { get; }

        /// <summary>Gets the Realm's position reference space.</summary>
        /// <value>Cartesian for shared source axes and units, or Geographic for ECEF positions and linear motion in metres.</value>
        public ReferenceSpace Space { get; }

        /// <summary>Gets the configured source-to-Unity coordinate convention.</summary>
        /// <value>Source axis directions used for Cartesian input and Source rotations.</value>
        public CoordinateSystem SourceCoordinates { get; }

        /// <summary>Gets the body-axis mapping for an EarthCentered orientation.</summary>
        /// <value>The accepted right-handed body mapping, or null for other rotation representations.</value>
        public CoordinateSystem? EarthCenteredBodyAxes { get; }

        internal ExtrapolationContext(Spatial spatial, CoordinateSystem sourceCoordinates, Timestamp? observationTime,
            Double3? velocity, Double3? acceleration, Double3? angularVelocity, bool velocityIsEstimated,
            bool angularVelocityIsEstimated, bool angularVelocityIsWorld, Timestamp? latestVelocityTime,
            Timestamp? latestAccelerationTime, Timestamp? latestAngularVelocityTime)
        {
            // Keep the latest raw values separate from the pose at the time being predicted.
            Position = spatial.HasPosition ? spatial.Position : (Double3?)null;
            Rotation = spatial.HasRotation ? spatial.Rotation : (Quaternion?)null;
            PositionTime = spatial.PositionTime;
            RotationTime = spatial.RotationTime;
            ObservationTime = observationTime;
            ObservationPosition = SamplePosition(spatial, observationTime);
            ObservationRotation = SampleRotation(spatial, observationTime);
            Velocity = velocity;
            Acceleration = acceleration;
            AngularVelocity = angularVelocity;
            VelocityIsEstimated = velocityIsEstimated;
            AngularVelocityIsEstimated = angularVelocityIsEstimated;
            AngularVelocityIsWorld = angularVelocityIsWorld;
            LatestVelocityTime = latestVelocityTime;
            LatestAccelerationTime = latestAccelerationTime;
            LatestAngularVelocityTime = latestAngularVelocityTime;
            RotationSpace = spatial.RotationSpace;
            Ghost ghost = spatial.GetComponent<Ghost>();
            ReferenceFrame frame = ghost != null && ghost.Anchor != null ? ghost.Anchor.Realm.ReferenceFrame : null;
            Space = frame != null ? frame.Space : ReferenceSpace.Cartesian;
            SourceCoordinates = sourceCoordinates;
            EarthCenteredBodyAxes = spatial.EarthCenteredBodyAxes;
        }

        internal static Double3? SamplePosition(Spatial spatial, Timestamp? time)
        {
            // Treat input without a timestamp as current. Otherwise, require a sample at or before the requested time.
            if (!spatial.HasPosition)
            {
                return null;
            }

            if (!time.HasValue || !spatial.PositionTime.HasValue)
            {
                return spatial.Position;
            }

            if (!spatial.History.OldestPositionTime.HasValue || time.Value.CompareTo(spatial.History.OldestPositionTime.Value) < 0)
            {
                return null;
            }

            spatial.History.SamplePosition(time.Value.ElapsedSince(spatial.History.NewestPositionTime.Value), out Double3 position);
            return position;
        }

        private static Quaternion? SampleRotation(Spatial spatial, Timestamp? time)
        {
            // A rotation from a later time must not change how we predict an older position.
            if (!spatial.HasRotation)
            {
                return null;
            }

            if (!time.HasValue || !spatial.RotationTime.HasValue)
            {
                return spatial.Rotation;
            }

            if (!spatial.History.OldestRotationTime.HasValue || time.Value.CompareTo(spatial.History.OldestRotationTime.Value) < 0)
            {
                return null;
            }

            spatial.History.SampleRotation(time.Value.ElapsedSince(spatial.History.NewestRotationTime.Value), out Quaternion rotation);
            return rotation;
        }
    }
}
