using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Compares accepted observations with presented Ghost poses in the Scene view.</summary>
    /// <remarks>Activate from Window/Emas or the Scene tools menu. Reads existing state on the main thread;
    /// selection, pinning and rendering never update a Realm or modify a Ghost.</remarks>
    [EditorTool("Emas pose comparison")]
    public sealed class PoseTool : EditorTool
    {
        private static PoseTool _instance;
        [SerializeField] private Ghost _pinnedGhost;
        [SerializeField] private bool _drawObservedPose = true;
        [SerializeField] private bool _drawPresentedPose = true;
        [SerializeField] private bool _drawLabels = true;
        [SerializeField] private bool _drawConnection = true;
        [SerializeField] private float _gizmoSize = 1f;
        private readonly List<Ghost> _ghosts = new List<Ghost>();
        private readonly HashSet<Ghost> _seen = new HashSet<Ghost>();

        /// <summary>Gets the icon and tooltip shown in Unity's Scene tools menu.</summary>
        /// <value>The built-in transform icon labelled as the Emas pose comparison tool.</value>
        public override GUIContent toolbarIcon => new GUIContent(EditorGUIUtility.IconContent("TransformTool").image, "Emas pose comparison");

        internal static PoseTool Instance
        {
            get
            {
                if (_instance == null)
                {
                    PoseTool[] tools = Resources.FindObjectsOfTypeAll<PoseTool>();
                    _instance = tools.Length > 0 ? tools[0] : CreateInstance<PoseTool>();
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                }

                return _instance;
            }
        }

        internal bool IsActive => ToolManager.activeToolType == typeof(PoseTool);

        /// <summary>Retains the active tool instance as the shared debugger state for windows and overlays.</summary>
        public override void OnActivated()
        {
            if (_instance != null && _instance != this)
            {
                _pinnedGhost = _instance._pinnedGhost;
                _drawObservedPose = _instance._drawObservedPose;
                _drawPresentedPose = _instance._drawPresentedPose;
                _drawLabels = _instance._drawLabels;
                _drawConnection = _instance._drawConnection;
                _gizmoSize = _instance._gizmoSize;
            }

            _instance = this;
            SceneView.RepaintAll();
        }

        /// <summary>Draws observed and presented poses for the pinned Ghost or selected Ghost roots.</summary>
        /// <param name="window">The Editor window requesting tool GUI; only Scene view repaint events draw markers.</param>
        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            CollectGhosts();
            foreach (Ghost ghost in _ghosts)
            {
                Spatial spatial = ghost.GetComponent<Spatial>();
                if (spatial == null || !spatial.TryGetObservedUnityPose(out Pose observed))
                {
                    continue;
                }

                Pose presented = new Pose(ghost.transform.position, ghost.transform.rotation);
                float size = HandleUtility.GetHandleSize(presented.position) * _gizmoSize;
                if (_drawObservedPose)
                {
                    DrawPose(observed, size, Color.cyan, spatial.HasRotation, _drawLabels ? "Observed: " + ghost.name : null, 1);
                }
                if (_drawPresentedPose)
                {
                    DrawPose(presented, size, Color.magenta, true, _drawLabels ? "Presented: " + ghost.name : null, -1);
                }
                if (_drawConnection && _drawObservedPose && _drawPresentedPose)
                {
                    using (new Handles.DrawingScope(Color.yellow, Matrix4x4.identity))
                    {
                        Handles.DrawDottedLine(observed.position, presented.position, 4f);
                    }
                }
            }
        }

        internal void Open(Ghost ghost)
        {
            _pinnedGhost = ghost;
            ToolManager.SetActiveTool(this);
            DiagnosticsWindow.ShowWindow();
        }

        internal void DrawControls(Realm realm, bool compact)
        {
            EditorGUI.BeginChangeCheck();
            if (GUILayout.Button(IsActive ? "Deactivate pose tool" : "Activate pose tool"))
            {
                if (IsActive)
                {
                    ToolManager.RestorePreviousTool();
                }
                else
                {
                    ToolManager.SetActiveTool(this);
                }
            }

            _pinnedGhost = (Ghost)EditorGUILayout.ObjectField("Pinned Ghost", _pinnedGhost, typeof(Ghost), true);
            CollectGhosts();
            Ghost first = _ghosts.Count > 0 ? _ghosts[0] : null;
            Ghost followed = null;
            Key? followedKey = realm.ReferenceFrame != null ? realm.ReferenceFrame.FollowedGhost : null;
            if (followedKey.HasValue && realm.TryGetGhost(followedKey.Value, out IGhost reference))
            {
                followed = reference as Ghost;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(first == null))
                {
                    if (GUILayout.Button("Pin", EditorStyles.miniButton))
                    {
                        _pinnedGhost = first;
                    }
                }
                if (GUILayout.Button("Selection", EditorStyles.miniButton))
                {
                    _pinnedGhost = null;
                }
                using (new EditorGUI.DisabledScope(followed == null))
                {
                    if (GUILayout.Button("Followed Ghost", EditorStyles.miniButton))
                    {
                        _pinnedGhost = followed;
                    }
                }
            }

            _drawObservedPose = EditorGUILayout.ToggleLeft("Observed pose (cyan)", _drawObservedPose);
            _drawPresentedPose = EditorGUILayout.ToggleLeft("Presented pose (magenta)", _drawPresentedPose);
            using (new EditorGUILayout.HorizontalScope())
            {
                _drawLabels = EditorGUILayout.ToggleLeft("Labels", _drawLabels);
                _drawConnection = EditorGUILayout.ToggleLeft("Connection", _drawConnection);
            }
            _gizmoSize = EditorGUILayout.Slider("Gizmo size", _gizmoSize, 0.1f, 5f);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            CollectGhosts();
            if (_ghosts.Count == 0)
            {
                EditorGUILayout.HelpBox("Select a Ghost or one of its view objects, or choose the followed Ghost. Pin keeps the comparison when selection changes.", MessageType.Info);
            }
            foreach (Ghost ghost in _ghosts)
            {
                DrawGhost(ghost, compact);
            }
        }

        private void CollectGhosts()
        {
            _ghosts.Clear();
            _seen.Clear();
            if (_pinnedGhost != null)
            {
                _ghosts.Add(_pinnedGhost);
                return;
            }

            foreach (GameObject selected in Selection.gameObjects)
            {
                Ghost ghost = selected != null ? selected.GetComponentInParent<Ghost>() : null;
                if (ghost != null && _seen.Add(ghost))
                {
                    _ghosts.Add(ghost);
                }
            }
        }

        private static void DrawGhost(Ghost ghost, bool compact)
        {
            using (InspectorLayout.Section(ghost.name))
            {
                Spatial spatial = ghost.GetComponent<Spatial>();
                if (spatial == null || !spatial.TryGetObservedUnityPose(out Pose observed))
                {
                    EditorGUILayout.LabelField("No projectable observation.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                InspectorLayout.ReadOnly("Position difference", Vector3.Distance(observed.position, ghost.transform.position).ToString("G6") + " units");
                InspectorLayout.ReadOnly("Rotation difference", spatial.HasRotation
                    ? Quaternion.Angle(observed.rotation, ghost.transform.rotation).ToString("G6") + " deg" : "No observed attitude");
                InspectorLayout.ReadOnly("Position time", SampleTime(spatial.PositionTime));
                InspectorLayout.ReadOnly("Rotation time", SampleTime(spatial.RotationTime));
                Extrapolation extrapolation = ghost.GetComponent<Extrapolation>();
                InspectorLayout.ReadOnly("Prediction", extrapolation == null ? "No Extrapolation trait" : !extrapolation.enabled ? "Disabled"
                    : ObjectNames.NicifyVariableName(extrapolation.Strategy.GetType().Name).Replace(" Extrapolation", string.Empty));
                if (!compact)
                {
                    InspectorLayout.ReadOnly("Prediction horizon", extrapolation != null ? extrapolation.MaximumExtrapolation.ToString("G4") + " s (maximum)" : "None");
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Vector3Field("Observed position", observed.position);
                        EditorGUILayout.Vector3Field("Presented position", ghost.transform.position);
                        if (spatial.HasRotation)
                        {
                            EditorGUILayout.Vector3Field("Observed rotation (deg)", observed.rotation.eulerAngles);
                            EditorGUILayout.Vector3Field("Presented rotation (deg)", ghost.transform.eulerAngles);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Ghost", EditorStyles.miniButton))
                    {
                        Selection.activeGameObject = ghost.gameObject;
                    }
                    if (GUILayout.Button("Frame comparison", EditorStyles.miniButton))
                    {
                        SceneView scene = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView : EditorWindow.GetWindow<SceneView>();
                        Bounds bounds = new Bounds(ghost.transform.position, Vector3.one * 2f);
                        bounds.Encapsulate(observed.position);
                        scene.Frame(bounds, false);
                    }
                }
            }
        }

        private static string SampleTime(Timestamp? time)
        {
            return time.HasValue ? time.Value.Seconds + "." + time.Value.Nanoseconds.ToString("D9") : "Untimed / not received";
        }

        private static void DrawPose(Pose pose, float size, Color color, bool hasRotation, string label, float labelSide)
        {
            using (new Handles.DrawingScope(color, Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one)))
            {
                Handles.DrawWireCube(Vector3.zero, Vector3.one * (size * 0.15f));
            }
            if (hasRotation)
            {
                DrawAxis(pose.position, pose.rotation * Quaternion.LookRotation(Vector3.right), size, Color.red);
                DrawAxis(pose.position, pose.rotation * Quaternion.LookRotation(Vector3.up, Vector3.forward), size, Color.green);
                DrawAxis(pose.position, pose.rotation, size, Color.blue);
            }
            if (label != null)
            {
                SceneView scene = SceneView.currentDrawingSceneView;
                Vector3 up = scene != null && scene.camera != null ? scene.camera.transform.up : Vector3.up;
                GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel);
                style.normal.textColor = color;
                using (new Handles.DrawingScope(color, Matrix4x4.identity))
                {
                    Handles.Label(pose.position + up * (size * 0.2f * labelSide), label, style);
                }
            }
        }

        private static void DrawAxis(Vector3 position, Quaternion rotation, float size, Color color)
        {
            using (new Handles.DrawingScope(color, Matrix4x4.identity))
            {
                Handles.ArrowHandleCap(0, position, rotation, size, EventType.Repaint);
            }
        }
    }
}
