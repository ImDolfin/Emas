using UnityEditor;

namespace Emas.Editor
{
    /// <summary>
    /// Shows the prefab realm's reference-frame settings and anchor validation.
    /// </summary>
    [CustomEditor(typeof(RealmSetup))]
    [CanEditMultipleObjects]
    public sealed class RealmSetupInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Draws relevant reference-frame fields and configuration errors.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty blueprints = serializedObject.FindProperty("_blueprints");
            EditorGUILayout.PropertyField(blueprints,
                new UnityEngine.GUIContent("Manifestation Blueprints", blueprints.tooltip), true);
            SerializedProperty useFrame = serializedObject.FindProperty("_useReferenceFrame");
            EditorGUILayout.PropertyField(useFrame);
            if (useFrame.boolValue)
            {
                SerializedProperty followGhost = serializedObject.FindProperty("_followGhost");
                EditorGUILayout.PropertyField(followGhost);
                if (followGhost.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_referenceAnchorId"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_referenceKind"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_referenceEntityId"));
                }
                else
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_position"));
                }

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_rotation"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_unityPosition"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_unityRotation"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_followRotation"));
                SerializedProperty limitDistance = serializedObject.FindProperty("_limitDistance");
                EditorGUILayout.PropertyField(limitDistance);
                if (limitDistance.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_maxDistance"));
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Assign realm manifestation blueprints here. An empty blueprint keeps a kind silent. Add Anchor Setup and one IDetectorProvider component to each anchor object; anchors may override manifestation blueprints.", MessageType.Info);
            foreach (UnityEngine.Object value in targets)
            {
                string error = ((RealmSetup)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }
    }
}
