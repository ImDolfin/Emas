using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>
    /// Shows application ghost fields and read-only Emas state for live ghosts.
    /// </summary>
    [CustomEditor(typeof(Ghost), true)]
    [CanEditMultipleObjects]
    public sealed class GhostInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Draws application fields and live identity, metadata and availability.
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying)
            {
                return;
            }

            bool hasRuntimeTarget = false;
            foreach (UnityEngine.Object value in targets)
            {
                Ghost ghost = value as Ghost;
                if (ghost == null || EditorUtility.IsPersistent(ghost))
                {
                    continue;
                }

                if (!hasRuntimeTarget)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Emas runtime state", EditorStyles.boldLabel);
                    hasRuntimeTarget = true;
                }

                if (targets.Length > 1)
                {
                    EditorGUILayout.LabelField(ghost.name, EditorStyles.miniBoldLabel);
                }

                Key key = ghost.Key;
                EditorGUILayout.LabelField("Key", key.Kind.IsValid ? key.ToString() : "Not initialized");
                EditorGUILayout.LabelField("Display name", ghost.Name ?? string.Empty);
                EditorGUILayout.LabelField("Variant", ghost.Variant.ToString());
                EditorGUILayout.LabelField("Available", ghost.IsAvailable ? "Yes" : "No");
            }
        }

        /// <inheritdoc />
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
