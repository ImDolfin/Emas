using UnityEditor;

namespace Emas.Editor
{
    /// <summary>
    /// Validates scene tracking settings without starting a realm or source.
    /// </summary>
    [CustomEditor(typeof(SceneSetup))]
    [CanEditMultipleObjects]
    public sealed class SceneSetupInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Draws settings and shared runtime configuration errors.
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (UnityEngine.Object value in targets)
            {
                string error = ((SceneSetup)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }
    }
}
