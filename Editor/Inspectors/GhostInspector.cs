using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Shows Ghost identity, spatial presentation and editable root traits in open sections.</summary>
    [CustomEditor(typeof(Ghost), true)]
    [CanEditMultipleObjects]
    public sealed class GhostInspector : UnityEditor.Editor
    {
        private void OnEnable()
        {
            foreach (Object value in targets)
            {
                Ghost ghost = value as Ghost;
                if (ghost != null)
                {
                    TraitInspectorVisibility.Synchronize(ghost.gameObject);
                }
            }
        }

        private void OnDisable()
        {
            // Removing the owner must make any surviving trait components inspectable again.
            TraitInspectorVisibility.QueueRefresh();
        }

        /// <summary>Draws authored settings, runtime data and root-trait settings without section foldouts.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                Ghost ghost = (Ghost)value;
                Key key = ghost.Key;
                bool tracked = key.Kind.IsValid;
                if (targets.Length > 1)
                {
                    InspectorLayout.Header(ghost.name, null);
                }
                using (InspectorLayout.Section("Identity", "Read-only metadata supplied by the detector."))
                {
                    InspectorLayout.ReadOnly("Status", !tracked ? "Not tracked / prefab" : ghost.IsAvailable ? "Available" : "Pending / unavailable");
                    if (tracked)
                    {
                        InspectorLayout.ReadOnly("Anchor ID", key.AnchorId);
                        InspectorLayout.ReadOnly("Kind ID", key.Kind.Id);
                        InspectorLayout.ReadOnly("Entity ID", key.EntityId);
                        InspectorLayout.ReadOnly("Display name", ghost.Name);
                        InspectorLayout.ReadOnly("Variant", ghost.Variant == Variant.None ? "None (default)" : ghost.Variant.ToString());
                    }
                }

                Spatial spatial;
                if (ghost.TryGet<Spatial>(out spatial))
                {
                    DrawSpatial(ghost, spatial);
                }

                IReadOnlyList<Trait> traits = ghost.Traits;
                TraitInspectorVisibility.Synchronize(traits, true);
                using (InspectorLayout.Section("Traits (" + traits.Count + ")",
                    "Edit and enable root traits here. Disabled traits remain listed but skip bound reader updates."))
                {
                    if (traits.Count == 0)
                    {
                        EditorGUILayout.LabelField("No traits on this root.", EditorStyles.miniLabel);
                    }
                    foreach (Trait trait in traits)
                    {
                        if (trait != null)
                        {
                            DrawTrait(trait);
                        }
                    }
                }
            }
        }

        private static void DrawSpatial(Ghost ghost, Spatial spatial)
        {
            using (InspectorLayout.Section("Spatial pose", "Cached absolute input and the current projected Unity world pose."))
            {
                InspectorLayout.ReadOnly("Presentation", !spatial.enabled ? "Application controlled"
                    : !ghost.IsAvailable ? "Not projected / unavailable"
                    : !spatial.IsInRange ? "Suppressed / awaiting pose or parent"
                    : spatial.AttachedTo.HasValue ? "Attached" : "In range");
                InspectorLayout.ReadOnly("Attached to", spatial.AttachedTo.HasValue ? spatial.AttachedTo.Value.ToString() : "None");
                InspectorLayout.ReadOnly("Absolute position", spatial.HasPosition ? spatial.Position.ToString() : "Not received");
                InspectorLayout.ReadOnly("Absolute rotation", spatial.HasRotation ? spatial.Rotation.ToString("F4") : "Not received");
                if (spatial.HasRotation)
                {
                    InspectorLayout.ReadOnly("Rotation space", spatial.RotationSpace.ToString());
                }
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Unity position", ghost.transform.position);
                    EditorGUILayout.Vector3Field("Unity rotation (deg)", ghost.transform.eulerAngles);
                }
            }
        }

        private static void DrawTrait(Trait trait)
        {
            // Each trait owns separate serialized state; commit its edits through Unity's Undo/prefab pipeline.
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (SerializedObject settings = new SerializedObject(trait))
                {
                    settings.Update();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        SerializedProperty enabled = settings.FindProperty("m_Enabled");
                        EditorGUILayout.PropertyField(enabled, GUIContent.none, GUILayout.Width(18f));
                        EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(trait.GetType().Name), EditorStyles.boldLabel);
                        GUILayout.Label(enabled.boolValue ? "Enabled" : "Disabled", EditorStyles.miniLabel);
                        if (GUILayout.Button(new GUIContent("...", "Trait actions"), EditorStyles.miniButton, GUILayout.Width(24f)))
                        {
                            ShowTraitMenu(trait);
                        }
                    }

                    DrawPropertiesExcluding(settings, "m_Script", "m_Enabled");
                    settings.ApplyModifiedProperties();
                }
            }
        }

        private static void ShowTraitMenu(Trait trait)
        {
            GenericMenu menu = new GenericMenu();
            MonoScript script = MonoScript.FromMonoBehaviour(trait);
            if (script != null)
            {
                menu.AddItem(new GUIContent("Edit Script"), false, () => AssetDatabase.OpenAsset(script));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Edit Script"));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Remove Trait"), false, () =>
            {
                if (trait != null)
                {
                    Undo.DestroyObjectImmediate(trait);
                }
            });
            menu.ShowAsContext();
        }

        /// <summary>Refreshes live identity, pose and trait state while the application is playing.</summary>
        /// <returns>True in Play Mode; otherwise false.</returns>
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
