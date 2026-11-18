using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Combines quaternion body-frame second-order dead reckoning, coordinated turns and cubic packet correction.</summary>
    /// <remarks>
    /// Assumes velocity turns with the body without changing sideslip. Acceleration must be gravity-free inertial
    /// acceleration; Gravity must use the same distance unit. Supplied rates, including zero, override bank-derived
    /// rates. Missing attitude falls back to world second-order motion. Instances may be shared when immutable; omit
    /// Smoothing to evaluate cubic correction alone.
    /// </remarks>
    [Serializable]
    public sealed class CoordinatedTurnExtrapolation : IExtrapolationStrategy, IPacketSmoothingStrategy
    {
        [Tooltip("Cubic position and quaternion correction duration on accepted packet arrivals, in unscaled seconds. Zero: immediate correction. Independent of the extrapolation horizon.")]
        [Min(0)]
        [SerializeField] private float _packetSmoothingDuration = 0.1f;
        [Tooltip("Use steady coordinated level-turn rate g*tan(bank)/horizontal speed only when neither supplied nor estimated angular velocity is available. Supplied zero rates override this fallback.")]
        [SerializeField] private bool _useBankAngleWhenRatesUnavailable = true;
        [Tooltip("Gravity magnitude in Cartesian source distance units per second squared; use 9.80665 for metres and geographic input. Used only for the bank-derived turn-rate fallback.")]
        [Min(float.Epsilon)]
        [SerializeField] private float _gravity = 9.80665f;

        /// <summary>Gets or sets cubic packet correction duration in unscaled seconds.</summary>
        /// <value>A finite, nonnegative duration, defaulting to 0.1 seconds. Zero bypasses packet correction.</value>
        /// <remarks>The correction can outlast the extrapolation horizon but converges to the capped pose.
        /// Assign a new strategy instance for a continuous transition when changing other model settings.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The duration is negative or nonfinite.</exception>
        public float PacketSmoothingDuration
        {
            get => _packetSmoothingDuration;
            set
            {
                Smoothing.ValidateTime(value, nameof(value));
                _packetSmoothingDuration = value;
            }
        }

        /// <summary>Gets or sets whether missing angular rates may be inferred from bank and horizontal speed.</summary>
        /// <value>True by default. Does not override a supplied or estimated angular rate, including zero.</value>
        /// <remarks>Assumes a steady coordinated turn with negligible sideslip and no wind. Holds angular motion
        /// at zero when speed is negligible, heading is vertical, or bank reaches 85 degrees or more.</remarks>
        public bool UseBankAngleWhenRatesUnavailable
        {
            get => _useBankAngleWhenRatesUnavailable;
            set => _useBankAngleWhenRatesUnavailable = value;
        }

        /// <summary>Gets or sets gravity used to infer turn rate from bank.</summary>
        /// <value>A finite, positive acceleration in source distance units per second squared; defaults to 9.80665.</value>
        /// <exception cref="ArgumentOutOfRangeException">Gravity is nonpositive or nonfinite.</exception>
        public float Gravity
        {
            get => _gravity;
            set
            {
                if (!SpatialMath.IsFinite(value) || value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Gravity must be finite and positive.");
                }

                _gravity = value;
            }
        }

        /// <summary>Integrates rotating body velocity and its constant first derivative to obtain a second-order displacement.</summary>
        /// <param name="context">Time-aligned pose and inertial motion. Missing velocity holds position.</param>
        /// <param name="seconds">Finite, nonnegative seconds already bounded by the extrapolation horizon.</param>
        /// <returns>Displacement in Cartesian source axes or ECEF metres, without modifying the observed pose.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or the resulting displacement is invalid.</exception>
        public Double3 ExtrapolateDisplacement(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (seconds == 0 || !context.Velocity.HasValue)
            {
                return default(Double3);
            }

            if (!TryAttitude(context, out Quaternion attitude))
            {
                return context.Velocity.Value * seconds
                    + context.Acceleration.GetValueOrDefault() * (0.5d * seconds * seconds);
            }

            // Convert world motion to local Unity axes, then calculate motion in the turning body axes.
            Double3 velocity = ToLocal(context, context.Velocity.Value);
            Double3 bodyVelocity = SpatialMath.Rotate(Quaternion.Inverse(attitude), velocity);
            Double3 rate = BodyRate(context, attitude, velocity);
            Double3 bodyAcceleration = default(Double3);
            if (context.Acceleration.HasValue)
            {
                Double3 acceleration = ToLocal(context, context.Acceleration.Value);
                // d(v_body)/dt = R^-1*a_world - omega_body x v_body. Feeding inertial
                // acceleration directly into this calculation would count the acceleration caused by turning twice.
                bodyAcceleration = SpatialMath.Rotate(Quaternion.Inverse(attitude), acceleration) - Cross(rate, bodyVelocity);
            }

            // Convert the movement back to source axes before mapping the position into Unity.
            Double3 displacement = SpatialMath.Rotate(attitude, Integrate(bodyVelocity, bodyAcceleration, rate, seconds));
            return context.Space == ReferenceSpace.Geographic
                ? Basis(context).ToEarthCentered(displacement) : context.SourceCoordinates.ToSource(displacement);
        }

        /// <summary>Integrates a constant angular vector with a quaternion exponential, retaining body or world composition.</summary>
        /// <param name="context">Time-aligned rates and attitude. Missing rates may use the coordinated-turn fallback.</param>
        /// <param name="seconds">Finite, nonnegative seconds already bounded by the extrapolation horizon.</param>
        /// <returns>The quaternion offset expected by Extrapolation's body/world composition pipeline.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Duration or angular displacement is invalid.</exception>
        public Quaternion ExtrapolateRotation(ExtrapolationContext context, double seconds)
        {
            ConstantVelocityExtrapolation.ValidateSeconds(seconds);
            if (context.AngularVelocity.HasValue)
            {
                return SpatialMath.AngularDisplacement(context.AngularVelocity.Value, seconds);
            }

            if (!TryAttitude(context, out Quaternion attitude) || !context.Velocity.HasValue)
            {
                return Quaternion.identity;
            }

            return SpatialMath.AngularDisplacement(BodyRate(context, attitude, ToLocal(context, context.Velocity.Value)), seconds);
        }

        private Double3 BodyRate(ExtrapolationContext context, Quaternion attitude, Double3 velocity)
        {
            // Use supplied or estimated turn rates, including zero, before trying to infer a turn from bank.
            if (context.AngularVelocity.HasValue)
            {
                Double3 rate = context.AngularVelocity.Value;
                if (context.AngularVelocityIsWorld)
                {
                    if (context.RotationSpace == RotationSpace.EarthCentered)
                    {
                        rate = Basis(context).ToLocal(rate) * -1d;
                    }

                    rate = SpatialMath.Rotate(Quaternion.Inverse(attitude), rate);
                }

                return rate;
            }

            if (!_useBankAngleWhenRatesUnavailable || !SpatialMath.IsFinite(_gravity) || _gravity <= 0)
            {
                return default(Double3);
            }

            // Estimate only a steady, level turn. Stop if bank or horizontal speed cannot be calculated.
            double speed = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            Double3 forward = SpatialMath.Rotate(attitude, new Double3(0, 0, 1));
            double headingLength = Math.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
            if (speed < 0.001d || headingLength < 0.001d)
            {
                return default(Double3);
            }

            Double3 right = SpatialMath.Rotate(attitude, new Double3(1, 0, 0));
            double levelRight = (right.X * forward.Z - right.Z * forward.X) / headingLength;
            double bank = Math.Atan2(-right.Y, levelRight);
            if (Math.Abs(bank) >= 85d * Math.PI / 180d)
            {
                return default(Double3);
            }

            double turnRate = _gravity * Math.Tan(bank) / speed;
            return SpatialMath.Rotate(Quaternion.Inverse(attitude), new Double3(0, turnRate, 0));
        }

        private static bool TryAttitude(ExtrapolationContext context, out Quaternion attitude)
        {
            // Calculate turns from the local body rotation, even when the SDK supplies an ECEF rotation.
            attitude = Quaternion.identity;
            if (!context.ObservationRotation.HasValue || (context.Space == ReferenceSpace.Geographic && !context.ObservationPosition.HasValue))
            {
                return false;
            }

            Quaternion rotation = context.ObservationRotation.Value;
            if (context.RotationSpace == RotationSpace.EarthCentered)
            {
                if (!context.ObservationPosition.HasValue)
                {
                    return false;
                }

                attitude = Basis(context).ToLocalEarthCenteredRotation(rotation, context.EarthCenteredBodyAxes.Value);
            }
            else
            {
                attitude = context.RotationSpace == RotationSpace.Source ? context.SourceCoordinates.ToUnityRotation(rotation) : rotation;
            }

            return true;
        }

        private static Double3 ToLocal(ExtrapolationContext context, Double3 vector)
        {
            return context.Space == ReferenceSpace.Geographic ? Basis(context).ToLocal(vector) : context.SourceCoordinates.ToUnity(vector);
        }

        private static GeographicBasis Basis(ExtrapolationContext context)
        {
            return new GeographicBasis(GeoPosition.FromEarthCentered(context.ObservationPosition.Value));
        }

        private static Double3 Integrate(Double3 velocity, Double3 acceleration, Double3 rate, double seconds)
        {
            double magnitude = Math.Sqrt(rate.X * rate.X + rate.Y * rate.Y + rate.Z * rate.Z);
            if (magnitude == 0)
            {
                return velocity * seconds + acceleration * (0.5d * seconds * seconds);
            }

            Double3 axis = rate * (1d / magnitude);
            double angle = magnitude * seconds;
            double f1;
            double f2;
            double f3;
            double f4;
            if (Math.Abs(angle) < 0.01d)
            {
                // For nearly straight flight, use a series expansion to avoid rounding errors and division by a tiny rate.
                double squared = angle * angle;
                f1 = angle * (0.5d + squared * (-1d / 24d + squared / 720d));
                f2 = squared * (1d / 6d + squared * (-1d / 120d + squared / 5040d));
                f3 = angle * (1d / 3d + squared * (-1d / 30d + squared / 840d));
                f4 = squared * (1d / 8d + squared * (-1d / 144d + squared / 5760d));
            }
            else
            {
                double sine = Math.Sin(angle);
                double cosine = Math.Cos(angle);
                f1 = (1d - cosine) / angle;
                f2 = 1d - sine / angle;
                f3 = (sine - angle * cosine) / (angle * angle);
                f4 = 0.5d - sine / angle + (1d - cosine) / (angle * angle);
            }

            // Use the exact Rodrigues formula to sum exp(omega*t)*(v_body + a_body*t) over this time interval.
            Double3 vCross = Cross(axis, velocity);
            Double3 aCross = Cross(axis, acceleration);
            return (velocity + vCross * f1 + Cross(axis, vCross) * f2) * seconds
                + (acceleration * 0.5d + aCross * f3 + Cross(axis, aCross) * f4) * (seconds * seconds);
        }

        private static Double3 Cross(Double3 left, Double3 right)
        {
            return new Double3(left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);
        }
    }
}
