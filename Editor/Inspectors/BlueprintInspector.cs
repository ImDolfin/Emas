using UnityEditor;

namespace Emas.Editor
{
    /// <summary>Shows blueprint settings and the same configuration errors used by runtime registration.</summary>
    [CustomEditor(typeof(Blueprint))]
    [CanEditMultipleObjects]
    public sealed class BlueprintInspector : UnityEditor.Editor
    {
        /// <summary>Draws editable settings followed by actionable validation errors.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (var value in targets)
            {
                var error = ((Blueprint)value).GetConfigurationError();
                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
        }
    }
}
