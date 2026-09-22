using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>
    /// Passively inspects anchors, source health and available ghosts in the existing default realm.
    /// </summary>
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;
        private readonly HashSet<PresenceSource> _expandedErrors = new HashSet<PresenceSource>();

        /// <summary>
        /// Opens diagnostics; opening or refreshing never creates a realm or starts tracking.
        /// </summary>
        [MenuItem("Window/Emas")]
        public static void ShowWindow()
        {
            GetWindow<DiagnosticsWindow>("Emas");
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            GUILayout.Label("Emas v" + Package.Version + " - Default realm", EditorStyles.boldLabel);
            IReadOnlyList<AnchorStatus> anchors = Capture();
            if (anchors == null)
            {
                _expandedErrors.Clear();
                EditorGUILayout.HelpBox("No default realm exists. Enter Play Mode and start tracking to inspect it here.", MessageType.Info);
                return;
            }

            if (anchors.Count == 0)
            {
                EditorGUILayout.HelpBox("The default realm has no anchors.", MessageType.Info);
            }

            HashSet<PresenceSource> failedSources = new HashSet<PresenceSource>();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (AnchorStatus anchor in anchors)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(anchor.Id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Available ghosts", anchor.Available.ToString());
                foreach (SourceStatus source in anchor.Sources)
                {
                    EditorGUILayout.LabelField(source.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Status", source.Status);
                    EditorGUILayout.LabelField("Available / owned ghosts", source.Available + " / " + source.Owned);
                    if (source.Error != null)
                    {
                        failedSources.Add(source.Source);
                        EditorGUILayout.HelpBox(source.ErrorContext + "\n" + source.Error.Message, MessageType.Error);
                        bool expanded = EditorGUILayout.Foldout(_expandedErrors.Contains(source.Source), "Exception details", true);
                        if (expanded)
                        {
                            _expandedErrors.Add(source.Source);
                            EditorGUILayout.SelectableLabel(source.Error.ToString(), EditorStyles.textArea, GUILayout.MinHeight(100f));
                        }
                        else
                        {
                            _expandedErrors.Remove(source.Source);
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            _expandedErrors.IntersectWith(failedSources);
        }

        internal static IReadOnlyList<AnchorStatus> Capture()
        {
            Realm realm;
            if (!DefaultRuntime.TryGetRealm(out realm))
            {
                return null;
            }

            List<AnchorStatus> result = new List<AnchorStatus>();
            foreach (Anchor anchor in realm.Anchors)
            {
                List<SourceStatus> sources = new List<SourceStatus>();
                foreach (PresenceSource source in anchor.Sources)
                {
                    IReadOnlyList<IGhost> ghosts = realm.GetOwnedGhosts(source);
                    int available = 0;
                    foreach (IGhost ghost in ghosts)
                    {
                        if (ghost.IsAvailable)
                        {
                            available++;
                        }
                    }

                    sources.Add(new SourceStatus
                    {
                        Source = source,
                        Name = source.Name,
                        Status = source.IsActive ? "Active" : source.IsAttached ? "Stopped (attached)" : "Detached",
                        Error = source.LastError,
                        ErrorContext = source.LastErrorContext,
                        Available = available,
                        Owned = ghosts.Count
                    });
                }

                result.Add(new AnchorStatus
                {
                    Id = anchor.Id,
                    Available = realm.Query().InAnchor(anchor.Id).Count,
                    Sources = sources.AsReadOnly()
                });
            }

            return result.AsReadOnly();
        }

        internal sealed class AnchorStatus
        {
            internal string Id;
            internal int Available;
            internal IReadOnlyList<SourceStatus> Sources;
        }

        internal sealed class SourceStatus
        {
            internal PresenceSource Source;
            internal string Name;
            internal string Status;
            internal string ErrorContext;
            internal int Available;
            internal int Owned;
            internal Exception Error;
        }
    }
}
