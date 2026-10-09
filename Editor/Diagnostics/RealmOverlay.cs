using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Emas.Editor
{
    /// <summary>Provides an optional passive Realm summary in the Scene view overlays menu.</summary>
    [Overlay(typeof(SceneView), "Emas", false)]
    public sealed class RealmOverlay : Overlay
    {
        /// <summary>Creates the compact live summary without starting a Realm.</summary>
        /// <returns>A periodically repainted IMGUI element backed by a passive diagnostics panel.</returns>
        public override VisualElement CreatePanelContent()
        {
            DiagnosticsPanel panel = new DiagnosticsPanel();
            IMGUIContainer content = new IMGUIContainer(() => panel.Draw(true));
            content.style.minWidth = 260f;
            content.style.maxWidth = 340f;
            // Schedule visual refreshes on this element rather than taking ownership of Realm updates.
            content.schedule.Execute(() => content.MarkDirtyRepaint()).Every(250);
            return content;
        }
    }
}
