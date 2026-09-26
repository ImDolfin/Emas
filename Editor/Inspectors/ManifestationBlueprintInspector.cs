using UnityEditor;

namespace Emas.Editor
{
    /// <summary>
    /// Shows manifestation blueprint settings and the same configuration errors used by runtime registration.
    /// </summary>
    [CustomEditor(typeof(ManifestationBlueprint))]
    [CanEditMultipleObjects]
    public sealed class ManifestationBlueprintInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Draws editable settings followed by actionable validation errors.
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (UnityEngine.Object value in targets)
            {
                string error = ((ManifestationBlueprint)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }
    }
}
