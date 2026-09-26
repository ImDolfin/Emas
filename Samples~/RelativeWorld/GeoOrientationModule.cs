using System;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Converts SDK heading, pitch and roll into a Unity-axis Spatial rotation.
    /// </summary>
    /// <remarks>
    /// The vehicle model faces local +Z with +X to its right and +Y up. SDK yaw is clockwise
    /// from true north, pitch is positive nose up, and roll is positive right wing down.
    /// </remarks>
    public sealed class GeoOrientationModule : EntityModule<GeoPoseReading>
    {
        /// <summary>
        /// Applies one SDK attitude without changing the independent position channel.
        /// </summary>
        /// <param name="reading">The geographic SDK observation.</param>
        public override void Apply(GeoPoseReading reading)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            if (Presence == null || Presence.Root == null)
            {
                throw new InvalidOperationException("The geographic orientation module is not bound to a Ghost root.");
            }

            Spatial spatial = Presence.Root.GetComponent<Spatial>();
            if (spatial == null)
            {
                throw new InvalidOperationException("A geographic orientation module requires Spatial on the Ghost root.");
            }

            Quaternion yaw = Quaternion.AngleAxis(ToUnityDegrees(reading.YawDegrees), Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(-ToUnityDegrees(reading.PitchDegrees), Vector3.right);
            Quaternion roll = Quaternion.AngleAxis(-ToUnityDegrees(reading.RollDegrees), Vector3.forward);
            spatial.SetRotation(yaw * pitch * roll);
        }

        private static float ToUnityDegrees(double degrees)
        {
            return (float)(degrees % 360.0);
        }
    }
}
