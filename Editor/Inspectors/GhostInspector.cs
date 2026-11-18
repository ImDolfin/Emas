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
        /// <summary>Draws authored settings, runtime data and root-trait settings without section foldouts.</summary>
        public override void OnInspectorGUI()
        {
            // Save the root's edits first, then edit and save each trait separately.
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
            foreach (Object value in targets)
            {
                // Read each root's own metadata and traits when several objects are selected.
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

            using (InspectorLayout.Section("Pose debugger"))
            {
                // Pin a single root. With several roots, let the pose tool use the current selection.
                if (GUILayout.Button("Inspect poses in Emas"))
                {
                    PoseTool.Instance.Open(targets.Length == 1 ? target as Ghost : null);
                }
            }
        }

        /// <summary>Refreshes live identity, pose and trait state while the application is playing.</summary>
        /// <returns>True in Play Mode; otherwise false.</returns>
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

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
            // When the Ghost is removed, show its remaining traits in the Inspector again.
            TraitInspectorVisibility.QueueRefresh();
        }

        private static void DrawSpatial(Ghost ghost, Spatial spatial)
        {
            // Show the last source values beside the displayed pose without reading the source again.
            using (InspectorLayout.Section("Spatial pose", "Cached absolute input and the current projected Unity world pose."))
            {
                InspectorLayout.ReadOnly("Presentation", !spatial.enabled ? "Application controlled"
                    : !ghost.IsAvailable ? "Not projected / unavailable"
                    : !spatial.IsInRange ? "Suppressed / awaiting pose or parent"
                    : spatial.AttachedTo.HasValue ? "Attached" : "In range");
                InspectorLayout.ReadOnly("Attached to", spatial.AttachedTo.HasValue ? spatial.AttachedTo.Value.ToString() : "None");
                InspectorLayout.ReadOnly("Absolute position", spatial.HasPosition ? spatial.Position.ToString() : "Not received");
                InspectorLayout.ReadOnly("Absolute rotation", spatial.HasRotation ? spatial.Rotation.ToString("F4") : "Not received");
                InspectorLayout.ReadOnly("Position sample time", SampleTime(spatial.PositionTime));
                InspectorLayout.ReadOnly("Rotation sample time", SampleTime(spatial.RotationTime));
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

        private static string SampleTime(Timestamp? time)
        {
            return time.HasValue ? time.Value.Seconds + "." + time.Value.Nanoseconds.ToString("D9") : "Untimed / not received";
        }

        private static void DrawTrait(Trait trait)
        {
            // Save each trait separately through Unity so Undo and prefab changes work.
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

                    DrawTraitSettings(settings);
                    settings.ApplyModifiedProperties();
                }
            }
        }

        private static void DrawTraitSettings(SerializedObject settings)
        {
            SerializedProperty property = settings.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "m_Script" || property.name == "m_Enabled")
                {
                    continue;
                }

                string label = property.displayName;
                if (settings.targetObject is Extrapolation && property.name == "_maximumExtrapolation")
                {
                    label = "Prediction horizon (s)";
                }
                else if (settings.targetObject is Smoothing)
                {
                    if (property.name == "_positionHalfLife")
                    {
                        label = "Position half-life (s)";
                    }
                    else if (property.name == "_rotationHalfLife")
                    {
                        label = "Rotation half-life (s)";
                    }
                }

                EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
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
    }
}
