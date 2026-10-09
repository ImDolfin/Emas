using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Shows Anchor settings, component connections and runtime health.</summary>
    [CustomEditor(typeof(AnchorSetup))]
    [CanEditMultipleObjects]
    public sealed class AnchorSetupInspector : UnityEditor.Editor
    {
        /// <summary>Draws concise authoring controls and read-only connections.</summary>
        public override void OnInspectorGUI()
        {
            // Serialized editing preserves Undo, prefab overrides and mixed values across selected objects.
            serializedObject.Update();
            bool attached = false;
            foreach (Object value in targets)
            {
                attached |= ((AnchorSetup)value).Anchor != null;
            }
            using (new EditorGUI.DisabledScope(attached))
            {
                InspectorLayout.Field(serializedObject, "_anchorId", "Anchor ID");
                InspectorLayout.Field(serializedObject, "_automaticViews", "Automatic views");
            }
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                AnchorSetup setup = (AnchorSetup)value;
                using (InspectorLayout.Section(targets.Length > 1 ? setup.name : "Connections"))
                {
                    InspectorLayout.Object("Realm", setup.AttachmentOwner != null ? setup.AttachmentOwner : setup.Owner());
                    foreach (MonoBehaviour component in setup.GetComponents<MonoBehaviour>())
                    {
                        if (component is IDetectorProvider)
                        {
                            InspectorLayout.Object("Detector", component);
                        }
                    }
                    GhostInitializer initializer = setup.GetComponent<GhostInitializer>();
                    if (initializer != null)
                    {
                        InspectorLayout.Object("Ghost initializer", initializer);
                    }
                }
                if (setup.Anchor != null)
                {
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
                else if (!setup.WantsAttachment)
                {
                    InspectorLayout.ReadOnly("Status", "Detector disabled");
                }
                else
                {
                    string error = setup.GetConfigurationError();
                    if (error != null)
                    {
                        EditorGUILayout.HelpBox(error, MessageType.Error);
                    }
                    if (setup.Owner() == null)
                    {
                        EditorGUILayout.HelpBox("Add Realm Setup to this object or a parent.", MessageType.Warning);
                    }
                }
            }
        }

        /// <summary>Keeps detector health and entity counts current while the application is playing.</summary>
        /// <returns>True in Play Mode; otherwise false.</returns>
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
