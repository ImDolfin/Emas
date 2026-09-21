using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Shows live Emas diagnostics while the Unity editor is in Play Mode.</summary>
    public class DiagnosticsWindow : EditorWindow
    {
        /// <summary>Opens the Emas diagnostics window.</summary>
        [MenuItem("Window/Emas")]
        public static void ShowWindow()
        {
            GetWindow<DiagnosticsWindow>("Emas");
        }

        private void OnGUI()
        {
            GUILayout.Label("Emas v" + Package.Version, EditorStyles.boldLabel);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect the default Emas context.", MessageType.Info);
                return;
            }

            var context = Context.Default;
            EditorGUILayout.LabelField("Available ghosts", context.Query().Count.ToString());
            EditorGUILayout.LabelField("Tracked car-like data", "Use application kinds and interfaces in the query API.");
            if (GUILayout.Button("Refresh"))
            {
                Repaint();
            }
        }
    }
}
