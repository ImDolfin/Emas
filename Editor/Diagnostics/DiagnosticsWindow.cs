using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Passively inspects anchors, source health and available ghosts in the existing default realm.</summary>
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private Vector2 _scroll;

        /// <summary>Opens diagnostics; opening or refreshing never creates a realm or starts tracking.</summary>
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
            var anchors = Capture();
            if (anchors == null)
            {
                EditorGUILayout.HelpBox("No default realm exists. Enter Play Mode and start tracking to inspect it here.", MessageType.Info);
                return;
            }
            if (anchors.Count == 0)
            {
                EditorGUILayout.HelpBox("The default realm has no anchors.", MessageType.Info);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var anchor in anchors)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(anchor.Id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Available ghosts", anchor.Available.ToString());
                foreach (var source in anchor.Sources)
                {
                    EditorGUILayout.LabelField(source.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Status", source.Status);
                    EditorGUILayout.LabelField("Available / owned ghosts", source.Available + " / " + source.Owned);
                    if (source.Error != null)
                    {
                        EditorGUILayout.HelpBox(source.Error.ToString(), MessageType.Error);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        internal static IReadOnlyList<AnchorStatus> Capture()
        {
            Realm realm;
            if (!DefaultRuntime.TryGetRealm(out realm))
            {
                return null;
            }
            var result = new List<AnchorStatus>();
            foreach (var anchor in realm.Anchors)
            {
                var sources = new List<SourceStatus>();
                foreach (var source in anchor.Sources)
                {
                    var ghosts = realm.GetOwnedGhosts(source);
                    var available = 0;
                    foreach (var ghost in ghosts)
                    {
                        if (ghost.IsAvailable)
                        {
                            available++;
                        }
                    }
                    sources.Add(new SourceStatus
                    {
                        Name = source.GetType().Name,
                        Status = source.IsActive ? "Active" : source.IsAttached ? "Stopped (attached)" : "Detached",
                        Error = source.LastError,
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
            internal string Name;
            internal string Status;
            internal int Available;
            internal int Owned;
            internal Exception Error;
        }
    }
}
