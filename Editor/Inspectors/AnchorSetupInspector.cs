using UnityEditor;

namespace Emas.Editor
{
    /// <summary>
    /// Validates one prefab anchor and its detector provider without starting a realm.
    /// </summary>
    [CustomEditor(typeof(AnchorSetup))]
    [CanEditMultipleObjects]
    public sealed class AnchorSetupInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Draws anchor settings and configuration errors.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_anchorId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_blueprints"),
                new UnityEngine.GUIContent("Manifestation Blueprints"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_automaticViews"));
            serializedObject.ApplyModifiedProperties();
            foreach (UnityEngine.Object value in targets)
            {
                string error = ((AnchorSetup)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }
    }
}
