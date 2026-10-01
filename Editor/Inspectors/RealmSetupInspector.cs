using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Organizes realm authoring separately from its read-only runtime state.</summary>
    [CustomEditor(typeof(RealmSetup))]
    [CanEditMultipleObjects]
    public sealed class RealmSetupInspector : UnityEditor.Editor
    {
        /// <summary>Draws grouped configuration, units, dependent controls and runtime diagnostics.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool running = false;
            foreach (Object value in targets)
            {
                running |= ((RealmSetup)value).Realm != null;
            }
            InspectorLayout.Header("Realm", "One entity world. Configure Kind mappings here; attach detectors on its Anchors.");
            using (new EditorGUI.DisabledScope(running))
            {
                using (InspectorLayout.Section("Entities", "One mapping per Kind, shared by every Anchor. Leave empty for entities without views or a custom prefab."))
                {
                    InspectorLayout.Field(serializedObject, "_blueprints", "Kind mappings");
                }
                DrawReference();
            }
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                RealmSetup setup = (RealmSetup)value;
                if (setup.Realm == null)
                {
                    string error = setup.GetConfigurationError();
                    if (error != null)
                    {
                        EditorGUILayout.HelpBox(setup.name + ": " + error, MessageType.Error);
                    }
                }
                else
                {
                    DrawRuntime(setup);
                }
            }
        }

        private void DrawReference()
        {
            SerializedProperty useFrame = serializedObject.FindProperty("_useReferenceFrame");
            SerializedProperty follow = serializedObject.FindProperty("_followGhost");
            SerializedProperty rotation = serializedObject.FindProperty("_followRotation");
            using (InspectorLayout.Section("Reference", "Spatial uses shared Cartesian coordinates. Units come from your SDK mapping; they are not necessarily metres."))
            {
                InspectorLayout.Field(serializedObject, "_useReferenceFrame", "Use reference frame");
                if (!useFrame.hasMultipleDifferentValues && !useFrame.boolValue)
                {
                    return;
                }
                using (new EditorGUI.DisabledScope(!InspectorLayout.IsOn(useFrame)))
                {
                    InspectorLayout.Field(serializedObject, "_followGhost", "Follow a Ghost");
                    if (follow.hasMultipleDifferentValues)
                    {
                        EditorGUILayout.HelpBox("Reference modes differ. Select one mode to edit its source settings together.", MessageType.Info);
                    }
                    else if (follow.boolValue)
                    {
                        InspectorLayout.Field(serializedObject, "_referenceAnchorId", "Anchor ID");
                        SerializedProperty kind = serializedObject.FindProperty("_referenceKind._id");
                        EditorGUILayout.PropertyField(kind, new GUIContent("Kind ID", "The Kind passed to Detect, not a prefab name."));
                        InspectorLayout.Field(serializedObject, "_referenceEntityId", "Entity ID");
                    }
                    else
                    {
                        InspectorLayout.Position(serializedObject.FindProperty("_position"), "Reference position (shared units)");
                    }
                    InspectorLayout.Field(serializedObject, "_followRotation", "Follow orientation");
                    using (new EditorGUI.DisabledScope(!InspectorLayout.IsOn(rotation) || follow.hasMultipleDifferentValues))
                    {
                        InspectorLayout.Rotation(serializedObject.FindProperty("_rotation"), follow.boolValue ? "Initial reference rotation" : "Reference rotation");
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!InspectorLayout.IsOn(useFrame)))
            {
                using (InspectorLayout.Section("Placement", "Where the reference appears in the scene. Keep this near the origin for float precision."))
                {
                    InspectorLayout.Field(serializedObject, "_unityPosition", "Position (Unity units)");
                    InspectorLayout.Rotation(serializedObject.FindProperty("_unityRotation"), "Scene alignment");
                }
                using (InspectorLayout.Section("Visibility", "Suppresses distant views, renderers and colliders; entities stay tracked and their logic keeps running."))
                {
                    SerializedProperty limit = serializedObject.FindProperty("_limitDistance");
                    InspectorLayout.Field(serializedObject, "_limitDistance", "Limit distance");
                    using (new EditorGUI.DisabledScope(!InspectorLayout.IsOn(limit)))
                    {
                        InspectorLayout.Field(serializedObject, "_maxDistance", "Radius (shared units)");
                    }
                }
            }
        }

        private static void DrawRuntime(RealmSetup setup)
        {
            Realm realm = setup.Realm;
            using (InspectorLayout.Section("Live realm ? " + setup.name, "Read-only. Configuration above is captured when this Realm starts."))
            {
                InspectorLayout.ReadOnly("Anchors", realm.Anchors.Count.ToString());
                InspectorLayout.ReadOnly("Available entities", realm.Query().Count.ToString());
                ReferenceFrame frame = realm.ReferenceFrame;
                InspectorLayout.ReadOnly("Reference", frame == null ? "Identity / world coordinates" : frame.IsReferenceAvailable ? "Available" : frame.HasPosition ? "Last known pose" : "Waiting for position");
                if (frame != null)
                {
                    InspectorLayout.ReadOnly("Shared position", frame.HasPosition ? frame.Position.ToString() : "Not received");
                    InspectorLayout.ReadOnly("Range (shared units)", frame.MaxDistance.HasValue ? frame.MaxDistance.Value.ToString("G") : "Unlimited");
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
