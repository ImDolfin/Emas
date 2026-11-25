using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Shows editable Ghost traits, with identity and pose diagnostics during Play Mode.</summary>
    [CustomEditor(typeof(Ghost), true)]
    [CanEditMultipleObjects]
    public sealed class GhostInspector : UnityEditor.Editor
    {
        private TraitReordering _reordering;

        /// <summary>Draws authored settings, runtime data and root-trait settings without section foldouts.</summary>
        public override void OnInspectorGUI()
        {
            _reordering.BeginGUI();
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
                Ghost ghost = value as Ghost;
                if (ghost == null)
                {
                    continue;
                }
                if (targets.Length > 1)
                {
                    InspectorLayout.Header(ghost.name, null);
                }
                if (Application.isPlaying)
                {
                    DrawRuntime(ghost);
                }

                IReadOnlyList<Trait> traits = ghost.Traits;
                using (InspectorLayout.Section("Traits (" + traits.Count + ")",
                    "Drag a trait's grip to reorder it. Filters run from top to bottom. Disabled traits remain listed."))
                {
                    if (traits.Count == 0)
                    {
                        EditorGUILayout.LabelField("No traits on this root.", EditorStyles.miniLabel);
                    }
                    foreach (Trait trait in traits)
                    {
                        if (trait != null)
                        {
                            DrawTrait(ghost, trait);
                        }
                    }
                }

                _reordering.DrawListEdges(ghost, traits, GUILayoutUtility.GetLastRect());
            }

            if (Application.isPlaying)
            {
                using (InspectorLayout.Section("Pose debugger"))
                {
                    // Pin a single root. With several roots, let the pose tool use the current selection.
                    if (GUILayout.Button("Inspect poses in Emas"))
                    {
                        PoseTool.Instance.Open(targets.Length == 1 ? target as Ghost : null);
                    }
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
            _reordering = new TraitReordering(this);
            // Unity is still constructing its editor list here. Changing component
            // visibility now can rebuild that list inside its own creation or GUI pass.
            TraitInspectorVisibility.QueueRefresh();
        }

        private void OnDisable()
        {
            _reordering?.Cancel();
            // When the Ghost is removed, show its remaining traits in the Inspector again.
            TraitInspectorVisibility.QueueRefresh();
        }

        private static void DrawRuntime(Ghost ghost)
        {
            Key key = ghost.Key;
            bool tracked = key.Kind.IsValid;
            using (InspectorLayout.Section("Identity", "Read-only metadata supplied by the detector."))
            {
                InspectorLayout.ReadOnly("Status", !tracked ? "Not tracked" : ghost.IsAvailable ? "Available" : "Pending / unavailable");
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
            if (ghost.TryGet(out spatial))
            {
                DrawSpatial(ghost, spatial);
            }
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
                InspectorLayout.ReadOnly("Pose processing", !spatial.enabled ? "Application controlled"
                    : spatial.AttachedTo.HasValue ? "Inherited from parent"
                    : spatial.IsPoseProcessingBypassed ? "Direct (Realm distance)" : "Configured traits");
                InspectorLayout.ReadOnly("Absolute position", spatial.HasPosition ? spatial.Position.ToString() : "Not received");
                InspectorLayout.ReadOnly("Absolute rotation", spatial.HasRotation ? spatial.Rotation.ToString("F4") : "Not received");
                InspectorLayout.ReadOnly("Position sample time", SampleTime(spatial.PositionTime));
                InspectorLayout.ReadOnly("Rotation sample time", SampleTime(spatial.RotationTime));
                if (spatial.HasRotation)
                {
                    InspectorLayout.ReadOnly("Rotation space", spatial.RotationSpace.ToString());
                    if (spatial.RotationSpace == RotationSpace.Geographic)
                    {
                        DrawGeographicBank("Observed bank", spatial.Rotation);
                        ReferenceFrame frame = ghost.Anchor != null ? ghost.Anchor.Realm.ReferenceFrame : null;
                        if (frame != null && frame.FollowedGhost.HasValue && frame.FollowedGhost.Value.Equals(ghost.Key)
                            && frame.RotationSpace == RotationSpace.Geographic)
                        {
                            DrawGeographicBank("Reference bank", frame.Rotation);
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Unity position", ghost.transform.position);
                    EditorGUILayout.Vector3Field("Unity rotation (deg)", ghost.transform.eulerAngles);
                }
            }
        }

        private static void DrawGeographicBank(string label, Quaternion rotation)
        {
            // Named geographic attitudes already use local Unity axes. Their right/up heights
            // isolate bank from heading and pitch, with right-wing-down bank positive.
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float horizontalWeight = right.y * right.y + up.y * up.y;
            string value = horizontalWeight > 1e-8f
                ? (Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg).ToString("F3") + " deg (right-wing-down +)"
                : "Undefined at vertical pitch";
            InspectorLayout.ReadOnly(label, value);
        }

        private static string SampleTime(Timestamp? time)
        {
            return time.HasValue ? time.Value.Seconds + "." + time.Value.Nanoseconds.ToString("D9") : "Untimed / not received";
        }

        private void DrawTrait(Ghost ghost, Trait trait)
        {
            // Save each trait separately through Unity so Undo and prefab changes work.
            Rect card;
            using (EditorGUILayout.VerticalScope scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                card = scope.rect;
                using (SerializedObject settings = new SerializedObject(trait))
                {
                    settings.Update();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        Rect handle = GUILayoutUtility.GetRect(16f, EditorGUIUtility.singleLineHeight, GUILayout.Width(16f));
                        _reordering.DrawHandle(trait, handle);
                        SerializedProperty enabled = settings.FindProperty("m_Enabled");
                        EditorGUILayout.PropertyField(enabled, GUIContent.none, GUILayout.Width(18f));
                        EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(trait.GetType().Name), EditorStyles.boldLabel);
                        GUILayout.Label(enabled.boolValue ? "Enabled" : "Disabled", EditorStyles.miniLabel);
                        if (GUILayout.Button(new GUIContent("...", "Trait actions"), EditorStyles.miniButton, GUILayout.Width(24f)))
                        {
                            ShowTraitMenu(trait);
                        }
                    }

                    DrawTraitSettings(ghost, settings);
                    settings.ApplyModifiedProperties();
                }
            }

            _reordering.DrawCardTarget(ghost, trait, card);
        }

        private static void DrawTraitSettings(Ghost ghost, SerializedObject settings)
        {
            Extrapolation extrapolation = settings.targetObject as Extrapolation;
            if (extrapolation != null)
            {
                DrawPredictionDiagnostics(ghost, extrapolation);
            }
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
                    label = "Prediction limit (s)";
                }
                else if (settings.targetObject is Extrapolation && property.name == "_predictionTime")
                {
                    // The followed Ghost owns the shared override; peer fields remain
                    // available for use if that Ghost later becomes the reference.
                    label = "Prediction Interval (s)";
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

        private static void DrawPredictionDiagnostics(Ghost ghost, Extrapolation extrapolation)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // Read cached observations only. Inspecting a Ghost must never advance the
            // Realm, sample an SDK reader or change the reference's clock settings.
            Realm realm = ghost.Anchor != null ? ghost.Anchor.Realm : null;
            ReferenceFrame frame = realm != null ? realm.ReferenceFrame : null;
            IGhost followed;
            Spatial reference = null;
            bool shared = frame != null && frame.FollowedGhost.HasValue
                && realm.TryGetGhost(frame.FollowedGhost.Value, out followed) && followed.IsAvailable
                && followed.TryGet(out reference) && reference.enabled && !reference.AttachedTo.HasValue
                && (reference.PositionTime.HasValue || reference.RotationTime.HasValue);
            Spatial spatial;
            if (!ghost.TryGet(out spatial))
            {
                return;
            }

            string inactive = !extrapolation.enabled ? "Extrapolation disabled"
                : !spatial.enabled ? "Spatial disabled"
                : !ghost.IsAvailable ? "Ghost unavailable"
                : spatial.AttachedTo.HasValue ? "Inherited from parent"
                : spatial.IsPoseProcessingBypassed ? "Direct pose (Realm distance)"
                : extrapolation.Strategy is NoExtrapolation ? "Hold recorded poses" : null;
            if (inactive != null)
            {
                InspectorLayout.ReadOnly("Prediction status", inactive);
            }

            Timestamp? target = shared ? realm.ProjectedTime : null;
            if (shared)
            {
                InspectorLayout.ReadOnly("Projected time", target.HasValue ? SampleTime(target) : "Awaiting projection pass",
                    "The shared target captured by the last Realm projection pass, including playback delay and shared lead.");
                double? packetAge = realm.EstimatedPacketAge;
                InspectorLayout.ReadOnly("Packet age (ms)", packetAge.HasValue ? (packetAge.Value * 1000d).ToString("F1") : "Unavailable",
                    "The reference clock's median creation-to-receipt age after clock calibration. Automatic timing uses this estimate; manual timing retains it for diagnostics without applying it. It excludes playback delay, manual lead and smoothing. Unavailable without Unix clock alignment.");
            }
            if (target.HasValue)
            {
                // These are signed sample-to-target intervals, not measured display lag.
                // Negative values mean buffered playback; smoothing can still alter the pose.
                DrawPredictionOffset("Position to target (s)", spatial.PositionTime, target.Value,
                    extrapolation.MaximumExtrapolation);
                DrawPredictionOffset("Rotation to target (s)", spatial.RotationTime, target.Value,
                    extrapolation.MaximumExtrapolation);
            }
        }

        private static void DrawPredictionOffset(string label, Timestamp? sample, Timestamp target, float limit)
        {
            if (!sample.HasValue)
            {
                InspectorLayout.ReadOnly(label, "Untimed / not received");
                return;
            }

            double seconds = target.ElapsedSince(sample.Value);
            string status = seconds < 0 ? " (buffered)" : seconds > limit ? " (over limit)" : string.Empty;
            InspectorLayout.ReadOnly(label, seconds.ToString("F3") + status,
                "Signed interval from this channel's sample to the shared target. Negative means buffered playback. Over limit means prediction cannot reach that time with the current cap. This interval does not measure the final smoothed pose or display latency.");
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
