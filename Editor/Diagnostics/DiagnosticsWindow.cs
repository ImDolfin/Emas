using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Passively inspects every live Realm, its Anchors and detector health.</summary>
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private DiagnosticsPanel _panel;

        /// <summary>Opens diagnostics without creating a Realm or starting tracking.</summary>
        [MenuItem("Window/Emas")]
        public static void ShowWindow()
        {
            DiagnosticsWindow window = GetWindow<DiagnosticsWindow>("Emas diagnostics");
            window.minSize = new Vector2(340f, 220f);
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
                InspectorLayout.Header("Emas diagnostics", "Live state ? read-only ? v" + Package.Version);
                EditorGUILayout.Space(6f);
                _panel.Draw(false);
            }
        }
    }
}
