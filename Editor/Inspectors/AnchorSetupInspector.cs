using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Groups Anchor identity, provider wiring and read-only runtime health.</summary>
    [CustomEditor(typeof(AnchorSetup))]
    [CanEditMultipleObjects]
    public sealed class AnchorSetupInspector : UnityEditor.Editor
    {
        /// <summary>Draws editable startup settings and explains provider requirements.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool attached = false;
            foreach (Object value in targets)
            {
                attached |= ((AnchorSetup)value).Anchor != null;
            }
            InspectorLayout.Header("Anchor", "Groups entity identities and attaches a detector to a Realm.");
            if (attached)
            {
                EditorGUILayout.HelpBox("Attached ? settings are read-only. Disable this Anchor Setup before changing its attachment settings.", MessageType.Info);
            }
            using (new EditorGUI.DisabledScope(attached))
            {
                using (InspectorLayout.Section("Identity", "A non-empty ID, unique among Anchors in this Realm. Entity IDs only need to be unique within their Anchor and Kind."))
                {
                    InspectorLayout.Field(serializedObject, "_anchorId", "Anchor ID");
                }
                using (InspectorLayout.Section("Presentation", "Ghost prefabs and variants come from the Realm's Kind mappings."))
                {
                    InspectorLayout.Field(serializedObject, "_automaticViews", "Automatic views");
                    EditorGUILayout.LabelField("Off: use Realm.Manifest / Demanifest from application code.", EditorStyles.wordWrappedMiniLabel);
                }
            }
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                AnchorSetup setup = (AnchorSetup)value;
                using (InspectorLayout.Section(targets.Length > 1 ? "Connections ? " + setup.name : "Connections",
                    "Add one enabled component implementing IDetectorProvider to this GameObject. Its CreateDetector() supplies the detector."))
                {
                    InspectorLayout.Object("Realm owner", setup.AttachmentOwner != null ? setup.AttachmentOwner : setup.Owner());
                    int providers = 0;
                    foreach (MonoBehaviour component in setup.GetComponents<MonoBehaviour>())
                    {
                        if (component is IDetectorProvider)
                        {
                            providers++;
                            InspectorLayout.Object("Detector provider", component);
                            InspectorLayout.ReadOnly("Provider state", component.isActiveAndEnabled ? "Enabled" : "Disabled");
                        }
                    }
                    if (providers == 0)
                    {
                        EditorGUILayout.HelpBox("Missing detector provider. Implement IDetectorProvider on a component beside this Anchor Setup.", MessageType.Warning);
                    }
                    EditorGUILayout.LabelField("Optional: implement IRealmConfigurator to bind Ghost modules before detectors start.", EditorStyles.wordWrappedMiniLabel);
                }
                if (setup.Anchor != null)
                {
                    using (InspectorLayout.Section("Live attachment ? " + setup.name))
                    {
                        InspectorLayout.ReadOnly("Anchor ID", setup.Anchor.Id);
                        InspectorLayout.ReadOnly("Available entities", setup.Anchor.Realm.Query().InAnchor(setup.Anchor.Id).Count.ToString());
                        foreach (PresenceDetector detector in setup.Anchor.Detectors)
                        {
                            InspectorLayout.ReadOnly(detector.Name, detector.IsActive ? "Active" : "Stopped");
                            if (detector.LastError != null)
                            {
                                EditorGUILayout.HelpBox(detector.LastErrorContext + "\n" + detector.LastError.Message, MessageType.Error);
                            }
                        }
                    }
                }
                else
                {
                    string error = setup.GetConfigurationError();
                    if (error != null)
                    {
                        EditorGUILayout.HelpBox(setup.name + ": " + error, MessageType.Error);
                    }
                    if (setup.Owner() == null)
                    {
                        EditorGUILayout.HelpBox("Add Realm Setup to this object or one of its parents.", MessageType.Warning);
                    }
                }
            }
            InspectorLayout.DiagnosticsButton();
        }

        /// <inheritdoc />
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
