using UnityEngine;

namespace Emas
{
    /// <summary>Provides timing and coordinate conventions for a pre-prediction observation filter.</summary>
    public readonly struct FilterContext
    {
        private readonly Spatial _spatial;

        /// <summary>Gets the local processing time.</summary>
        /// <value>Unscaled Unity realtime seconds. This is separate from the SDK sample clock.</value>
        public double Time { get; }

        /// <summary>Gets elapsed processing time for this filter.</summary>
        /// <value>Nonnegative unscaled seconds; zero on the first evaluation after reset.</value>
        public double DeltaTime { get; }

        /// <summary>Gets the Cartesian source coordinate convention.</summary>
        /// <value>The Realm's source-to-Unity axis mapping.</value>
        public CoordinateSystem SourceCoordinates { get; }

        /// <summary>Gets the orientation representation.</summary>
        /// <value>Source axes, geographic east/up/north, or body-to-ECEF.</value>
        public RotationSpace RotationSpace => _spatial.RotationSpace;

        /// <summary>Gets the position reference space.</summary>
        /// <value>Cartesian source coordinates or geographic ECEF metres.</value>
        public ReferenceSpace Space { get; }

        /// <summary>Gets the body-axis convention for ECEF attitudes.</summary>
        /// <value>The accepted right-handed mapping, or null for other representations.</value>
        public CoordinateSystem? EarthCenteredBodyAxes => _spatial.EarthCenteredBodyAxes;

        internal FilterContext(Spatial spatial, CoordinateSystem sourceCoordinates, double time, double deltaTime)
        {
            _spatial = spatial;
            SourceCoordinates = sourceCoordinates;
            Time = time;
            DeltaTime = deltaTime;
            Ghost ghost = spatial.Ghost;
            Space = ghost != null && ghost.Anchor != null && ghost.Anchor.Realm.ReferenceFrame != null
                ? ghost.Anchor.Realm.ReferenceFrame.Space : ReferenceSpace.Cartesian;
        }

        /// <summary>Converts stored SDK angular motion to the world axes of an orientation quaternion.</summary>
        /// <param name="state">The pose and angular motion at the observation being filtered.</param>
        /// <returns>Radians per second with Quaternion.AngleAxis signs; zero if orientation or motion is unavailable.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">Conversion overflows.</exception>
        public Double3 WorldAngularVelocity(FilterState state)
        {
            if (!state.AngularVelocity.HasValue || !state.Rotation.HasValue)
            {
                return default(Double3);
            }

            Double3 rate = state.AngularVelocity.Value;
            if (state.AngularVelocityIsWorld)
            {
                Double3 worldRate = RotationSpace == RotationSpace.EarthCentered ? rate
                    : state.Position.HasValue ? _spatial.BasisAt(state.Position.Value).ToLocal(rate) * -1d : default(Double3);
                return RotationSpace == RotationSpace.Source ? SourceCoordinates.ToSourceAngularVelocity(worldRate) : worldRate;
            }

            if (RotationSpace == RotationSpace.Source)
            {
                rate = SourceCoordinates.ToSourceAngularVelocity(rate);
            }
            else if (RotationSpace == RotationSpace.EarthCentered)
            {
                rate = EarthCenteredBodyAxes.Value.ToSourceAngularVelocity(rate);
            }

            return SpatialMath.Rotate(state.Rotation.Value, rate);
        }

        internal Double3 BodyAngularVelocity(Quaternion rotation, Double3 worldRate)
        {
            Double3 rate = SpatialMath.Rotate(Quaternion.Inverse(rotation), worldRate);
            if (RotationSpace == RotationSpace.Source)
            {
                return SourceCoordinates.ToUnityAngularVelocity(rate);
            }

            return RotationSpace == RotationSpace.EarthCentered ? EarthCenteredBodyAxes.Value.ToUnityAngularVelocity(rate) : rate;
        }
    }
}
