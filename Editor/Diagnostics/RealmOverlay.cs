using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Emas.Editor
{
    /// <summary>Provides pose comparison controls and a passive Realm summary in the Scene view overlays menu.</summary>
    [Overlay(typeof(SceneView), "Emas", false)]
    public sealed class RealmOverlay : Overlay
    {
        /// <summary>Creates the compact live summary without starting a Realm.</summary>
        /// <returns>A periodically repainted IMGUI element sharing the pose tool's settings and passive diagnostics.</returns>
        public override VisualElement CreatePanelContent()
        {
            DiagnosticsPanel panel = new DiagnosticsPanel();
            IMGUIContainer content = new IMGUIContainer(() => panel.Draw(true));
            content.style.minWidth = 260f;
            content.style.maxWidth = 340f;
            // Refresh this panel on a schedule. Leave Realm updates to the application.
            content.schedule.Execute(() => content.MarkDirtyRepaint()).Every(250);
            return content;
        }
    }
}
