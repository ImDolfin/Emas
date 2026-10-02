using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Shows Ghost identity, spatial presentation and editable root modules in open sections.</summary>
    [CustomEditor(typeof(Ghost), true)]
    [CanEditMultipleObjects]
    public sealed class GhostInspector : UnityEditor.Editor
    {
        /// <summary>Draws authored settings, runtime data and root-module settings without section foldouts.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                Ghost ghost = (Ghost)value;
                Key key = ghost.Key;
                bool tracked = key.Kind.IsValid;
                if (targets.Length > 1)
                {
                    InspectorLayout.Header(ghost.name, null);
                }
                using (InspectorLayout.Section("Identity", "Read-only metadata supplied by the detector."))
                {
                    InspectorLayout.ReadOnly("Status", !tracked ? "Not tracked / prefab" : ghost.IsAvailable ? "Available" : "Pending / unavailable");
                    if (tracked)
                    {
                        InspectorLayout.ReadOnly("Anchor ID", key.AnchorId);
                        InspectorLayout.ReadOnly("Kind ID", key.Kind.Id);
                        InspectorLayout.ReadOnly("Entity ID", key.EntityId);
                        InspectorLayout.ReadOnly("Display name", ghost.Name);
                        InspectorLayout.ReadOnly("Variant", ghost.Variant == Variant.None ? "None (default)" : ghost.Variant.ToString());
                    }
                }

                Spatial spatial;
                if (ghost.TryGet<Spatial>(out spatial))
                {
                    DrawSpatial(ghost, spatial);
                }

                IReadOnlyList<EntityModule> modules = ghost.Modules;
                using (InspectorLayout.Section("Modules (" + modules.Count + ")",
                    "Modules authored on this root. Disabled modules remain listed but skip bound reader updates."))
                {
                    if (modules.Count == 0)
                    {
                        EditorGUILayout.LabelField("No modules on this root.", EditorStyles.miniLabel);
                    }
                    foreach (EntityModule module in modules)
                    {
                        if (module != null)
                        {
                            DrawModule(module);
                        }
                    }
                }
            }
        }

        private static void DrawSpatial(Ghost ghost, Spatial spatial)
        {
            using (InspectorLayout.Section("Spatial pose", "Cached absolute input and the current projected Unity world pose."))
            {
                InspectorLayout.ReadOnly("Presentation", !spatial.enabled ? "Application controlled"
                    : !ghost.IsAvailable ? "Not projected / unavailable"
                    : !spatial.IsInRange ? "Suppressed / awaiting pose or parent"
                    : spatial.AttachedTo.HasValue ? "Attached" : "In range");
                InspectorLayout.ReadOnly("Attached to", spatial.AttachedTo.HasValue ? spatial.AttachedTo.Value.ToString() : "None");
                InspectorLayout.ReadOnly("Absolute position", spatial.HasPosition ? spatial.Position.ToString() : "Not received");
                InspectorLayout.ReadOnly("Absolute rotation", spatial.HasRotation ? spatial.Rotation.ToString("F4") : "Not received");
                if (spatial.HasRotation)
                {
                    InspectorLayout.ReadOnly("Rotation space", spatial.RotationSpace.ToString());
                }
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Unity position", ghost.transform.position);
                    EditorGUILayout.Vector3Field("Unity rotation (deg)", ghost.transform.eulerAngles);
                }
            }
        }

        private static void DrawModule(EntityModule module)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (SerializedObject settings = new SerializedObject(module))
            {
                settings.Update();
                using (new EditorGUILayout.HorizontalScope())
                {
                    SerializedProperty enabled = settings.FindProperty("m_Enabled");
                    EditorGUILayout.PropertyField(enabled, GUIContent.none, GUILayout.Width(18f));
                    EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(module.GetType().Name), EditorStyles.boldLabel);
                    GUILayout.Label(enabled.boolValue ? "Enabled" : "Disabled", EditorStyles.miniLabel);
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(48f)))
                    {
                        Selection.activeObject = module;
                        EditorGUIUtility.PingObject(module);
                    }
                }
                DrawPropertiesExcluding(settings, "m_Script", "m_Enabled");
                settings.ApplyModifiedProperties();
            }
        }

        /// <inheritdoc />
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
