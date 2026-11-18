using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Keeps root-trait component cards hidden while their Ghost supplies the authoring controls.</summary>
    [InitializeOnLoad]
    internal static class TraitInspectorVisibility
    {
        private static bool _refreshQueued;

        static TraitInspectorVisibility()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            Undo.undoRedoPerformed += QueueRefresh;
            Selection.selectionChanged += SynchronizeSelection;
            QueueRefresh();
        }

        internal static void Synchronize(GameObject root)
        {
            if (root != null)
            {
                Synchronize(root.GetComponents<Trait>(), root.GetComponent<Ghost>() != null);
            }
        }

        internal static void Synchronize(IReadOnlyList<Trait> traits, bool hasGhost)
        {
            for (int index = 0; index < traits.Count; index++)
            {
                SetVisibility(traits[index], hasGhost);
            }
        }

        internal static void QueueRefresh()
        {
            // Group object changes so one edit or Undo action scans loaded traits only once.
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
            EditorApplication.delayCall += RefreshLoadedTraits;
        }

        private static void OnHierarchyChanged()
        {
            // Do not scan the whole scene each frame when tracked objects change.
            // GhostInspector refreshes the traits of the roots being inspected.
            if (!EditorApplication.isPlaying)
            {
                QueueRefresh();
            }
        }

        private static void OnObjectChanges(ref ObjectChangeEventStream changes)
        {
            // Scan only when components or prefabs change. Editing a value does not change who owns a trait.
            for (int index = 0; index < changes.length; index++)
            {
                ObjectChangeKind kind = changes.GetEventType(index);
                if (kind == ObjectChangeKind.ChangeGameObjectStructure ||
                    kind == ObjectChangeKind.ChangeGameObjectStructureHierarchy ||
                    kind == ObjectChangeKind.UpdatePrefabInstances)
                {
                    QueueRefresh();
                    return;
                }
            }
        }

        private static void SynchronizeSelection()
        {
            foreach (GameObject root in Selection.gameObjects)
            {
                Synchronize(root);
            }
        }

        private static void RefreshLoadedTraits()
        {
            _refreshQueued = false;
            // Include inactive roots, prefab assets and Prefab Mode. Also fix old flags saved
            // before scripts reloaded or a Ghost changed while no Inspector was open.
            foreach (Trait trait in Resources.FindObjectsOfTypeAll<Trait>())
            {
                if (trait != null)
                {
                    SetVisibility(trait, trait.GetComponent<Ghost>() != null);
                }
            }
        }

        private static void SetVisibility(Trait trait, bool hasGhost)
        {
            if (trait == null)
            {
                return;
            }

            // Change only Inspector visibility, leaving NotEditable and DontSave alone. Show any
            // remaining traits again when their Ghost is removed so their settings stay accessible.
            HideFlags flags = hasGhost ? trait.hideFlags | HideFlags.HideInInspector
                : trait.hideFlags & ~HideFlags.HideInInspector;
            if (trait.hideFlags != flags)
            {
                trait.hideFlags = flags;
            }
        }
    }
}
