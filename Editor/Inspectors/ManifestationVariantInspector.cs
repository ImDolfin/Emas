using UnityEditor;

namespace Emas.Editor
{
    /// <summary>
    /// Shows a manifestation variant's detail mappings and configuration errors.
    /// </summary>
    [CustomEditor(typeof(ManifestationVariant))]
    [CanEditMultipleObjects]
    public sealed class ManifestationVariantInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Draws variant settings and actionable validation errors.
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (UnityEngine.Object value in targets)
            {
                string error = ((ManifestationVariant)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }
    }
}