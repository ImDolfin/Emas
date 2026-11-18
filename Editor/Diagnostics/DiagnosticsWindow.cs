using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Provides pose comparison tools and passive tracking diagnostics for every live Realm.</summary>
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private DiagnosticsPanel _panel;

        /// <summary>Opens diagnostics without creating a Realm or starting tracking.</summary>
        [MenuItem("Window/Emas")]
        public static void ShowWindow()
        {
            DiagnosticsWindow window = GetWindow<DiagnosticsWindow>("Emas");
            window.minSize = new Vector2(360f, 300f);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (_panel == null)
            {
                _panel = new DiagnosticsPanel();
            }
            using (new EditorGUILayout.VerticalScope())
            {
                InspectorLayout.Header("Emas", "Pose comparison and live tracking. Package version " + Package.Version);
                EditorGUILayout.Space(6f);
                _panel.Draw(false);
            }
        }
    }
}
