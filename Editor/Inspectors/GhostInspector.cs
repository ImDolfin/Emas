using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Separates authored Ghost behavior from its modules and runtime-owned identity.</summary>
    [CustomEditor(typeof(Ghost), true)]
    [CanEditMultipleObjects]
    public sealed class GhostInspector : UnityEditor.Editor
    {
        /// <summary>Draws component composition, application settings and read-only live state.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InspectorLayout.Header("Ghost", "An entity root. Add reusable modules here; a View is optional.");
            using (InspectorLayout.Section("Application behavior", "Custom subclasses can override OnUpdate(): modules ? Ghost behavior ? spatial placement."))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
                }
                SerializedProperty property = serializedObject.GetIterator();
                bool children = true;
                bool hasFields = false;
                while (property.NextVisible(children))
                {
                    children = false;
                    if (property.name == "m_Script")
                    {
                        continue;
                    }
                    EditorGUILayout.PropertyField(property, true);
                    hasFields = true;
                }
                if (!hasFields)
                {
                    EditorGUILayout.LabelField("No custom settings. Configure the attached modules instead.", EditorStyles.wordWrappedMiniLabel);
                }
            }
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                Ghost ghost = (Ghost)value;
                using (InspectorLayout.Section(targets.Length > 1 ? "Modules ? " + ghost.name : "Modules",
                    "An initializer binds SDK values to these components. Modules stay independent of the SDK."))
                {
                    EntityModule[] modules = ghost.GetComponents<EntityModule>();
                    foreach (EntityModule module in modules)
                    {
                        InspectorLayout.Object(module.enabled ? "Enabled module" : "Disabled module", module);
                    }
                    if (modules.Length == 0)
                    {
                        EditorGUILayout.LabelField("No modules attached. Metadata-only entities are supported.", EditorStyles.wordWrappedMiniLabel);
                    }
                    Spatial spatial = ghost.GetComponent<Spatial>();
                    if (spatial != null)
                    {
                        InspectorLayout.Object("Spatial placement", spatial);
                    }
                }
                using (InspectorLayout.Section("Identity & state ? " + ghost.name, "Read-only. The detector supplies Kind, entity ID, display name and Variant through Detect()."))
                {
                    Key key = ghost.Key;
                    bool initialized = key.Kind.IsValid;
                    InspectorLayout.ReadOnly("Status", !initialized ? "Not tracked / prefab" : ghost.IsAvailable ? "Available" : "Pending / unavailable");
                    InspectorLayout.ReadOnly("Anchor ID", initialized ? key.AnchorId : "Assigned at detection");
                    InspectorLayout.ReadOnly("Kind ID", initialized ? key.Kind.Id : "Assigned at detection");
                    InspectorLayout.ReadOnly("Entity ID", initialized ? key.EntityId : "Assigned at detection");
                    InspectorLayout.ReadOnly("Display name", ghost.Name);
                    InspectorLayout.ReadOnly("Variant", ghost.Variant == Variant.None ? "None (default)" : ghost.Variant.ToString());
                    InspectorLayout.ReadOnly("Ghost hook", ghost.enabled ? "Enabled" : "Disabled (modules update independently)");
                    Spatial spatial = ghost.GetComponent<Spatial>();
                    if (Application.isPlaying && initialized && spatial != null)
                    {
                        InspectorLayout.ReadOnly("Shared position", spatial.HasPosition ? spatial.Position.ToString() : "Not received");
                        InspectorLayout.ReadOnly("Spatial presentation", !spatial.enabled ? "Application controlled" : spatial.IsInRange ? "In range" : "Suppressed / awaiting position");
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
