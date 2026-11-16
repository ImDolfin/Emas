using UnityEngine;

namespace Emas.AircraftFormation
{
    /// <summary>Shows reference attitude, angular speed, observation ages and wingman bearings during the flight.</summary>
    public sealed class FlightDisplay : MonoBehaviour
    {
        [SerializeField] private RealmSetup _setup;
        [SerializeField] private FlightSource _source;
        [SerializeField] private Camera _camera;
        private Quaternion _previousRotation;
        private double _previousTime;
        private bool _hasPrevious;
        private float _angularSpeed;

        private void LateUpdate()
        {
            ReferenceFrame frame = _setup != null ? _setup.Realm?.ReferenceFrame : null;
            if (frame == null || !frame.IsReferenceAvailable)
            {
                _hasPrevious = false;
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (_hasPrevious && now > _previousTime)
            {
                _angularSpeed = Quaternion.Angle(_previousRotation, frame.Rotation) / (float)(now - _previousTime);
            }
            _previousRotation = frame.Rotation;
            _previousTime = now;
            _hasPrevious = true;
        }

        private void OnGUI()
        {
            if (_setup == null || _source == null || _camera == null)
            {
                return;
            }
            Realm realm = _setup.Realm;
            if (realm != null && realm.ReferenceFrame.IsReferenceAvailable)
            {
                DrawHorizon(realm.ReferenceFrame.Rotation);
            }
            GUI.Box(new Rect(12, 12, 450, 260), GUIContent.none);
            GUILayout.BeginArea(new Rect(24, 20, 426, 245));
            GUILayout.Label("MACH 1  /  AIRCRAFT FORMATION");
            GUILayout.Label("343 m/s (1,234.8 km/h)  |  SDK " + _source.ElapsedSeconds.ToString("F1") + " s");
            if (realm == null || !realm.ReferenceFrame.IsReferenceAvailable)
            {
                GUILayout.Label("Waiting for independently delayed aircraft observations...");
            }
            else
            {
                Vector3 angles = realm.ReferenceFrame.Rotation.eulerAngles;
                GUILayout.Label("Presented camera angular speed: " + _angularSpeed.ToString("F2") + " deg/s");
                GUILayout.Label("Yaw " + Signed(angles.y).ToString("F1") + "   Pitch " + (-Signed(angles.x)).ToString("F1")
                    + "   Bank " + (-Signed(angles.z)).ToString("F1") + " deg");
                ShowAircraft(realm, "reference");
                ShowAircraft(realm, "left");
                ShowAircraft(realm, "right");
            }
            if (GUILayout.Button(_source.SimulateJitterAndDelay ? "Use clean, simultaneous observations" : "Use independent noisy, delayed observations"))
            {
                _source.SimulateJitterAndDelay = !_source.SimulateJitterAndDelay;
            }
            if (realm != null && GUILayout.Button("Shared buffer: " + (realm.InterpolationDelay * 1000).ToString("F0") + " ms  (toggle 0 / 150)"))
            {
                realm.InterpolationDelay = realm.InterpolationDelay > 0 ? 0 : 0.15f;
            }
            GUILayout.EndArea();

            GUI.color = new Color(0.4f, 1, 0.75f);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 12, Screen.height * 0.5f, 24, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f, Screen.height * 0.5f - 12, 1, 24), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void ShowAircraft(Realm realm, string id)
        {
            if (!realm.TryGetGhost(new Key("aircraft-formation", FlightSource.Kind, id), out IGhost entity))
            {
                GUILayout.Label(id + ": waiting for packet");
                return;
            }
            Ghost ghost = (Ghost)entity;
            Spatial spatial = ghost.GetRequired<Spatial>();
            double age = spatial.PositionTime.HasValue
                ? _source.ElapsedSeconds - spatial.PositionTime.Value.ElapsedSince(new Timestamp(0, 0)) : 0;
            string text = id + " capture age " + (age * 1000).ToString("F0") + " ms";
            if (id != "reference" && spatial.IsInRange)
            {
                Vector3 local = _camera.transform.InverseTransformPoint(ghost.transform.position);
                float azimuth = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float elevation = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                text += "   bearing " + azimuth.ToString("F1") + " / " + elevation.ToString("F1") + " deg";
            }
            GUILayout.Label(text);
        }

        private static float Signed(float degrees)
        {
            return Mathf.DeltaAngle(0, degrees);
        }

        private void DrawHorizon(Quaternion rotation)
        {
            // The level horizon satisfies dot(simulation up, camera ray) = 0. Use the presented reference
            // attitude so the instrument and target projection share the same buffered, filtered motion.
            Vector3 up = Quaternion.Inverse(rotation) * Vector3.up;
            float focalLength = Screen.height * 0.5f / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float leftY = Screen.height * 0.5f + (-up.x * Screen.width * 0.5f + up.z * focalLength) / up.y;
            float rightY = Screen.height * 0.5f + (up.x * Screen.width * 0.5f + up.z * focalLength) / up.y;
            Vector2 start = new Vector2(0, leftY);
            Vector2 end = new Vector2(Screen.width, rightY);
            Matrix4x4 previous = GUI.matrix;
            GUI.color = new Color(0.4f, 1, 0.75f, 0.6f);
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y, Vector2.Distance(start, end), 2), Texture2D.whiteTexture);
            GUI.matrix = previous;
            GUI.color = Color.white;
        }
    }
}
