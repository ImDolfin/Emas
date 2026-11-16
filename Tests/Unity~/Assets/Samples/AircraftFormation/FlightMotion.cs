using System;
using UnityEngine;

namespace Emas.AircraftFormation
{
    // Bounded, multi-frequency maneuvers keep both translated wingmen inside the forward hemisphere.
    // Offsets stay in simulation axes: rotating a rigid formation offset would give unequal airspeeds in turns.
    internal static class FlightMotion
    {
        internal static Quaternion Rotation(double time)
        {
            float yaw = (float)(22 * Math.Sin(0.31 * time) + 6 * Math.Sin(0.79 * time));
            float pitch = (float)(12 * Math.Sin(0.43 * time) + 3 * Math.Sin(1.1 * time));
            float bank = (float)(30 * Math.Sin(0.57 * time) + 8 * Math.Sin(1.3 * time));
            return Quaternion.Euler(-pitch, yaw, -bank);
        }

        internal static Double3 Velocity(double time)
        {
            Vector3 forward = Rotation(time) * Vector3.forward;
            // Normalize in doubles so float quaternion roundoff does not change commanded airspeed.
            double length = Math.Sqrt((double)forward.x * forward.x + (double)forward.y * forward.y
                + (double)forward.z * forward.z);
            return new Double3(forward.x / length * 343, forward.y / length * 343, forward.z / length * 343);
        }

        internal static Double3 AngularVelocity(double time)
        {
            const double interval = 0.002;
            Quaternion start = Rotation(time - interval * 0.5);
            Quaternion end = Rotation(time + interval * 0.5);
            // Local quaternion composition yields true body rates, including coupled pitch/yaw/roll motion.
            Quaternion delta = Quaternion.Inverse(start) * end;
            double length = Math.Sqrt((double)delta.x * delta.x + (double)delta.y * delta.y + (double)delta.z * delta.z);
            if (length < 1e-10)
            {
                return default(Double3);
            }
            // atan2 keeps the small vector part accurate when float w has rounded to one.
            double angle = 2 * Math.Atan2(length, delta.w);
            if (angle > Math.PI)
            {
                angle -= 2 * Math.PI;
            }
            double scale = angle / (length * interval);
            return new Double3(delta.x * scale, delta.y * scale, delta.z * scale);
        }

        internal static Double3 Offset(int index)
        {
            return index == 0 ? default(Double3) : new Double3(index == 1 ? -70 : 70, index == 1 ? 20 : -20, 350);
        }
    }
}
