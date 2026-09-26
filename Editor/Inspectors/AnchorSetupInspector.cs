using UnityEditor;

namespace Emas.Editor
{
    /// <summary>
    /// Validates one prefab anchor and its source provider without starting a realm.
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
            DrawDefaultInspector();
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
