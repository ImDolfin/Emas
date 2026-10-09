using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    // Shared passive UI for the diagnostics window and Scene view overlay.
    internal sealed class DiagnosticsPanel
    {
        private Realm _selected;
        private Vector2 _scroll;
        private string _filter = string.Empty;
        private readonly HashSet<PresenceDetector> _expandedErrors = new HashSet<PresenceDetector>();

        internal void Draw(bool compact)
        {
            Realm[] realms = RealmRegistry.Snapshot();
            if (realms.Length == 0)
            {
                _selected = null;
                _expandedErrors.Clear();
                EditorGUILayout.HelpBox("No live Realms. Enable a configured Realm Setup in Play Mode, or create a Realm from code.", MessageType.Info);
                return;
            }

            SelectRealm(realms);
            IReadOnlyList<Anchor> anchors = _selected.Anchors;
            int failed = DrawSummary(anchors);
            if (compact)
            {
                if (failed > 0)
                {
                    EditorGUILayout.HelpBox("Detector failures need attention. Open diagnostics for context and details.", MessageType.Warning);
                }

                if (GUILayout.Button("Open Emas diagnostics"))
                {
                    DiagnosticsWindow.ShowWindow();
                }

                return;
            }

            DrawFilter();
            DrawAnchors(anchors);
        }

        private void SelectRealm(Realm[] realms)
        {
            // Query existing owners only; opening diagnostics must never create or start a default Realm.
            Realm defaultRealm;
            DefaultRuntime.TryGetRealm(out defaultRealm);
            RealmSetup[] setups = UnityEngine.Object.FindObjectsOfType<RealmSetup>(true);
            string[] names = new string[realms.Length];
            int selected = 0;
            for (int index = 0; index < realms.Length; index++)
            {
                names[index] = ReferenceEquals(realms[index], defaultRealm) ? "Default realm" : "Code realm " + (index + 1);
                foreach (RealmSetup setup in setups)
                {
                    if (ReferenceEquals(setup.Realm, realms[index]))
                    {
                        names[index] = setup.gameObject.scene.name + " / " + setup.name + " (#" + setup.GetInstanceID() + ")";
                        break;
                    }
                }
                if (ReferenceEquals(realms[index], _selected))
                {
                    selected = index;
                }
            }

            selected = EditorGUILayout.Popup(new GUIContent("Realm", "Live Realms only. Inspecting never starts tracking."), selected, names);
            _selected = realms[selected];
        }

        private int DrawSummary(IReadOnlyList<Anchor> anchors)
        {
            int active = 0;
            int stopped = 0;
            int failed = 0;
            foreach (Anchor anchor in anchors)
            {
                foreach (PresenceDetector detector in anchor.Detectors)
                {
                    if (detector.LastError != null)
                    {
                        failed++;
                    }
                    if (detector.IsActive)
                    {
                        active++;
                    }
                    else
                    {
                        stopped++;
                    }
                }
            }

            EditorGUILayout.LabelField(anchors.Count + " anchors  ?  " + _selected.Query().Count + " available entities", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(active + " active detectors  ?  " + stopped + " stopped  ?  " + failed + " errors", EditorStyles.wordWrappedMiniLabel);
            return failed;
        }

        private void DrawFilter()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Filter", GUILayout.Width(36f));
                _filter = GUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                {
                    _filter = string.Empty;
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawAnchors(IReadOnlyList<Anchor> anchors)
        {
            HashSet<PresenceDetector> errors = new HashSet<PresenceDetector>();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            bool any = false;
            foreach (Anchor anchor in anchors)
            {
                bool match = Matches(anchor.Id);
                foreach (PresenceDetector detector in anchor.Detectors)
                {
                    match |= Matches(detector.Name);
                }
                if (!match)
                {
                    continue;
                }

                any = true;
                using (InspectorLayout.Section(anchor.Id, _selected.Query().InAnchor(anchor.Id).Count + " available entities"))
                {
                    if (GUILayout.Button("Select Anchor", EditorStyles.miniButton))
                    {
                        Selection.activeGameObject = anchor.Transform.gameObject;
                        EditorGUIUtility.PingObject(anchor.Transform.gameObject);
                    }
                    if (anchor.Detectors.Count == 0)
                    {
                        EditorGUILayout.LabelField("No detector attached.", EditorStyles.wordWrappedMiniLabel);
                    }
                    foreach (PresenceDetector detector in anchor.Detectors)
                    {
                        DrawDetector(detector, errors);
                    }
                }
            }

            if (!any)
            {
                EditorGUILayout.HelpBox(anchors.Count == 0 ? "This Realm has no Anchors." : "No Anchors or detectors match the filter.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            // Stop retaining disposed or recovered detectors solely because their error foldout was once open.
            _expandedErrors.IntersectWith(errors);
        }

        private void DrawDetector(PresenceDetector detector, HashSet<PresenceDetector> errors)
        {
            using (InspectorLayout.Section(detector.Name))
            {
                IReadOnlyList<IGhost> ghosts = _selected.GetOwnedGhosts(detector);
                int available = 0;
                foreach (IGhost ghost in ghosts)
                {
                    if (ghost.IsAvailable)
                    {
                        available++;
                    }
                }

                InspectorLayout.ReadOnly("Status", detector.LastError != null ? "Failed" : detector.IsActive ? "Active" : "Stopped (attached)");
                InspectorLayout.ReadOnly("Available / owned", available + " / " + ghosts.Count);
                InspectorLayout.ReadOnly("Inactivity timeout (s)", detector.InactivityTimeout.HasValue ? detector.InactivityTimeout.Value.TotalSeconds.ToString("G") : "Off");
                InspectorLayout.ReadOnly("Disappearance grace (s)", detector.DisappearanceGracePeriod.TotalSeconds.ToString("G"));
                if (detector.LastError != null)
                {
                    errors.Add(detector);
                    EditorGUILayout.HelpBox(detector.LastErrorContext + "\n" + detector.LastError.Message, MessageType.Error);
                    bool expanded = EditorGUILayout.Foldout(_expandedErrors.Contains(detector), "Exception details", true);
                    if (expanded)
                    {
                        _expandedErrors.Add(detector);
                        EditorGUILayout.SelectableLabel(detector.LastError.ToString(), EditorStyles.textArea, GUILayout.MinHeight(100f));
                    }
                    else
                    {
                        _expandedErrors.Remove(detector);
                    }
                }
            }
        }

        private bool Matches(string value)
        {
            return string.IsNullOrEmpty(_filter) || (value != null && value.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
