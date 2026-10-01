using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Shows authored Ghost settings and read-only runtime identity.</summary>
    [CustomEditor(typeof(Ghost), true)]
    [CanEditMultipleObjects]
    public sealed class GhostInspector : UnityEditor.Editor
    {
        private bool _showModules;
        private bool _showIdentity;

        /// <summary>Draws custom settings with optional module and identity details.</summary>
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
                InspectorLayout.ReadOnly("Status", !tracked ? "Not tracked / prefab" : ghost.IsAvailable ? "Available" : "Pending / unavailable");
                if (tracked)
                {
                    _showIdentity = EditorGUILayout.Foldout(_showIdentity, "Runtime identity", true);
                    if (_showIdentity)
                    {
                        InspectorLayout.ReadOnly("Anchor ID", key.AnchorId);
                        InspectorLayout.ReadOnly("Kind ID", key.Kind.Id);
                        InspectorLayout.ReadOnly("Entity ID", key.EntityId);
                        InspectorLayout.ReadOnly("Display name", ghost.Name);
                        InspectorLayout.ReadOnly("Variant", ghost.Variant == Variant.None ? "None (default)" : ghost.Variant.ToString());
                        Spatial spatial = ghost.GetComponent<Spatial>();
                        if (spatial != null)
                        {
                            InspectorLayout.ReadOnly("Shared position", spatial.HasPosition ? spatial.Position.ToString() : "Not received");
                            InspectorLayout.ReadOnly("Spatial presentation", !spatial.enabled ? "Application controlled" : spatial.IsInRange ? "In range" : "Suppressed / awaiting position");
                        }
                    }
                }
                EntityModule[] modules = ghost.GetComponents<EntityModule>();
                _showModules = EditorGUILayout.Foldout(_showModules, "Modules (" + modules.Length + ")", true);
                if (_showModules)
                {
                    foreach (EntityModule module in modules)
                    {
                        InspectorLayout.Object(module.enabled ? "Enabled" : "Disabled", module);
                    }
                }
            }
        }

        /// <inheritdoc />
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
