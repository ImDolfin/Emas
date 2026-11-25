using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Emas.Editor
{
    // Move real components, so saved authoring order and runtime filter order agree.
    internal sealed class TraitReordering
    {
        private readonly UnityEditor.Editor _owner;
        private Trait _candidate;
        private Vector2 _pressedAt;
        private int _control;
        private Trait _dropTarget;
        private bool _dropBefore;

        internal TraitReordering(UnityEditor.Editor owner)
        {
            _owner = owner;
        }

        internal void BeginGUI()
        {
            if (Event.current.type == EventType.DragExited)
            {
                _dropTarget = null;
                _owner.Repaint();
            }
        }

        internal void Cancel()
        {
            if (_control != 0 && GUIUtility.hotControl == _control)
            {
                GUIUtility.hotControl = 0;
            }

            _control = 0;
            _candidate = null;
            _dropTarget = null;
        }

        internal void DrawHandle(Trait trait, Rect rect)
        {
            GUI.Label(rect, new GUIContent("\u2261", "Drag to reorder traits. Filters run from top to bottom."), EditorStyles.centeredGreyMiniLabel);
            if (!Editable(trait) || !GUI.enabled)
            {
                return;
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            int control = GUIUtility.GetControlID(trait.GetInstanceID(), FocusType.Passive, rect);
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                _candidate = trait;
                _pressedAt = current.mousePosition;
                _control = control;
                GUIUtility.hotControl = control;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == control && _candidate == trait)
            {
                if ((current.mousePosition - _pressedAt).sqrMagnitude >= 16f)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new UnityEngine.Object[] { trait };
                    DragAndDrop.StartDrag("Reorder " + ObjectNames.NicifyVariableName(trait.GetType().Name));
                    Cancel();
                }

                current.Use();
            }
            else if (current.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                Cancel();
                current.Use();
            }
        }

        internal void DrawCardTarget(Ghost ghost, Trait trait, Rect rect)
        {
            HandleDrop(ghost, trait, rect, null);
            if (Event.current.type == EventType.Repaint && _dropTarget == trait)
            {
                float y = _dropBefore ? rect.yMin : rect.yMax;
                EditorGUI.DrawRect(new Rect(rect.xMin, y - 1f, rect.width, 2f), new Color(0.2f, 0.6f, 1f));
            }
        }

        internal void DrawListEdges(Ghost ghost, System.Collections.Generic.IReadOnlyList<Trait> traits, Rect rect)
        {
            if (traits.Count > 0)
            {
                // Include the section heading and bottom padding as generous edge targets.
                HandleDrop(ghost, traits[0], new Rect(rect.xMin, rect.yMin, rect.width, EditorGUIUtility.singleLineHeight + 8f), true);
                HandleDrop(ghost, traits[traits.Count - 1], new Rect(rect.xMin, rect.yMax - 12f, rect.width, 12f), false);
            }
        }

        private void HandleDrop(Ghost ghost, Trait target, Rect rect, bool? before)
        {
            Event current = Event.current;
            if ((current.type != EventType.DragUpdated && current.type != EventType.DragPerform) || !rect.Contains(current.mousePosition))
            {
                return;
            }

            UnityEngine.Object[] references = DragAndDrop.objectReferences;
            Trait source = references.Length == 1 ? references[0] as Trait : null;
            if (!Editable(source) || !Editable(target) || source == target || ghost == null
                || source.gameObject != ghost.gameObject || target.gameObject != ghost.gameObject)
            {
                _dropTarget = null;
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                _owner.Repaint();
                return;
            }

            _dropTarget = target;
            _dropBefore = before ?? current.mousePosition.y < rect.center.y;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (current.type == EventType.DragPerform)
            {
                bool insertBefore = _dropBefore;
                DragAndDrop.AcceptDrag();
                _dropTarget = null;
                // Changing component order inside OnGUI can rebuild Unity's Inspector list
                // while its layout is in progress. Apply the accepted drop after that pass.
                EditorApplication.CallbackFunction apply = null;
                apply = () =>
                {
                    EditorApplication.update -= apply;
                    Move(source, target, insertBefore);
                };
                EditorApplication.update += apply;
            }

            current.Use();
            _owner.Repaint();
        }

        private static bool Editable(Trait trait)
        {
            return trait != null && (trait.hideFlags & HideFlags.NotEditable) == 0
                && (trait.gameObject.hideFlags & HideFlags.NotEditable) == 0
                && !PrefabUtility.IsPartOfImmutablePrefab(trait);
        }

        private static void Move(Trait source, Trait target, bool before)
        {
            if (!Editable(source) || !Editable(target) || source.gameObject != target.gameObject)
            {
                return;
            }

            GameObject root = source.gameObject;
            Component[] components = root.GetComponents<Component>();
            int sourceIndex = Array.IndexOf(components, source);
            int destination = Array.IndexOf(components, target) + (before ? 0 : 1);
            if (sourceIndex < destination)
            {
                destination--;
            }

            if (sourceIndex == destination)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Reorder Ghost traits");
            Component[] hidden = Array.FindAll(components, component => component != null
                && (component.hideFlags & HideFlags.HideInInspector) != 0);
            HideFlags[] flags = new HideFlags[hidden.Length];
            for (int index = 0; index < hidden.Length; index++)
            {
                flags[index] = hidden[index].hideFlags;
            }

            // Use Unity's component authoring operations rather than copying or recreating
            // traits. References, bindings, enabled states and non-trait components survive.
            try
            {
                // Unity's move command will not cross a hidden component. Reveal components
                // only within this deferred operation, then restore their exact flags.
                // Record both flag transitions so Undo/Redo cannot replay the temporary visibility.
                if (hidden.Length > 0)
                {
                    Undo.RegisterCompleteObjectUndo(hidden, "Reorder Ghost traits");
                }

                for (int index = 0; index < hidden.Length; index++)
                {
                    hidden[index].hideFlags &= ~HideFlags.HideInInspector;
                }

                Undo.FlushUndoRecordObjects();

                while (sourceIndex != destination)
                {
                    bool moved = sourceIndex > destination ? ComponentUtility.MoveComponentUp(source) : ComponentUtility.MoveComponentDown(source);
                    int previous = sourceIndex;
                    sourceIndex = Array.IndexOf(root.GetComponents<Component>(), source);
                    if (!moved || sourceIndex == previous || (previous > destination && sourceIndex < destination)
                        || (previous < destination && sourceIndex > destination))
                    {
                        Undo.RevertAllDownToGroup(group);
                        return;
                    }
                }
            }
            finally
            {
                if (hidden.Length > 0)
                {
                    Undo.RegisterCompleteObjectUndo(hidden, "Reorder Ghost traits");
                }

                for (int index = 0; index < hidden.Length; index++)
                {
                    if (hidden[index] != null)
                    {
                        hidden[index].hideFlags = flags[index];
                    }
                }

                Undo.FlushUndoRecordObjects();
            }

            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            }

            EditorUtility.SetDirty(root);
            Undo.CollapseUndoOperations(group);
            TraitInspectorVisibility.QueueRefresh();
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
